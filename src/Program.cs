using System;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;

[assembly: AssemblyTitle("ConnexionPont")]
[assembly: AssemblyDescription("Tunnel inverse Windows pour RDP et SSH")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace RdpTunnel
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            { MessageBox.Show(e.Exception.Message, "ConnexionPont", MessageBoxButtons.OK, MessageBoxIcon.Error); };
#if RELAY
            Application.Run(new TunnelForm(true));
#else
            Application.Run(new TunnelForm(false));
#endif
        }
    }
}
