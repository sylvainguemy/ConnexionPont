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
            ProxyUnitChecks(config, folder);
            if (cryptoOnly) { Console.WriteLine("Network checks explicitly skipped in this restricted environment."); return; }

            using (var rdp = new Echo("RDP"))
            using (var ssh = new Echo("SSH"))
            using (var web8080 = new HttpTarget("WEB8080"))
            using (var web8090 = new HttpTarget("WEB8090"))
            {
                config.SshPort = ssh.Port;
                config.WebHost = "127.0.0.1"; config.LocalWebProxyPort = FreePort();
                var agentSettings = config.AgentCopy();
                using (var relay = new Relay(config, Log))
                using (var agent = new Agent(agentSettings, Log, rdp.Port, web8080.Port, web8090.Port))
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
                    Check((await HttpThroughProxy(config.LocalWebProxyPort, 8080, false)).Contains("WEB8080|Host: 127.0.0.1:8080"), "HTTP 8080 uses bridge and preserves original Host header");
                    Check((await HttpThroughProxy(config.LocalWebProxyPort, 8090, true)).Contains("WEB8090|Host: 127.0.0.1:8090"), "HTTP 8090 uses SOCKS domain addressing and correct bridge target");
                    await Task.WhenAll(Enumerable.Range(0, 12).Select(i => HttpThroughProxy(config.LocalWebProxyPort, i % 2 == 0 ? 8080 : 8090, false)));
                    Check(true, "Twelve concurrent browser requests complete through the bridge");
                    await RejectNetworkDestination(config.LocalWebProxyPort, "127.0.0.2", 8080);
                    await RejectNetworkDestination(config.LocalWebProxyPort, "127.0.0.1", 22);
                    await Until(() => relay.ActiveSessions == 0 && agent.ActiveSessions == 0, "HTTP and rejected proxy sessions release slots");
                    if (WebBrowserLauncher.FindBrowser() != null)
                    {
                        await BrowserThroughProxy(config, 8080, "WEB8080", folder);
                        await BrowserThroughProxy(config, 8090, "WEB8090", folder);
                    }
                    else Console.WriteLine("Browser rendering checks skipped: Edge/Chrome not installed.");

                    // Malformed authenticated data session cannot attach to an arbitrary stream.
                    using (var cert = Security.LoadCertificate(agentSettings.AgentPfx))
                    using (var socket = new TcpClient())
                    {
                        await socket.ConnectAsync("127.0.0.1", config.ListenPort);
                        using (var ssl = new SslStream(socket.GetStream(), false, (s,c,ch,e) => Security.Matches(c, config.RelayHash), (s,h,l,r,i) => cert))
                        {
                            ssl.ReadTimeout = 5000; ssl.WriteTimeout = 5000;
                            ssl.AuthenticateAsClient("RdpTunnel", new X509CertificateCollection { cert }, SslProtocols.Tls12, false);
                            var wire = new Wire(socket, ssl); wire.Send(Wire.Protocol + " DATA " + Guid.NewGuid().ToString("N"));
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
                        Check((await HttpThroughProxy(config.LocalWebProxyPort, 8080, false)).Contains("WEB8080"), "HTTP works after relay restart and tunnel reconnection");
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

        private static void ProxyUnitChecks(Settings config, string folder)
        {
            var oldJson = System.Text.Encoding.UTF8.GetString(config.ToBytes());
            oldJson = System.Text.RegularExpressions.Regex.Replace(oldJson, "\\\"LocalWebProxyPort\\\":1080,?", "");
            oldJson = System.Text.RegularExpressions.Regex.Replace(oldJson, "\\\"WebHost\\\":\\\"\\\",?", "");
            var migrated = Settings.FromBytes(System.Text.Encoding.UTF8.GetBytes(oldJson));
            Check(migrated.LocalWebProxyPort == 1080 && migrated.WebHost == "", "V0.1 settings migrate with web disabled and existing certificates intact");
            var ipv4Request = new byte[] { 5, 1, 0, 5, 1, 0, 1, 127, 0, 0, 1, 31, 144, 71 };
            using (var stream = new ScriptedStream(ipv4Request))
            {
                Check(Socks5.Negotiate(stream, "127.0.0.1") == "WEB8080", "SOCKS IPv4 routes only the configured web target");
                Check(stream.ReadByte() == 71, "SOCKS parser leaves subsequent application bytes untouched");
            }
            using (var stream = new ScriptedStream(DomainRequest("Example.Internal", 8090)))
                Check(Socks5.Negotiate(stream, "example.internal") == "WEB8090", "SOCKS domain matching is case-insensitive");
            using (var stream = new ScriptedStream(DomainRequest("other.internal", 8080)))
                Reject(() => Socks5.Negotiate(stream, "example.internal"), "SOCKS rejects hosts outside the web allowlist");
            using (var stream = new ScriptedStream(DomainRequest("example.internal", 22)))
                Reject(() => Socks5.Negotiate(stream, "example.internal"), "SOCKS refuses arbitrary ports including SSH");
            var bind = DomainRequest("example.internal", 8080); bind[4] = 2;
            using (var stream = new ScriptedStream(bind))
                Reject(() => Socks5.Negotiate(stream, "example.internal"), "SOCKS BIND is rejected");
            using (var stream = new ScriptedStream(new byte[] { 5, 1, 2 }))
                Reject(() => Socks5.Negotiate(stream, "example.internal"), "Unsupported SOCKS authentication is rejected");
            using (var stream = new ScriptedStream(new byte[] { 5, 1, 0, 5 }))
                Reject(() => Socks5.Negotiate(stream, "example.internal"), "Truncated SOCKS request is rejected");
            var webSettings = Settings.FromBytes(config.ToBytes()); webSettings.WebHost = "127.0.0.1";
            string args = WebBrowserLauncher.BuildArguments(webSettings, 8080, folder);
            Check(args.Contains("socks5://127.0.0.1:1080") && args.Contains("http://127.0.0.1:8080/") && args.Contains("--user-data-dir="), "Browser launcher preserves original URL and isolates proxy settings in its own profile");
            Reject(() => WebBrowserLauncher.BuildArguments(webSettings, 22, folder), "Browser launcher rejects ports outside the web profile");
            webSettings.LocalWebProxyPort = webSettings.ListenPort;
            Reject(() => webSettings.Validate(true), "Web proxy port conflicts are reported before startup");
        }

        private static byte[] DomainRequest(string host, int port)
        {
            var bytes = new List<byte>(new byte[] { 5, 1, 0, 5, 1, 0, 3, (byte)host.Length });
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(host)); bytes.Add((byte)(port >> 8)); bytes.Add((byte)port);
            return bytes.ToArray();
        }

        private static async Task<TcpClient> OpenProxy(int proxyPort, string target, int targetPort, bool domain)
        {
            var socket = new TcpClient(); await socket.ConnectAsync(IPAddress.Loopback, proxyPort);
            socket.ReceiveTimeout = 15000; socket.SendTimeout = 15000;
            try
            {
                var stream = socket.GetStream(); var hello = new byte[] { 5, 1, 0 };
                await stream.WriteAsync(hello, 0, hello.Length); var accepted = new byte[2]; await ReadExactly(stream, accepted);
                if (accepted[0] != 5 || accepted[1] != 0) throw new Exception("SOCKS greeting failed");
                byte[] request;
                if (domain) request = DomainRequest(target, targetPort).Skip(3).ToArray();
                else
                {
                    var list = new List<byte>(new byte[] { 5, 1, 0, 1 }); list.AddRange(IPAddress.Parse(target).GetAddressBytes());
                    list.Add((byte)(targetPort >> 8)); list.Add((byte)targetPort); request = list.ToArray();
                }
                await stream.WriteAsync(request, 0, request.Length);
                var reply = new byte[10]; await ReadExactly(stream, reply);
                if (reply[1] != 0) throw new ProxyRejectedException("SOCKS rejection: " + reply[1]);
                return socket;
            }
            catch { socket.Close(); throw; }
        }

        private static async Task<string> HttpThroughProxy(int proxyPort, int targetPort, bool domain)
        {
            using (var socket = await OpenProxy(proxyPort, "127.0.0.1", targetPort, domain))
            {
                var request = System.Text.Encoding.ASCII.GetBytes("GET /ui?test=1 HTTP/1.1\r\nHost: 127.0.0.1:" + targetPort + "\r\nCookie: test=kept\r\nConnection: close\r\n\r\n");
                var stream = socket.GetStream(); await stream.WriteAsync(request, 0, request.Length);
                using (var response = new MemoryStream())
                {
                    var read = stream.CopyToAsync(response);
                    if (await Task.WhenAny(read, Task.Delay(20000)) != read) throw new TimeoutException("HTTP proxy transfer stalled");
                    await read; string text = System.Text.Encoding.ASCII.GetString(response.ToArray());
                    if (!text.StartsWith("HTTP/1.1 200 OK") || !text.Contains("Cookie: test=kept")) throw new Exception("HTTP content or cookies were not preserved");
                    return text;
                }
            }
        }

        private static async Task RejectNetworkDestination(int proxyPort, string target, int port)
        {
            bool rejected = false;
            try { using (var socket = await OpenProxy(proxyPort, target, port, true)) { } }
            catch (ProxyRejectedException) { rejected = true; }
            Check(rejected, "Live proxy rejects " + target + ":" + port);
        }

        private static async Task BrowserThroughProxy(Settings config, int port, string marker, string folder)
        {
            var root = Path.Combine(folder, "browser-" + port); Directory.CreateDirectory(root);
            string args = WebBrowserLauncher.BuildArguments(config, port, root) + " --headless=new --disable-gpu --dump-dom";
            var info = new System.Diagnostics.ProcessStartInfo(WebBrowserLauncher.FindBrowser(), args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = System.Diagnostics.Process.Start(info))
            {
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                var exit = Task.Run(() => process.WaitForExit(45000));
                if (!await exit) { try { process.Kill(); } catch { } throw new TimeoutException("Headless browser test exceeded 45 s"); }
                string dom = await output; string diagnostic = await error;
                if (!dom.Contains(marker + "|Host: 127.0.0.1:" + port))
                    throw new Exception("Browser did not use tunnel for original URL on " + port + ": " + dom.Substring(0, Math.Min(1000, dom.Length)) + " " + diagnostic.Substring(0, Math.Min(1000, diagnostic.Length)));
                Check(true, "Actual Chromium browser loads original URL through proxy on " + port);
            }
        }

        private sealed class ScriptedStream : Stream
        {
            private readonly MemoryStream input; private readonly MemoryStream output = new MemoryStream();
            public ScriptedStream(byte[] bytes) { input = new MemoryStream(bytes); }
            public override bool CanRead { get { return true; } } public override bool CanWrite { get { return true; } } public override bool CanSeek { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override int Read(byte[] buffer, int offset, int count) { return input.Read(buffer, offset, count); }
            public override void Write(byte[] buffer, int offset, int count) { output.Write(buffer, offset, count); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing) { if (disposing) { input.Dispose(); output.Dispose(); } base.Dispose(disposing); }
        }

        private sealed class HttpTarget : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly List<TcpClient> clients = new List<TcpClient>(); private readonly string marker; private bool closed;
            public readonly int Port;
            public HttpTarget(string label) { marker = label; listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port; Accept(); }
            private async void Accept()
            {
                while (!closed)
                {
                    try
                    {
                        var socket = await listener.AcceptTcpClientAsync(); lock (clients) clients.Add(socket);
                        Task.Run(async delegate
                        {
                            try
                            {
                                using (socket)
                                {
                                    var stream = socket.GetStream(); var header = new List<byte>();
                                    while (header.Count < 16384)
                                    {
                                        int value = stream.ReadByte(); if (value < 0) return; header.Add((byte)value);
                                        if (header.Count >= 4 && System.Text.Encoding.ASCII.GetString(header.Skip(header.Count - 4).ToArray()) == "\r\n\r\n") break;
                                    }
                                    string request = System.Text.Encoding.ASCII.GetString(header.ToArray());
                                    string host = request.Split(new[] { "\r\n" }, StringSplitOptions.None).FirstOrDefault(x => x.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
                                    if (host != null) host = "Host: " + host.Substring(5).Trim();
                                    var body = System.Text.Encoding.ASCII.GetBytes(marker + "|" + host + "|" + request);
                                    var head = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                                    await stream.WriteAsync(head, 0, head.Length); await stream.WriteAsync(body, 0, body.Length);
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
