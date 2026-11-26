using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using RdpTunnel;
internal static class UiSmoke
{
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        foreach (bool relay in new[] { true, false })
        {
            using (var form = new TunnelForm(relay))
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.Show(); Application.DoEvents(); form.PerformLayout();
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relay ? "relay-ui.png" : "agent-ui.png"));
                if (form.Controls.Count == 0) return 1;
                form.Close();
            }
        }
        Console.WriteLine("PASS: both WinForms interfaces construct and render without starting network access");
        return 0;
    }
}
