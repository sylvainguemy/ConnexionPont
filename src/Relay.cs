using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace RdpTunnel
{
    public sealed class Relay : Engine
    {
        private TcpListener incoming, rdp, ssh, web;
        private readonly X509Certificate2 identity;
        private readonly object controlLock = new object();
        private Wire control;
        private readonly ConcurrentDictionary<string, Pending> pending = new ConcurrentDictionary<string, Pending>();
        private readonly SemaphoreSlim handshakes = new SemaphoreSlim(24);
        private int active;
        public bool AgentConnected { get { lock (controlLock) return control != null; } }
        public int ActiveSessions { get { return active; } }
        private sealed class Pending
        {
            public Wire Owner;
            public TaskCompletionSource<Wire> Ready = new TaskCompletionSource<Wire>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Relay(Settings settings, Action<string> log) : base(settings, log)
        { settings.Validate(true); identity = Security.LoadCertificate(settings.RelayPfx); }

        public void Start()
        {
            try
            {
                incoming = new TcpListener(IPAddress.Any, Config.ListenPort); incoming.Start(32);
                rdp = new TcpListener(IPAddress.Loopback, Config.LocalRdpPort); rdp.Start(8);
                if (!string.IsNullOrWhiteSpace(Config.SshHost)) { ssh = new TcpListener(IPAddress.Loopback, Config.LocalSshPort); ssh.Start(8); }
                if (!string.IsNullOrWhiteSpace(Config.WebHost)) { web = new TcpListener(IPAddress.Loopback, Config.LocalWebProxyPort); web.Start(32); }
                AcceptIncoming(); AcceptLocal(rdp, "RDP"); if (ssh != null) AcceptLocal(ssh, "SSH"); if (web != null) AcceptLocal(web, "SOCKS");
                Log("Relais démarré : écoute TCP " + Config.ListenPort + ". En attente de l'agent.");
            }
            catch { Stop(); throw; }
        }

        private async void AcceptIncoming()
        {
            while (!IsStopped)
            {
                TcpClient socket = null;
                try
                {
                    socket = await incoming.AcceptTcpClientAsync().ConfigureAwait(false);
                    if (!handshakes.Wait(0)) { socket.Close(); continue; }
                    Track(socket); var accepted = socket;
                    Task.Run(() => HandleIncoming(accepted));
                }
                catch (Exception error) { if (socket != null) socket.Close(); Report("Écoute relais", error); if (!IsStopped) Stop(); }
            }
        }

        private async Task HandleIncoming(TcpClient socket)
        {
            Wire wire = null; bool slot = true;
            try
            {
                wire = Authenticate(socket, identity, Config.AgentHash, true);
                var header = wire.Read();
                handshakes.Release(); slot = false;
                if (header == Wire.Protocol + " CONTROL")
                {
                    lock (controlLock)
                    {
                        if (control != null) throw new IOException("Un agent est déjà connecté.");
                        control = wire;
                    }
                    wire.Stream.ReadTimeout = 45000;
                    wire.Send("READY"); Log("Pont authentifié. Accès RDP, SSH et web disponibles selon le profil.");
                    try
                    {
                        while (!IsStopped)
                        {
                            if (wire.Read() != "PING") throw new InvalidDataException("Commande de contrôle inconnue.");
                            wire.Send("PONG");
                        }
                    }
                    finally
                    {
                        lock (controlLock) { if (control == wire) control = null; }
                        foreach (var entry in pending)
                            if (entry.Value.Owner == wire)
                            {
                                entry.Value.Ready.TrySetCanceled();
                                if (entry.Value.Ready.Task.Status == TaskStatus.RanToCompletion) entry.Value.Ready.Task.Result.Dispose();
                            }
                        Log("Agent déconnecté. Attente de sa reconnexion.");
                    }
                }
                else if (header.StartsWith(Wire.Protocol + " DATA ", StringComparison.Ordinal))
                {
                    var id = header.Substring(10); Guid parsed; Pending request;
                    if (!Guid.TryParseExact(id, "N", out parsed) || !pending.TryGetValue(id, out request))
                        throw new InvalidDataException("Session inconnue ou expirée.");
                    lock (controlLock)
                        if (control == null || control != request.Owner) throw new IOException("Agent déconnecté.");
                    if (!request.Ready.TrySetResult(wire)) throw new IOException("Session déjà associée.");
                    await request.Finished.Task.ConfigureAwait(false);
                }
                else throw new InvalidDataException("Protocole incompatible : mettre à jour les deux EXE vers la V0.2.");
            }
            catch (Exception error) { Report("Connexion relais", error); }
            finally { if (slot) handshakes.Release(); if (wire != null) wire.Dispose(); Untrack(socket); }
        }

        private async void AcceptLocal(TcpListener listener, string service)
        {
            while (!IsStopped)
            {
                try
                {
                    var socket = await listener.AcceptTcpClientAsync().ConfigureAwait(false); Track(socket);
                    if (Interlocked.Increment(ref active) > Wire.MaxSessions) { Interlocked.Decrement(ref active); Untrack(socket); continue; }
                    Task.Run(() => HandleLocal(socket, service));
                }
                catch (Exception error) { Report("Écoute locale", error); if (!IsStopped) Stop(); }
            }
        }

        private async Task HandleLocal(TcpClient socket, string service)
        {
            var id = Guid.NewGuid().ToString("N"); var request = new Pending(); Wire data = null;
            bool proxy = service == "SOCKS", proxyReady = false;
            try
            {
                if (proxy)
                {
                    var stream = socket.GetStream(); stream.ReadTimeout = 5000; stream.WriteTimeout = 5000;
                    service = Socks5.Negotiate(stream, Config.WebHost);
                    stream.ReadTimeout = Timeout.Infinite; stream.WriteTimeout = 10000;
                }
                lock (controlLock)
                {
                    if (control == null) throw new IOException("L'agent n'est pas connecté.");
                    request.Owner = control; pending[id] = request;
                    control.Send("OPEN " + id + " " + service);
                }
                var wait = request.Ready.Task;
                if (await Task.WhenAny(wait, Task.Delay(20000, StopToken.Token)).ConfigureAwait(false) != wait)
                    throw new TimeoutException("Le PC client n'a pas ouvert la destination " + service + ".");
                data = await wait.ConfigureAwait(false);
                data.Stream.ReadTimeout = System.Threading.Timeout.Infinite;
                data.Send("GO");
                if (proxy) { Socks5.Reply(socket.GetStream(), 0); proxyReady = true; }
                Log(service + " : session ouverte.");
                await Bridge(socket.GetStream(), data.Stream, delegate { socket.Close(); data.Dispose(); }).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                if (proxy && !proxyReady && !(error is ProxyRejectedException))
                    try { Socks5.Reply(socket.GetStream(), 4); } catch { }
                Report(service, error);
            }
            finally
            {
                Pending removed; pending.TryRemove(id, out removed);
                request.Ready.TrySetCanceled();
                if (data == null && request.Ready.Task.Status == TaskStatus.RanToCompletion) data = request.Ready.Task.Result;
                if (data != null) data.Dispose();
                request.Finished.TrySetResult(true); Untrack(socket); Interlocked.Decrement(ref active);
            }
        }

        public override void Stop()
        {
            base.Stop();
            if (incoming != null) incoming.Stop(); if (rdp != null) rdp.Stop(); if (ssh != null) ssh.Stop(); if (web != null) web.Stop();
            lock (controlLock) { if (control != null) control.Dispose(); control = null; }
            foreach (var request in pending.Values) { request.Ready.TrySetCanceled(); request.Finished.TrySetResult(true); }
        }
    }
}
