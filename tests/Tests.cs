using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace RdpTunnel
{
    internal static class Tests
    {
        private static int assertions;
        private static readonly List<string> logs = new List<string>();
        private static void Log(string text) { lock (logs) logs.Add(text); }
        private static void Check(bool value, string name)
        { if (!value) throw new Exception("FAIL: " + name); assertions++; Console.WriteLine("PASS: " + name); }
        private static void Reject(Action action, string name)
        { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
        private static int FreePort()
        { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
        private static async Task Until(Func<bool> condition, string label)
        { var end = DateTime.UtcNow.AddSeconds(20); while (!condition() && DateTime.UtcNow < end) await Task.Delay(50); Check(condition(), label); }

        public static int Main(string[] args)
        {
            try { Run(args.Contains("--crypto-only")).GetAwaiter().GetResult(); Console.WriteLine("SUCCESS: " + assertions + " checks"); return 0; }
            catch (Exception error)
            {
                Console.WriteLine(error);
                lock (logs) foreach (var line in logs) Console.WriteLine("LOG: " + line);
                return 1;
            }
        }

        private static async Task Run(bool cryptoOnly)
        {
            var config = Security.CreatePair("127.0.0.1", FreePort(), FreePort(), FreePort(), FreePort(), "127.0.0.1");
            config.ListenPort = config.PublicPort;
            using (var cert = Security.LoadCertificate(config.RelayPfx))
            {
                Check(Security.Matches(cert, config.RelayHash), "Relay certificate pin accepted");
                Check(!Security.Matches(cert, config.AgentHash), "Wrong certificate pin rejected");
            }
            const string password = "Test-only-passphrase-1234";
            var encrypted = Security.Export(config, password);
            var imported = Security.Import(encrypted, password);
            Check(imported.RelayPfx == null && imported.AgentPfx.Length > 0, "Enrollment excludes relay private key");
            Check(imported.RelayHash == config.RelayHash && imported.AgentHash == config.AgentHash, "Enrollment identity round-trip");
            Reject(() => Security.Import(encrypted, "incorrect"), "Wrong enrollment password rejected");
            var changed = (byte[])encrypted.Clone(); changed[80] ^= 1;
            Reject(() => Security.Import(changed, password), "Tampered encrypted enrollment rejected");
            Reject(() => Security.Import(new byte[200000], password), "Oversized enrollment rejected");
            var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-output"); Directory.CreateDirectory(folder);
            var settingsFile = Path.Combine(folder, "test-settings.bin"); config.Save(settingsFile);
            Check(Settings.Load(settingsFile).RelayHash == config.RelayHash, "DPAPI current-user persistence round-trip");
            File.Delete(settingsFile);
            if (cryptoOnly) { Console.WriteLine("Network checks explicitly skipped in this restricted environment."); return; }

            using (var rdp = new Echo("RDP"))
            using (var ssh = new Echo("SSH"))
            {
                config.SshPort = ssh.Port;
                var agentSettings = config.AgentCopy();
                using (var relay = new Relay(config, Log))
                using (var agent = new Agent(agentSettings, Log, rdp.Port))
                {
                    relay.Start(); var agentTask = agent.Start();
                    await Until(() => relay.AgentConnected && agent.Connected, "Mutual TLS tunnel connects");
                    await Transfer(config.LocalRdpPort, "RDP", 1048576);
                    Check(true, "RDP route: 1 MiB bidirectional binary integrity");
                    await Transfer(config.LocalSshPort, "SSH", 1048576);
                    Check(true, "SSH route: 1 MiB bidirectional binary integrity");
                    await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Transfer(i % 2 == 0 ? config.LocalRdpPort : config.LocalSshPort, i % 2 == 0 ? "RDP" : "SSH", 262144)));
                    Check(true, "Four concurrent RDP/SSH streams remain isolated");
                    await Until(() => relay.ActiveSessions == 0 && agent.ActiveSessions == 0, "Closed sessions release slots");

                    // Malformed authenticated data session cannot attach to an arbitrary stream.
                    using (var cert = Security.LoadCertificate(agentSettings.AgentPfx))
                    using (var socket = new TcpClient())
                    {
                        await socket.ConnectAsync("127.0.0.1", config.ListenPort);
                        using (var ssl = new SslStream(socket.GetStream(), false, (s,c,ch,e) => Security.Matches(c, config.RelayHash), (s,h,l,r,i) => cert))
                        {
                            ssl.ReadTimeout = 5000; ssl.WriteTimeout = 5000;
                            ssl.AuthenticateAsClient("RdpTunnel", new X509CertificateCollection { cert }, SslProtocols.Tls12, false);
                            var wire = new Wire(socket, ssl); wire.Send("RDT1 DATA " + Guid.NewGuid().ToString("N"));
                            bool closed = false; try { closed = ssl.ReadByte() == -1; } catch (IOException) { closed = true; }
                            Check(closed, "Unknown data session rejected");
                        }
                    }

                    // A second certificate is rejected even if it knows the relay address.
                    var impostorSettings = Security.CreatePair("127.0.0.1", config.PublicPort, config.ListenPort, config.LocalRdpPort, config.LocalSshPort, "127.0.0.1");
                    impostorSettings.RelayHash = config.RelayHash; impostorSettings.RelayPfx = null;
                    using (var impostor = new Agent(impostorSettings, Log, rdp.Port))
                    {
                        var impostorTask = impostor.Start(); await Task.Delay(1500);
                        Check(!impostor.Connected && agent.Connected, "Unpaired client certificate rejected without displacing agent");
                        impostor.Stop(); await impostorTask;
                    }

                    relay.Stop(); await Until(() => !agent.Connected, "Agent notices relay shutdown");
                    using (var restarted = new Relay(config, Log))
                    {
                        restarted.Start(); await Until(() => restarted.AgentConnected && agent.Connected, "Agent reconnects automatically after relay restart");
                        await Transfer(config.LocalSshPort, "SSH", 65536);
                        Check(true, "SSH works after reconnection");
                        agent.Stop(); await agentTask;
                        await Until(() => !restarted.AgentConnected, "Stop agent closes authenticated channel");
                        using (var client = new TcpClient())
                        {
                            await client.ConnectAsync(IPAddress.Loopback, config.LocalRdpPort);
                            client.ReceiveTimeout = 3000;
                            Check(client.GetStream().ReadByte() == -1, "Local connection rejected when agent is offline");
                        }
                    }
                }
            }
        }

        private static async Task Transfer(int port, string marker, int size)
        {
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                using (var stream = client.GetStream())
                {
                    var label = new byte[4]; await ReadExactly(stream, label);
                    if (System.Text.Encoding.ASCII.GetString(label) != marker + "\n") throw new Exception("Wrong target route");
                    var bytes = Security.RandomBytes(size); var received = new byte[size];
                    var transfer = Task.WhenAll(stream.WriteAsync(bytes, 0, bytes.Length), ReadExactly(stream, received));
                    if (await Task.WhenAny(transfer, Task.Delay(15000)) != transfer) throw new TimeoutException("Binary transfer stalled");
                    await transfer;
                    if (!bytes.SequenceEqual(received)) throw new Exception("Corrupted tunnel payload");
                }
            }
        }
        private static async Task ReadExactly(Stream stream, byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length) { int count = await stream.ReadAsync(buffer, offset, buffer.Length - offset); if (count == 0) throw new EndOfStreamException(); offset += count; }
        }

        private sealed class Echo : IDisposable
        {
            private readonly TcpListener listener;
            private readonly List<TcpClient> clients = new List<TcpClient>();
            private readonly string marker;
            public readonly int Port;
            private bool closed;
            public Echo(string label)
            {
                marker = label; listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                Port = ((IPEndPoint)listener.LocalEndpoint).Port; Accept();
            }
            private async void Accept()
            {
                while (!closed)
                {
                    try
                    {
                        var client = await listener.AcceptTcpClientAsync(); lock (clients) clients.Add(client);
                        Task.Run(async delegate
                        {
                            try
                            {
                                using (client) using (var stream = client.GetStream())
                                {
                                    var label = System.Text.Encoding.ASCII.GetBytes(marker + "\n"); await stream.WriteAsync(label, 0, label.Length);
                                    var buffer = new byte[32768]; int read;
                                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0) await stream.WriteAsync(buffer, 0, read);
                                }
                            }
                            catch (IOException) { } catch (ObjectDisposedException) { }
                        });
                    }
                    catch { if (!closed) throw; }
                }
            }
            public void Dispose() { closed = true; listener.Stop(); lock (clients) foreach (var client in clients) client.Close(); }
        }
    }
}
