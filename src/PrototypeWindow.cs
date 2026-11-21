using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DiscordProtonUpdater
{
    public sealed class PrototypeRequest
    {
        public string Action { get; set; }
        public string SettingsPath { get; set; }
        public List<string> Roots { get; set; }
        public bool AccountConfirmed { get; set; }
        public bool AllowGracefulRestart { get; set; }
        public bool DefaultConnectionConfirmed { get; set; }
        public bool RoutingConfirmed { get; set; }
    }
    public sealed class PrototypeResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string BackupPath { get; set; }
    }
    public interface IPrototypeBackend
    {
        AppInspection Inspect();
        PrototypeResult Execute(PrototypeRequest request, Action<string> log);
        void AddAccount(string path);
        void AddRoot(string path);
    }
    public sealed class PrototypeBackend : IPrototypeBackend
    {
        private readonly DesktopBackend desktop;
        private readonly string folder;
        private readonly string workflow;
        public PrototypeBackend(string packageFolder)
        {
            folder = packageFolder;
            desktop = new DesktopBackend(folder);
            workflow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiscordProtonUpdater", "PrototypeRuns", Guid.NewGuid().ToString("N"));
        }
        public AppInspection Inspect() { return desktop.Inspect(); }
        public void AddAccount(string path) { desktop.AddAccount(path); }
        public void AddRoot(string path) { desktop.AddRoot(path); }
        public PrototypeResult Execute(PrototypeRequest request, Action<string> log)
        {
            string run = Path.Combine(workflow, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(run);
            string input = Path.Combine(run, "request.json");
            File.WriteAllText(input, DesktopBackend.Json().Serialize(request), new UTF8Encoding(false));
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File \"" +
                    Path.Combine(folder, "PrototypeWorker.ps1") + "\" -RequestPath \"" + input + "\"",
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (Process worker = new Process())
            {
                worker.StartInfo = start;
                worker.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) log(e.Data); };
                worker.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) log(e.Data); };
                worker.Start(); worker.BeginOutputReadLine(); worker.BeginErrorReadLine();
                if (!worker.WaitForExit(150000))
                {
                    try { worker.Kill(); } catch { }
                    return new PrototypeResult { Message = "Automation timed out. Observe Proton before retrying. Its connection state is uncertain." };
                }
                worker.WaitForExit();
                string output = Path.Combine(run, "result.json");
                if (!File.Exists(output)) return new PrototypeResult { Message = "Prototype worker failed. Check Activity and retry after inspecting Proton." };
                return DesktopBackend.Json().Deserialize<PrototypeResult>(File.ReadAllText(output));
            }
        }
    }
    public sealed class PrototypeForm : Form
    {
        private readonly IPrototypeBackend backend;
        private readonly BackgroundWorker inspection = new BackgroundWorker();
        private readonly BackgroundWorker operation = new BackgroundWorker();
        private readonly Timer poll = new Timer { Interval = 1000 };
        private readonly Timer finish = new Timer { Interval = 2500 };
        private readonly Button[] steps = new Button[4];
        private readonly ComboBox accounts = new ComboBox { Name = "PrototypeAccounts", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly CheckBox accountConfirmed = new CheckBox { Name = "AccountConfirmed", Text = "I verified that the selected account matches Proton's signed-in account.", AutoSize = true };
        private readonly CheckBox restartConfirmed = new CheckBox { Name = "RestartConfirmed", Text = "Allow Proton to exit through its menu for the rule refresh.", AutoSize = true };
        private readonly CheckBox connectionConfirmed = new CheckBox { Name = "ConnectionConfirmed", Text = "Reconnect using Proton's connection-card preferences (the server may change).", AutoSize = true };
        private readonly CheckBox routingConfirmed = new CheckBox { Name = "RoutingConfirmed", Text = "I verified the Proton account, connection, and desired routing.", AutoSize = true };
        private readonly Label status = new Label { Name = "PrototypeStatus", Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Color.FromArgb(225,243,234), TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label details = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly RichTextBox activity = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9), BackColor = Color.White };
        private Image artwork;
        private AppInspection last;
        private int stage, generation, pendingGeneration;
        private bool busy, binding, closing;
        public PrototypeForm(IPrototypeBackend value)
        {
            backend = value;
            Text = AppIdentity.Name + " | UI Automation prototype";
            ClientSize = new Size(900, 780); MinimumSize = new Size(880, 780);
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(243,246,250);
            StartPosition = FormStartPosition.CenterScreen;
            using (Stream resource = typeof(PrototypeForm).Assembly.GetManifestResourceStream("AppIcon.ico"))
                if (resource != null) using (Icon original = new Icon(resource)) Icon = (Icon)original.Clone();
            Build();
            inspection.DoWork += delegate(object sender, DoWorkEventArgs e) { e.Result = backend.Inspect(); };
            inspection.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                if (closing || busy || generation != pendingGeneration) return;
                last = e.Error == null ? (AppInspection)e.Result : new AppInspection { Known = false, ScanDetail = "Process inspection failed." };
                if (stage == 0)
                {
                    string selected = accounts.SelectedItem is AccountInfo ? ((AccountInfo)accounts.SelectedItem).Path : null;
                    binding = true;
                    accounts.Items.Clear();
                    foreach (AccountInfo account in last.Accounts)
                    {
                        accounts.Items.Add(account);
                        if (String.Equals(account.Path, selected, StringComparison.OrdinalIgnoreCase)) accounts.SelectedItem = account;
                    }
                    if (accounts.SelectedIndex < 0 && accounts.Items.Count == 1) accounts.SelectedIndex = 0;
                    binding = false;
                    AccountInfo current = accounts.SelectedItem as AccountInfo;
                    if (!String.Equals(selected, current == null ? null : current.Path, StringComparison.OrdinalIgnoreCase))
                        accountConfirmed.Checked = false;
                }
                details.Text = "Proton clients: " + last.ProtonCount + "   Discord/helpers: " + last.DiscordCount +
                    "   Unverified: " + last.UnknownCount + "   " + last.ScanDetail;
                Render();
            };
            operation.WorkerReportsProgress = true;
            operation.ProgressChanged += delegate(object sender, ProgressChangedEventArgs e) { Log(Convert.ToString(e.UserState)); };
            operation.DoWork += delegate(object sender, DoWorkEventArgs e)
            {
                try { e.Result = backend.Execute((PrototypeRequest)e.Argument, text => operation.ReportProgress(0, text)); }
                catch (Exception error) { e.Result = new PrototypeResult { Message = error.Message }; }
            };
            operation.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                busy = false; generation++;
                PrototypeResult result = e.Error == null ? (PrototypeResult)e.Result : new PrototypeResult { Message = e.Error.Message };
                status.Text = result.Message; Log(result.Message);
                if (!String.IsNullOrEmpty(result.BackupPath)) Log("Backup: " + result.BackupPath);
                if (result.Success) stage++;
                if (stage == 4) { status.Text = "Process completed"; finish.Start(); }
                Render(); RequestInspection();
            };
            accounts.SelectedIndexChanged += delegate { if (!binding) { accountConfirmed.Checked = false; Render(); } };
            foreach (CheckBox check in new[] { accountConfirmed, restartConfirmed, connectionConfirmed, routingConfirmed })
                check.CheckedChanged += delegate { Render(); };
            poll.Tick += delegate { RequestInspection(); };
            finish.Tick += delegate { finish.Stop(); Close(); };
            Shown += delegate { status.Text = "Open Proton Home in English and verify the selected account, then begin."; RequestInspection(); poll.Start(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (busy) { e.Cancel = true; return; }
                closing = true; poll.Stop(); finish.Stop();
            };
        }
        private void Build()
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22,14,22,14), ColumnCount = 1, RowCount = 9 };
            foreach (int height in new[] { 70, 62, 58, 68, 30, 38, 150, 34 })
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            using (Stream resource = typeof(PrototypeForm).Assembly.GetManifestResourceStream("HeaderArtwork.png"))
                if (resource != null) using (Image original = Image.FromStream(resource)) artwork = new Bitmap(original);
            header.Controls.Add(new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = artwork }, 0, 0);
            header.Controls.Add(new Label { Text = AppIdentity.Name + "\r\nProton UI Automation prototype 1", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 18, FontStyle.Bold) }, 1, 0);
            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(new Label { Text = "This experiment disconnects through Proton's UI. Refresh requires your choice to let Proton exit gracefully so its cached settings reload. Disconnection may allow direct traffic depending on Kill Switch.", Dock = DockStyle.Fill }, 0, 1);
            layout.Controls.Add(status, 0, 2);
            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent,50)); actions.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            string[] labels = { "1. Close Discord + disconnect VPN", "2. Refresh rules (graceful restart)", "3. Reconnect Proton VPN", "4. Launch Discord" };
            for (int i = 0; i < steps.Length; i++)
            {
                int index = i;
                steps[i] = new Button { Name = "PrototypeStep" + (i+1), Text = labels[i], Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = new Padding(3), UseVisualStyleBackColor = false };
                steps[i].Click += delegate { Start(index); };
                actions.Controls.Add(steps[i], i % 2, i / 2);
            }
            layout.Controls.Add(actions, 0, 3);
            layout.Controls.Add(new Label { Text = "SELECTED PROTON ACCOUNT SETTINGS", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft }, 0, 4);
            layout.Controls.Add(accounts, 0, 5);
            FlowLayoutPanel choices = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            foreach (CheckBox check in new[] { accountConfirmed, restartConfirmed, connectionConfirmed, routingConfirmed }) choices.Controls.Add(check);
            FlowLayoutPanel links = new FlowLayoutPanel { AutoSize = true };
            LinkLabel browse = new LinkLabel { Text = "Browse account file", AutoSize = true };
            browse.LinkClicked += delegate
            {
                if (busy || stage != 0) return;
                using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Proton account (UserSettings*.json)|UserSettings*.json", RestoreDirectory = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) { backend.AddAccount(dialog.FileName); RequestInspection(); }
            };
            LinkLabel folder = new LinkLabel { Text = "Choose Discord folder", AutoSize = true };
            folder.LinkClicked += delegate
            {
                if (busy || stage != 0) return;
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                    if (dialog.ShowDialog(this) == DialogResult.OK) { backend.AddRoot(dialog.SelectedPath); RequestInspection(); }
            };
            links.Controls.Add(browse); links.Controls.Add(folder); choices.Controls.Add(links);
            layout.Controls.Add(choices, 0, 6); layout.Controls.Add(details, 0, 7);
            layout.Controls.Add(activity, 0, 8); Controls.Add(layout); Render();
        }
        private void RequestInspection()
        {
            if (!closing && !busy && !inspection.IsBusy) { pendingGeneration = generation; inspection.RunWorkerAsync(); }
        }
        private bool CanStart(int index)
        {
            AccountInfo account = accounts.SelectedItem as AccountInfo;
            if (busy || stage != index || last == null || !last.Known || last.UnknownCount != 0 ||
                !last.ScanReady || account == null || !account.Ready) return false;
            if (index == 0) return accountConfirmed.Checked;
            if (last.DiscordCount != 0) return false;
            if (index == 1) return restartConfirmed.Checked;
            if (index == 2) return connectionConfirmed.Checked;
            return routingConfirmed.Checked;
        }
        private void Render()
        {
            for (int i = 0; i < steps.Length; i++)
            {
                steps[i].Enabled = CanStart(i);
                steps[i].BackColor = steps[i].Enabled ? Color.FromArgb(105,74,224) : Color.FromArgb(222,227,236);
                steps[i].ForeColor = steps[i].Enabled ? Color.White : Color.FromArgb(130,141,159);
            }
            accounts.Enabled = !busy && stage == 0;
            accountConfirmed.Enabled = !busy && stage == 0;
            restartConfirmed.Enabled = !busy && stage == 1;
            connectionConfirmed.Enabled = !busy && stage == 2;
            routingConfirmed.Enabled = !busy && stage == 3;
        }
        private void Start(int index)
        {
            if (!CanStart(index)) return;
            PrototypeRequest pending = new PrototypeRequest
            {
                Action = new[] { "Disconnect", "Refresh", "Reconnect", "LaunchDiscord" }[index],
                SettingsPath = ((AccountInfo)accounts.SelectedItem).Path,
                Roots = new List<string>(last.Roots), AccountConfirmed = accountConfirmed.Checked,
                AllowGracefulRestart = restartConfirmed.Checked,
                DefaultConnectionConfirmed = connectionConfirmed.Checked, RoutingConfirmed = routingConfirmed.Checked
            };
            busy = true; generation++; status.Text = "Working. Observe Proton if it requests confirmation."; Render();
            operation.RunWorkerAsync(pending);
        }
        private void Log(string text) { activity.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { closing = true; poll.Dispose(); finish.Dispose(); if (Icon != null) Icon.Dispose(); if (artwork != null) artwork.Dispose(); }
            base.Dispose(disposing);
        }
    }
    internal static class PrototypeProgram
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                string folder = AppDomain.CurrentDomain.BaseDirectory;
                DesktopBackend desktop = new DesktopBackend(folder);
                FirstLaunchSetup setup = new FirstLaunchSetup(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscordProtonUpdater"), FirstLaunchSetup.AssertProtonClosed);
                JobResult setupResult;
                if (!setup.RunRequiredSetup(delegate {
                    using (FirstLaunchForm first = new FirstLaunchForm(desktop, setup)) { return first.ShowDialog() == DialogResult.OK ? first.Result : null; }
                }, out setupResult)) return;
                Application.Run(new PrototypeForm(new PrototypeBackend(folder)));
            }
            catch (Exception error) { MessageBox.Show(error.Message, AppIdentity.Name + " prototype", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
