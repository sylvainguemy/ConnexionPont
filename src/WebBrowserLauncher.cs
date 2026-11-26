using System;
using System.Diagnostics;
using System.IO;

namespace RdpTunnel
{
    public static class WebBrowserLauncher
    {
        public static string ProfileDirectory(Settings settings, string root)
        {
            Guid pair;
            if (!Guid.TryParse(settings.PairId, out pair)) throw new InvalidDataException("Identifiant d'appairage invalide.");
            return Path.Combine(root, "ConnexionPont", "Navigateur", pair.ToString("N") + "-" + settings.LocalWebProxyPort);
        }

        public static string BuildArguments(Settings settings, int targetPort, string root)
        {
            Settings.CheckHost(settings.WebHost, "Serveur web interne");
            Settings.CheckPort(settings.LocalWebProxyPort);
            if (targetPort != 8080 && targetPort != 8090) throw new ArgumentException("Le navigateur accepte les ports 8080 ou 8090.");
            var address = new UriBuilder("http", settings.WebHost, targetPort).Uri.AbsoluteUri;
            return "--proxy-server=socks5://127.0.0.1:" + settings.LocalWebProxyPort
                + " --proxy-bypass-list=\"<-loopback>\" --user-data-dir=\"" + ProfileDirectory(settings, root)
                + "\" --no-first-run --disable-background-networking --new-window \"" + address + "\"";
        }

        public static void Open(Settings settings, int port)
        {
            string executable = FindBrowser();
            if (executable == null) throw new FileNotFoundException("Microsoft Edge ou Google Chrome doit être présent sur votre PC d'accès.");
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Directory.CreateDirectory(ProfileDirectory(settings, root));
            Process.Start(new ProcessStartInfo(executable, BuildArguments(settings, port, root)) { UseShellExecute = true });
        }

        internal static string FindBrowser()
        {
            // A dedicated user-data directory makes the proxy flags effective even
            // when the person's normal browser is already running.
            string[] bases = { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
            foreach (string relative in new[] { "Microsoft\\Edge\\Application\\msedge.exe", "Google\\Chrome\\Application\\chrome.exe" })
                foreach (string folder in bases)
                    if (!string.IsNullOrEmpty(folder))
                    { string candidate = Path.Combine(folder, relative); if (File.Exists(candidate)) return candidate; }
            return null;
        }
    }
}
