using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RdpTunnel
{
    public sealed class TunnelForm : Form
    {
        private readonly bool isRelay;
        private readonly string settingsPath;
        private Settings settings;
        private Relay relay;
        private Agent agent;
        private Task agentTask;
        private readonly TextBox host = new TextBox(), sshHost = new TextBox(), sshUser = new TextBox();
        private readonly NumericUpDown publicPort = Port(443), listenPort = Port(8443), rdpPort = Port(13389), sshPort = Port(10022);
        private readonly RichTextBox journal = new RichTextBox();
        private readonly Label status = new Label(), summary = new Label(), command = new Label();
        private readonly FlowLayoutPanel actions = new FlowLayoutPanel();
        private readonly TableLayoutPanel fields = new TableLayoutPanel();
        private readonly Button start = new Button(), stop = new Button(), rdp = new Button(), ssh = new Button(), export = new Button();
        private readonly Timer timer = new Timer();
        private bool busy;

        public TunnelForm(bool relayMode)
        {
            isRelay = relayMode;
            Text = "ConnexionPont — " + (isRelay ? "Relais chez vous" : "Agent chez le client");
            Size = new Size(970, 780); MinimumSize = new Size(850, 730);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(245, 247, 251);
            AutoScaleMode = AutoScaleMode.Dpi;
            settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConnexionPont", isRelay ? "Relais" : "Agent", "settings.bin");
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 1, Padding = new Padding(22) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, isRelay ? 220 : 120));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            Controls.Add(layout);
            var title = new Label { Text = "ConnexionPont  /  " + (isRelay ? "RELAIS" : "AGENT"), Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Color.FromArgb(25, 58, 105), Dock = DockStyle.Fill };
            layout.Controls.Add(title, 0, 0);
            fields.Dock = DockStyle.Fill; fields.ColumnCount = 4; fields.RowCount = 4;
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            if (isRelay)
            {
                AddField("IP publique / DNS", host, 0, 0); AddField("Port Internet", publicPort, 2, 0);
                AddField("Port d'écoute du relais", listenPort, 0, 1); AddField("RDP local chez vous", rdpPort, 2, 1);
                AddField("Serveur SSH interne", sshHost, 0, 2); AddField("SSH local chez vous", sshPort, 2, 2);
                AddField("Utilisateur SSH", sshUser, 0, 3);
                var note = new Label { Text = "SSH cible : 22. Laissez le serveur vide pour désactiver SSH.", AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.DimGray };
                fields.Controls.Add(note, 2, 3); fields.SetColumnSpan(note, 2);
                layout.Controls.Add(fields, 0, 1);
            }
            else
            {
                summary.Dock = DockStyle.Fill; summary.ForeColor = Color.FromArgb(35, 54, 80);
                layout.Controls.Add(summary, 0, 1);
            }
            actions.Dock = DockStyle.Fill; actions.WrapContents = false;
            if (isRelay) AddButton("Enregistrer", async delegate { await SaveSettings(); }, null);
            else AddButton("Importer le profil", async delegate { await ImportProfile(); }, null);
            AddButton(isRelay ? "Démarrer le relais" : "Connecter", async delegate { await StartEngine(); }, start);
            AddButton("Arrêter", delegate { StopEngine(); }, stop);
            if (isRelay) AddButton("Exporter l'agent", async delegate { await ExportProfile(); }, export);
            else AddButton("Diagnostic", async delegate { await Diagnostics(); }, null);
            layout.Controls.Add(actions, 0, 2);
            status.Dock = DockStyle.Fill; status.Font = new Font(Font, FontStyle.Bold); status.Padding = new Padding(0, 8, 0, 0);
            layout.Controls.Add(status, 0, 3);
            if (isRelay)
            {
                var links = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
                rdp.Text = "Ouvrir RDP"; StyleButton(rdp); rdp.Click += delegate { Guard(OpenRdp); }; links.Controls.Add(rdp);
                ssh.Text = "Ouvrir SSH"; StyleButton(ssh); ssh.Click += delegate { Guard(OpenSsh); }; links.Controls.Add(ssh);
                var copy = new Button { Text = "Copier la commande SSH" }; StyleButton(copy);
                copy.Click += delegate { Guard(delegate { Clipboard.SetText(SshCommand()); WriteLog("Commande SSH copiée."); }); };
                links.Controls.Add(copy); layout.Controls.Add(links, 0, 4);
            }
            else
            {
                command.Text = "Gardez cette fenêtre ouverte pendant l'accès distant. Arrêter coupe toutes les connexions.";
                command.Dock = DockStyle.Fill; command.ForeColor = Color.DimGray;
                layout.Controls.Add(command, 0, 4);
            }
            journal.ReadOnly = true; journal.Dock = DockStyle.Fill; journal.BackColor = Color.White;
            journal.Font = new Font("Consolas", 9); journal.BorderStyle = BorderStyle.FixedSingle;
            journal.DetectUrls = false; layout.Controls.Add(journal, 0, 5);
            var footer = new Label { Text = "TLS 1.2 • certificats appairés • accès locaux RDP / SSH • fermeture = arrêt du tunnel", Dock = DockStyle.Fill, ForeColor = Color.DimGray, TextAlign = ContentAlignment.BottomLeft };
            layout.Controls.Add(footer, 0, 6);
            Shown += delegate
            {
                Guard(delegate
                {
                    if (File.Exists(settingsPath))
                    {
                        settings = Settings.Load(settingsPath); settings.Validate(isRelay); DisplaySettings();
                        WriteLog("Configuration chargée et protégée pour votre compte Windows.");
                    }
                    else WriteLog(isRelay ? "Renseignez votre IP publique, les ports et le serveur SSH, puis Enregistrer." : "Importez le profil .rdptunnel créé par votre relais.");
                }); UpdateState();
            };
            FormClosing += delegate { StopEngine(); timer.Stop(); };
            timer.Interval = 750; timer.Tick += delegate { UpdateState(); }; timer.Start();
        }

        private static NumericUpDown Port(int value) { return new NumericUpDown { Minimum = 1, Maximum = 65535, Value = value, Width = 110 }; }
        private void AddField(string label, Control input, int col, int row)
        {
            fields.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, col, row);
            input.Dock = DockStyle.Fill; input.Margin = new Padding(3, 10, 18, 10); fields.Controls.Add(input, col + 1, row);
        }
        private static void StyleButton(Button button)
        { button.AutoSize = true; button.Height = 35; button.Padding = new Padding(8, 3, 8, 3); button.Margin = new Padding(0, 2, 10, 2); button.FlatStyle = FlatStyle.System; }
        private void AddButton(string text, Action action, Button existing)
        {
            var button = existing ?? new Button(); button.Text = text; StyleButton(button);
            button.Click += delegate { Guard(action); }; actions.Controls.Add(button);
        }
        private void Guard(Action action)
        { try { action(); } catch (Exception error) { ShowError(error); } }
        private void ShowError(Exception error)
        { WriteLog(error.Message); MessageBox.Show(this, error.Message, "ConnexionPont", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        private void WriteLog(string text)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action<string>)WriteLog, text); } catch (InvalidOperationException) { } return;
            }
            if (journal.TextLength > 50000) journal.Text = journal.Text.Substring(journal.TextLength - 25000);
            journal.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
            journal.SelectionStart = journal.TextLength; journal.ScrollToCaret();
        }
        private void DisplaySettings()
        {
            host.Text = settings.PublicHost; publicPort.Value = settings.PublicPort; listenPort.Value = settings.ListenPort;
            rdpPort.Value = settings.LocalRdpPort; sshPort.Value = settings.LocalSshPort; sshHost.Text = settings.SshHost;
            summary.Text = "Relais : " + settings.PublicHost + ":" + settings.PublicPort + Environment.NewLine + Environment.NewLine
                + "RDP : ce PC, port 3389" + Environment.NewLine + "SSH : " + (string.IsNullOrWhiteSpace(settings.SshHost) ? "désactivé" : settings.SshHost + ":" + settings.SshPort);
        }
        private async Task SaveSettings()
        {
            if (busy || relay != null) return;
            busy = true; UpdateState();
            try
            {
                var address = host.Text.Trim(); var target = sshHost.Text.Trim();
                Settings.CheckHost(address, "IP publique / DNS"); if (target.Length > 0) Settings.CheckHost(target, "Serveur SSH");
                var pub = (int)publicPort.Value; var listen = (int)listenPort.Value; var rp = (int)rdpPort.Value; var sp = (int)sshPort.Value;
                if (settings == null) settings = await Task.Run(() => Security.CreatePair(address, pub, listen, rp, sp, target));
                else
                {
                    settings.PublicHost = address; settings.PublicPort = pub; settings.ListenPort = listen;
                    settings.LocalRdpPort = rp; settings.LocalSshPort = sp; settings.SshHost = target;
                }
                settings.Validate(true); settings.Save(settingsPath); DisplaySettings();
                WriteLog("Configuration enregistrée. Freebox : TCP Internet " + pub + " vers ce PC, port " + listen + ".");
                WriteLog("Après modification de l'adresse ou de la cible SSH, exportez puis réimportez le profil agent.");
            }
            catch (Exception error) { ShowError(error); }
            finally { busy = false; UpdateState(); }
        }
        private async Task ExportProfile()
        {
            if (settings == null || busy) return;
            using (var password = new PasswordDialog(true))
            {
                if (password.ShowDialog(this) != DialogResult.OK) return;
                using (var save = new SaveFileDialog { Filter = "Profil ConnexionPont|*.rdptunnel", FileName = "client.rdptunnel" })
                {
                    if (save.ShowDialog(this) != DialogResult.OK) return;
                    busy = true; UpdateState();
                    try
                    {
                        var snapshot = Settings.FromBytes(settings.ToBytes()); var phrase = password.Password;
                        var encrypted = await Task.Run(() => Security.Export(snapshot, phrase));
                        File.WriteAllBytes(save.FileName, encrypted);
                        WriteLog("Profil agent exporté avec un mot de passe. La clé privée du relais est exclue.");
                    }
                    catch (Exception error) { ShowError(error); }
                    finally { busy = false; UpdateState(); }
                }
            }
        }
        private async Task ImportProfile()
        {
            if (busy || agent != null) return;
            using (var file = new OpenFileDialog { Filter = "Profil ConnexionPont|*.rdptunnel" })
            {
                if (file.ShowDialog(this) != DialogResult.OK) return;
                using (var password = new PasswordDialog(false))
                {
                    if (password.ShowDialog(this) != DialogResult.OK) return;
                    busy = true; UpdateState();
                    try
                    {
                        if (new FileInfo(file.FileName).Length > 131072) throw new InvalidDataException("Profil trop volumineux.");
                        var bytes = File.ReadAllBytes(file.FileName); var phrase = password.Password;
                        settings = await Task.Run(() => Security.Import(bytes, phrase)); settings.Save(settingsPath);
                        DisplaySettings(); WriteLog("Agent appairé. Cliquez sur Connecter.");
                    }
                    catch (Exception error) { ShowError(error); }
                    finally { busy = false; UpdateState(); }
                }
            }
        }
        private async Task StartEngine()
        {
            if (settings == null || busy || relay != null || agent != null) return;
            try
            {
                if (isRelay) { relay = new Relay(Settings.FromBytes(settings.ToBytes()), WriteLog); relay.Start(); }
                else { agent = new Agent(Settings.FromBytes(settings.ToBytes()), WriteLog); agentTask = agent.Start(); }
            }
            catch (Exception error) { StopEngine(); ShowError(error); }
            UpdateState(); await Task.FromResult(0);
        }
        private void StopEngine()
        {
            if (relay != null) { relay.Stop(); relay = null; WriteLog("Relais arrêté."); }
            if (agent != null) { agent.Stop(); agent = null; WriteLog("Agent arrêté."); }
            UpdateState();
        }
        private void UpdateState()
        {
            if (IsDisposed) return;
            var running = relay != null || agent != null;
            var connected = isRelay ? relay != null && relay.AgentConnected : agent != null && agent.Connected;
            fields.Enabled = !running && !busy;
            foreach (Control button in actions.Controls) button.Enabled = !running && !busy;
            start.Enabled = settings != null && !running && !busy; stop.Enabled = running;
            if (isRelay) export.Enabled = settings != null && !busy;
            rdp.Enabled = connected; ssh.Enabled = connected && settings != null && !string.IsNullOrWhiteSpace(settings.SshHost);
            status.Text = busy ? "Préparation..." : connected ? "CONNECTÉ — " + (isRelay ? relay.ActiveSessions : agent.ActiveSessions) + " session(s) active(s)" : running ? "EN ATTENTE — connexion / reconnexion" : "ARRÊTÉ";
            status.ForeColor = connected ? Color.FromArgb(15, 119, 74) : Color.FromArgb(150, 92, 12);
        }
        private void OpenRdp()
        { if (settings == null) throw new InvalidOperationException("Enregistrez une configuration."); Process.Start(new ProcessStartInfo("mstsc.exe", "/v:127.0.0.1:" + settings.LocalRdpPort) { UseShellExecute = true }); }
        private string SshArguments()
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.SshHost)) throw new InvalidOperationException("Renseignez un serveur SSH, puis réexportez le profil agent.");
            var user = sshUser.Text.Trim(); if (user.Length == 0) user = "utilisateur";
            if (!Regex.IsMatch(user, "^[a-zA-Z0-9_][a-zA-Z0-9_.@\\\\-]{0,127}$")) throw new ArgumentException("Nom d'utilisateur SSH invalide.");
            return "-o HostKeyAlias=connexionpont-" + settings.PairId + " -p " + settings.LocalSshPort + " -l " + user + " 127.0.0.1";
        }
        private string SshCommand() { return "ssh " + SshArguments(); }
        private void OpenSsh() { Process.Start(new ProcessStartInfo("ssh.exe", SshArguments()) { UseShellExecute = true }); }
        private async Task Diagnostics()
        {
            if (settings == null) { WriteLog("Importez d'abord un profil."); return; }
            WriteLog("Diagnostic RDP local : " + await Probe("127.0.0.1", 3389));
            if (!string.IsNullOrWhiteSpace(settings.SshHost)) WriteLog("Diagnostic SSH " + settings.SshHost + ":" + settings.SshPort + " : " + await Probe(settings.SshHost, settings.SshPort));
        }
        private static async Task<string> Probe(string address, int port)
        {
            using (var socket = new TcpClient())
            {
                try
                {
                    var connect = socket.ConnectAsync(address, port);
                    if (await Task.WhenAny(connect, Task.Delay(4000)) != connect) return "pas de réponse (4 s)";
                    await connect; return "port TCP accessible";
                }
                catch (Exception error) { return error.Message; }
            }
        }
    }

    internal sealed class PasswordDialog : Form
    {
        private readonly TextBox password = new TextBox(), confirm = new TextBox();
        public string Password { get { return password.Text; } }
        public PasswordDialog(bool exporting)
        {
            Text = exporting ? "Protéger le profil agent" : "Déverrouiller le profil agent";
            ClientSize = new Size(480, exporting ? 230 : 175); FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; Font = new Font("Segoe UI", 10);
            var hint = new Label { Text = exporting ? "Choisissez au moins 12 caractères. Transmettez le mot de passe séparément du fichier." : "Saisissez le mot de passe choisi lors de l'export.", Location = new Point(20, 15), Size = new Size(440, 45) };
            Controls.Add(hint); password.UseSystemPasswordChar = true; password.SetBounds(20, 70, 440, 30); Controls.Add(password);
            if (exporting) { confirm.UseSystemPasswordChar = true; confirm.SetBounds(20, 115, 440, 30); Controls.Add(confirm); }
            var ok = new Button { Text = "Valider", Location = new Point(260, exporting ? 175 : 120), Width = 95 };
            ok.Click += delegate
            {
                if (exporting && (Password.Length < 12 || Password != confirm.Text))
                { MessageBox.Show(this, "Les deux mots de passe doivent être identiques et contenir au moins 12 caractères."); return; }
                DialogResult = DialogResult.OK; Close();
            };
            var cancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, Location = new Point(365, exporting ? 175 : 120), Width = 95 };
            Controls.Add(ok); Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;
        }
    }
}
