using System;
using System.IO;
using System.Net;
using System.Text;

namespace RdpTunnel
{
    internal sealed class ProxyRejectedException : IOException
    {
        public ProxyRejectedException(string message) : base(message) { }
    }

    public static class Socks5
    {
        // This is a narrowly scoped browser proxy, not a general network proxy.
        // No requested hostname/port ever leaves this parser unless allowlisted.
        public static string Negotiate(Stream stream, string permittedHost)
        {
            if (Read(stream) != 5) throw new InvalidDataException("Version SOCKS invalide.");
            int count = Read(stream); bool anonymous = false;
            for (int i = 0; i < count; i++) if (Read(stream) == 0) anonymous = true;
            stream.WriteByte(5); stream.WriteByte(anonymous ? (byte)0 : (byte)255); stream.Flush();
            if (!anonymous) throw new ProxyRejectedException("Méthode SOCKS non prise en charge.");
            int version = Read(stream), command = Read(stream), reserved = Read(stream), addressType = Read(stream);
            if (version != 5 || reserved != 0) Reject(stream, 1, "Requête SOCKS invalide.");
            if (command != 1) Reject(stream, 7, "Seules les connexions TCP CONNECT sont autorisées.");
            string requestedHost;
            if (addressType == 1) requestedHost = new IPAddress(ReadBytes(stream, 4)).ToString();
            else if (addressType == 3)
            {
                int length = Read(stream); if (length == 0) Reject(stream, 8, "Nom SOCKS vide.");
                var hostBytes = ReadBytes(stream, length);
                foreach (byte b in hostBytes) if (b < 33 || b > 126) Reject(stream, 8, "Nom SOCKS invalide.");
                requestedHost = Encoding.ASCII.GetString(hostBytes);
            }
            else { Reject(stream, 8, "Type d'adresse SOCKS non pris en charge."); return null; }
            int port = (Read(stream) << 8) | Read(stream);
            if (!MatchesHost(requestedHost, permittedHost) || (port != 8080 && port != 8090))
                Reject(stream, 2, "Le proxy web autorise uniquement le serveur du profil sur les ports 8080 et 8090.");
            return port == 8080 ? "WEB8080" : "WEB8090";
        }

        public static void Reply(Stream stream, byte status)
        {
            var reply = new byte[] { 5, status, 0, 1, 0, 0, 0, 0, 0, 0 };
            stream.Write(reply, 0, reply.Length); stream.Flush();
        }
        private static void Reject(Stream stream, byte code, string message)
        { Reply(stream, code); throw new ProxyRejectedException(message); }
        private static int Read(Stream stream)
        { int value = stream.ReadByte(); if (value < 0) throw new EndOfStreamException("Requête SOCKS interrompue."); return value; }
        private static byte[] ReadBytes(Stream stream, int count)
        { var result = new byte[count]; for (int i = 0; i < count; i++) result[i] = (byte)Read(stream); return result; }
        private static bool MatchesHost(string requested, string configured)
        {
            if (string.IsNullOrWhiteSpace(configured)) return false;
            IPAddress left, right;
            if (IPAddress.TryParse(requested, out left) && IPAddress.TryParse(configured, out right)) return left.Equals(right);
            return string.Equals(requested.TrimEnd('.'), configured.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
