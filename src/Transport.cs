using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RdpTunnel
{
    public sealed class Wire : IDisposable
    {
        public readonly TcpClient Socket;
        public readonly SslStream Stream;
        private readonly object writeLock = new object();
        public Wire(TcpClient socket, SslStream stream) { Socket = socket; Stream = stream; }

        public void Send(string line)
        {
            if (line.Length > 200 || line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0) throw new InvalidDataException("Commande invalide.");
            var bytes = Encoding.ASCII.GetBytes(line + "\n");
            lock (writeLock) { Stream.Write(bytes, 0, bytes.Length); Stream.Flush(); }
        }

        public string Read()
        {
            // No buffered reader: it could consume the first bytes of SSH/RDP.
            var bytes = new byte[201]; var count = 0;
            while (count < bytes.Length)
            {
                var value = Stream.ReadByte();
                if (value < 0) throw new EndOfStreamException("Connexion fermée.");
                if (value == 10) return Encoding.ASCII.GetString(bytes, 0, count);
                if (value < 32 || value > 126) throw new InvalidDataException("Commande non ASCII.");
                bytes[count++] = (byte)value;
            }
            throw new InvalidDataException("Commande trop longue.");
        }

        public void Dispose() { try { Socket.Close(); } catch { } try { Stream.Dispose(); } catch { } }
    }

    public abstract class Engine : IDisposable
    {
        protected readonly Settings Config;
        protected readonly Action<string> Log;
        protected readonly CancellationTokenSource StopToken = new CancellationTokenSource();
        protected readonly ConcurrentDictionary<TcpClient, byte> Sockets = new ConcurrentDictionary<TcpClient, byte>();
        private int stopped;
        protected Engine(Settings settings, Action<string> log) { Config = settings; Log = log ?? delegate { }; }
        public bool IsStopped { get { return StopToken.IsCancellationRequested; } }
        protected void Track(TcpClient socket)
        {
            socket.NoDelay = true; Sockets.TryAdd(socket, 0);
            if (IsStopped) { Untrack(socket); throw new OperationCanceledException(); }
        }
        protected void Untrack(TcpClient socket)
        { byte value; Sockets.TryRemove(socket, out value); try { socket.Close(); } catch { } }
        protected async Task<TcpClient> Connect(string host, int port)
        {
            var socket = new TcpClient(); Track(socket);
            try
            {
                var connect = socket.ConnectAsync(host, port);
                if (await Task.WhenAny(connect, Task.Delay(10000, StopToken.Token)).ConfigureAwait(false) != connect)
                    throw new TimeoutException("Connexion impossible ou expirée (10 s).");
                await connect.ConfigureAwait(false); return socket;
            }
            catch { Untrack(socket); throw; }
        }
        protected Wire Authenticate(TcpClient socket, X509Certificate2 identity, string expected, bool server)
        {
            var ssl = new SslStream(socket.GetStream(), false,
                delegate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors) { return Security.Matches(cert, expected); },
                delegate(object sender, string host, X509CertificateCollection local, X509Certificate remote, string[] issuers) { return identity; });
            ssl.ReadTimeout = 10000; ssl.WriteTimeout = 10000;
            try
            {
                // TLS 1.2 is supported by Windows 10/11 and .NET Framework 4.8.
                // Mutual authentication uses exact SHA-256 certificate pinning.
                if (server) ssl.AuthenticateAsServer(identity, true, SslProtocols.Tls12, false);
                else ssl.AuthenticateAsClient("RdpTunnel", new X509CertificateCollection { identity }, SslProtocols.Tls12, false);
                if (!ssl.IsMutuallyAuthenticated || !Security.Matches(ssl.RemoteCertificate, expected))
                    throw new AuthenticationException("Certificat distant non autorisé.");
                return new Wire(socket, ssl);
            }
            catch { ssl.Dispose(); throw; }
        }
        protected static async Task Bridge(Stream first, Stream second, Action close)
        {
            var forward = first.CopyToAsync(second, 65536);
            var reverse = second.CopyToAsync(first, 65536);
            try { await Task.WhenAny(forward, reverse).ConfigureAwait(false); }
            finally { close(); }
            try { await Task.WhenAll(forward, reverse).ConfigureAwait(false); } catch (IOException) { } catch (ObjectDisposedException) { } catch (SocketException) { }
        }
        public virtual void Stop()
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            StopToken.Cancel();
            foreach (var socket in Sockets.Keys) Untrack(socket);
        }
        public void Dispose() { Stop(); }
        protected void Report(string context, Exception error)
        { if (!IsStopped) Log(context + " : " + error.Message); }
    }
}
