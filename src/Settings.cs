using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace RdpTunnel
{
    [DataContract]
    public sealed class Settings
    {
        [DataMember] public int Version = 1;
        [DataMember] public string PublicHost = "";
        [DataMember] public int PublicPort = 443;
        [DataMember] public int ListenPort = 8443;
        [DataMember] public int LocalRdpPort = 13389;
        [DataMember] public int LocalSshPort = 10022;
        [DataMember] public string SshHost = "";
        [DataMember] public int SshPort = 22;
        [DataMember] public string WebHost = "";
        [DataMember] public int LocalWebProxyPort = 1080;
        [DataMember] public byte[] RelayPfx;
        [DataMember] public byte[] AgentPfx;
        [DataMember] public string RelayHash;
        [DataMember] public string AgentHash;
        [DataMember] public string PairId;

        [OnDeserializing]
        private void SetNewDefaults(StreamingContext context)
        {
            // DataContractJsonSerializer does not execute field initializers.
            // Existing V0.1 settings remain usable without regenerating keys.
            WebHost = ""; LocalWebProxyPort = 1080;
        }

        public void Validate(bool relay)
        {
            if (Version != 1) throw new InvalidDataException("Version de configuration inconnue.");
            CheckHost(PublicHost, "IP publique ou nom DNS");
            CheckPort(PublicPort); CheckPort(ListenPort);
            CheckPort(LocalRdpPort); CheckPort(LocalSshPort); CheckPort(SshPort); CheckPort(LocalWebProxyPort);
            var ports = new HashSet<int>();
            if (!ports.Add(ListenPort) || !ports.Add(LocalRdpPort) || !ports.Add(LocalSshPort)
                || (!string.IsNullOrWhiteSpace(WebHost) && !ports.Add(LocalWebProxyPort)))
                throw new InvalidDataException("Les ports d'écoute, RDP, SSH et proxy web locaux doivent être différents.");
            if (!string.IsNullOrWhiteSpace(SshHost)) CheckHost(SshHost, "Serveur SSH");
            if (!string.IsNullOrWhiteSpace(WebHost)) CheckHost(WebHost, "Serveur web interne");
            if (AgentPfx == null || AgentPfx.Length == 0 || (relay && (RelayPfx == null || RelayPfx.Length == 0)))
                throw new InvalidDataException("Certificat privé manquant.");
            if (RelayHash == null || RelayHash.Length != 64 || AgentHash == null || AgentHash.Length != 64)
                throw new InvalidDataException("Empreinte de certificat invalide.");
            Guid pair;
            if (!Guid.TryParse(PairId, out pair)) throw new InvalidDataException("Identifiant d'appairage invalide.");
            using (var agent = Security.LoadCertificate(AgentPfx))
                if (!Security.Matches(agent, AgentHash)) throw new InvalidDataException("Certificat agent incohérent ou expiré.");
            if (relay) using (var cert = Security.LoadCertificate(RelayPfx))
                if (!Security.Matches(cert, RelayHash)) throw new InvalidDataException("Certificat relais incohérent ou expiré.");
        }

        public static void CheckPort(int port)
        {
            if (port < 1 || port > 65535) throw new InvalidDataException("Le port doit être compris entre 1 et 65535.");
        }

        public static void CheckHost(string host, string label)
        {
            if (string.IsNullOrWhiteSpace(host) || host != host.Trim() || host.Length > 253 ||
                Uri.CheckHostName(host) == UriHostNameType.Unknown)
                throw new InvalidDataException(label + " : saisir une IP ou un nom DNS, sans https:// ni port.");
        }

        public Settings AgentCopy()
        {
            var copy = FromBytes(ToBytes());
            copy.RelayPfx = null; // Never distribute the relay private key.
            return copy;
        }

        public byte[] ToBytes()
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, this);
                return stream.ToArray();
            }
        }

        public static Settings FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 131072) throw new InvalidDataException("Configuration trop volumineuse.");
            using (var stream = new MemoryStream(bytes))
                return (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
        }

        public void Save(string path)
        {
            var bytes = ProtectedData.Protect(ToBytes(), Encoding.UTF8.GetBytes("RdpTunnel/v1"), DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temp = path + ".new";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }

        public static Settings Load(string path)
        {
            return FromBytes(ProtectedData.Unprotect(File.ReadAllBytes(path), Encoding.UTF8.GetBytes("RdpTunnel/v1"), DataProtectionScope.CurrentUser));
        }
    }

    public static class Security
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("RDPTUN01");
        public static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }

        public static string Hash(X509Certificate cert)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(cert.GetRawCertData())).Replace("-", "");
        }

        public static bool Matches(X509Certificate certificate, string expected)
        {
            if (certificate == null || string.IsNullOrEmpty(expected)) return false;
            using (var cert = new X509Certificate2(certificate))
                return DateTime.UtcNow >= cert.NotBefore.ToUniversalTime() && DateTime.UtcNow <= cert.NotAfter.ToUniversalTime()
                    && string.Equals(Hash(cert), expected, StringComparison.Ordinal);
        }

        public static X509Certificate2 LoadCertificate(byte[] pfx)
        {
            // No certificate is installed into the Windows trusted-root store.
            return new X509Certificate2(pfx, "", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
        }

        public static byte[] CreateCertificate(string name, bool server)
        {
            using (var rsa = RSA.Create())
            {
                rsa.KeySize = 3072;
                var request = new CertificateRequest("CN=" + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                var usages = new OidCollection();
                usages.Add(new Oid(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2"));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
                using (var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddYears(2)))
                    return cert.Export(X509ContentType.Pfx, "");
            }
        }

        public static Settings CreatePair(string host, int publicPort, int listenPort, int rdpPort, int sshPort, string sshHost)
        {
            var settings = new Settings { PublicHost = host, PublicPort = publicPort, ListenPort = listenPort,
                LocalRdpPort = rdpPort, LocalSshPort = sshPort, SshHost = sshHost, PairId = Guid.NewGuid().ToString("N") };
            settings.RelayPfx = CreateCertificate("RdpTunnel-Relay-" + settings.PairId, true);
            settings.AgentPfx = CreateCertificate("RdpTunnel-Agent-" + settings.PairId, false);
            using (var cert = LoadCertificate(settings.RelayPfx)) settings.RelayHash = Hash(cert);
            using (var cert = LoadCertificate(settings.AgentPfx)) settings.AgentHash = Hash(cert);
            settings.Validate(true);
            return settings;
        }

        // Portable enrollment: PBKDF2-SHA256 + AES-256-CBC, encrypt-then-HMAC.
        // The MAC is always verified before decryption and before JSON parsing.
        public static byte[] Export(Settings settings, string password)
        {
            if (password == null || password.Length < 12) throw new ArgumentException("Utiliser un mot de passe d'au moins 12 caractères.");
            var salt = RandomBytes(32);
            byte[] keys;
            using (var derive = new Rfc2898DeriveBytes(password, salt, 200000, HashAlgorithmName.SHA256)) keys = derive.GetBytes(64);
            using (var aes = Aes.Create())
            {
                aes.Key = Slice(keys, 0, 32); aes.GenerateIV();
                byte[] cipher;
                var plain = settings.AgentCopy().ToBytes();
                using (var enc = aes.CreateEncryptor()) cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
                using (var stream = new MemoryStream())
                {
                    stream.Write(Magic, 0, Magic.Length); stream.Write(salt, 0, salt.Length);
                    stream.Write(aes.IV, 0, 16); stream.Write(cipher, 0, cipher.Length);
                    var body = stream.ToArray();
                    using (var mac = new HMACSHA256(Slice(keys, 32, 32)))
                    {
                        var tag = mac.ComputeHash(body); stream.Write(tag, 0, tag.Length);
                    }
                    Array.Clear(keys, 0, keys.Length); Array.Clear(plain, 0, plain.Length);
                    return stream.ToArray();
                }
            }
        }

        public static Settings Import(byte[] file, string password)
        {
            if (file == null || file.Length < 104 || file.Length > 131072 || !Equal(Slice(file, 0, 8), Magic))
                throw new InvalidDataException("Ce fichier n'est pas un profil RdpTunnel valide.");
            byte[] keys;
            using (var derive = new Rfc2898DeriveBytes(password ?? "", Slice(file, 8, 32), 200000, HashAlgorithmName.SHA256)) keys = derive.GetBytes(64);
            using (var mac = new HMACSHA256(Slice(keys, 32, 32)))
                if (!Equal(mac.ComputeHash(file, 0, file.Length - 32), Slice(file, file.Length - 32, 32)))
                    throw new CryptographicException("Mot de passe incorrect ou profil modifié.");
            using (var aes = Aes.Create())
            {
                aes.Key = Slice(keys, 0, 32); aes.IV = Slice(file, 40, 16);
                using (var dec = aes.CreateDecryptor())
                {
                    var result = Settings.FromBytes(dec.TransformFinalBlock(file, 56, file.Length - 88));
                    if (result.RelayPfx != null) throw new InvalidDataException("Le profil agent ne doit pas contenir de clé privée relais.");
                    result.Validate(false);
                    Array.Clear(keys, 0, keys.Length);
                    return result;
                }
            }
        }

        private static byte[] Slice(byte[] data, int offset, int count)
        { var result = new byte[count]; Buffer.BlockCopy(data, offset, result, 0, count); return result; }
        private static bool Equal(byte[] left, byte[] right)
        { if (left.Length != right.Length) return false; var diff = 0; for (var i = 0; i < left.Length; i++) diff |= left[i] ^ right[i]; return diff == 0; }
    }
}
