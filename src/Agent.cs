using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace RdpTunnel
{
    public sealed class Agent : Engine
    {
        private readonly X509Certificate2 identity;
        private readonly int rdpTargetPort;
        private int active;
        private volatile bool connected;
        public bool Connected { get { return connected; } }
        public int ActiveSessions { get { return active; } }
        public Agent(Settings settings, Action<string> log) : this(settings, log, 3389) { }
        internal Agent(Settings settings, Action<string> log, int testRdpPort) : base(settings, log)
        { settings.Validate(false); identity = Security.LoadCertificate(settings.AgentPfx); rdpTargetPort = testRdpPort; }

        public Task Start() { return Task.Run((Func<Task>)Run); }

        private async Task Run()
        {
            var delay = 1000;
            while (!IsStopped)
            {
                TcpClient socket = null; Wire wire = null; CancellationTokenSource heartbeat = null; Task heartbeatTask = null;
                try
                {
                    Log("Connexion au relais " + Config.PublicHost + ":" + Config.PublicPort + "...");
                    socket = await Connect(Config.PublicHost, Config.PublicPort).ConfigureAwait(false);
                    wire = Authenticate(socket, identity, Config.RelayHash, false);
                    wire.Send("RDT1 CONTROL");
                    if (wire.Read() != "READY") throw new InvalidDataException("Réponse du relais invalide.");
                    wire.Stream.ReadTimeout = 45000; connected = true; delay = 1000;
                    Log("Tunnel connecté et relais authentifié.");
                    heartbeat = CancellationTokenSource.CreateLinkedTokenSource(StopToken.Token);
                    var current = wire; var token = heartbeat.Token;
                    heartbeatTask = Task.Run(async delegate
                    {
                        try
                        {
                            while (!token.IsCancellationRequested)
                            { current.Send("PING"); await Task.Delay(10000, token).ConfigureAwait(false); }
                        }
                        catch (OperationCanceledException) { }
                        catch { current.Dispose(); }
                    });
                    while (!IsStopped)
                    {
                        var line = wire.Read(); if (line == "PONG") continue;
                        var parts = line.Split(' '); Guid session;
                        if (parts.Length != 3 || parts[0] != "OPEN" || !Guid.TryParseExact(parts[1], "N", out session) || (parts[2] != "RDP" && parts[2] != "SSH"))
                            throw new InvalidDataException("Commande de session invalide.");
                        if (Interlocked.Increment(ref active) > 8) { Interlocked.Decrement(ref active); continue; }
                        var id = parts[1]; var service = parts[2];
                        Task.Run(() => OpenSession(id, service));
                    }
                }
                catch (Exception error) { Report("Tunnel", error); }
                finally
                {
                    connected = false; if (heartbeat != null) heartbeat.Cancel();
                    if (wire != null) wire.Dispose(); if (socket != null) Untrack(socket);
                    // A lost control channel terminates the corresponding data channels.
                    foreach (var open in Sockets.Keys) Untrack(open);
                }
                if (heartbeatTask != null) await heartbeatTask.ConfigureAwait(false);
                if (heartbeat != null) heartbeat.Dispose();
                if (!IsStopped)
                {
                    Log("Nouvelle tentative dans " + (delay / 1000) + " s.");
                    try { await Task.Delay(delay, StopToken.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
                    delay = Math.Min(delay * 2, 30000);
                }
            }
        }

        private async Task OpenSession(string id, string service)
        {
            TcpClient target = null, remote = null; Wire wire = null;
            try
            {
                var host = service == "RDP" ? "127.0.0.1" : Config.SshHost;
                var port = service == "RDP" ? rdpTargetPort : Config.SshPort;
                if (string.IsNullOrWhiteSpace(host)) throw new IOException("Le serveur SSH n'est pas configuré.");
                target = await Connect(host, port).ConfigureAwait(false);
                remote = await Connect(Config.PublicHost, Config.PublicPort).ConfigureAwait(false);
                wire = Authenticate(remote, identity, Config.RelayHash, false);
                wire.Send("RDT1 DATA " + id);
                if (wire.Read() != "GO") throw new InvalidDataException("Session refusée.");
                wire.Stream.ReadTimeout = System.Threading.Timeout.Infinite;
                Log(service + " : destination connectée.");
                await Bridge(target.GetStream(), wire.Stream, delegate { target.Close(); wire.Dispose(); }).ConfigureAwait(false);
            }
            catch (Exception error) { Report(service + " : vérifier le service, l'adresse et le pare-feu de la destination", error); }
            finally
            {
                if (wire != null) wire.Dispose(); if (target != null) Untrack(target); if (remote != null) Untrack(remote);
                Interlocked.Decrement(ref active);
            }
        }
    }
}
