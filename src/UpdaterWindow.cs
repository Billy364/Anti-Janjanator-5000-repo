using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Democracy Enforcer 5000")]
[assembly: System.Reflection.AssemblyProduct("Democracy Enforcer 5000")]
[assembly: System.Reflection.AssemblyDescription("Discord rules updater for Proton VPN split tunneling")]
[assembly: System.Reflection.AssemblyVersion("11.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("11.0.0.0")]

namespace DiscordProtonUpdater
{
    public static class AppIdentity
    {
        public const string Name = "Democracy Enforcer 5000";
    }

    public sealed class AccountInfo
    {
        public string Path { get; set; }
        public string Display { get; set; }
        public string Detail { get; set; }
        public bool Ready { get; set; }
        public bool NeedsSplitTunnelingActivation { get; set; }
        public override string ToString() { return Display; }
    }
    public sealed class AppInspection
    {
        public int DiscordCount;
        public int ProtonCount;
        public int UnknownCount;
        public bool Known = true;
        public bool ScanReady;
        public int ExecutableCount;
        public string ScanDetail = "Checking installations...";
        public List<string> Roots = new List<string>();
        public List<AccountInfo> Accounts = new List<AccountInfo>();
        public List<string> UnverifiedProcesses = new List<string>();
        public bool AppsExited { get { return Known && DiscordCount == 0 && ProtonCount == 0 && UnknownCount == 0; } }
    }
    public sealed class OperationRequest
    {
        public string Action { get; set; }
        public string SettingsPath { get; set; }
        public List<string> Roots { get; set; }
    }
    public sealed class JobResult
    {
        public bool Success { get; set; }
        public string Action { get; set; }
        public string Message { get; set; }
        public int Total { get; set; }
        public string BackupPath { get; set; }
        public string[] Added { get; set; }
        public string[] Removed { get; set; }
        public bool EnabledSplitTunneling { get; set; }
        public string ProtonLaunchStatus { get; set; }
        public string ProtonLaunchWarning { get; set; }
    }
    public interface IUpdaterBackend
    {
        AppInspection Inspect();
        JobResult Execute(OperationRequest request, Action<string> log);
        void AddRoot(string path);
        void AddAccount(string path);
    }
    public static class UiPolicy
    {
        public static bool CanRefresh(AppInspection state, int clearPolls, bool busy, AccountInfo account)
        {
            return !busy && state != null && state.AppsExited && clearPolls >= 2 &&
                state.ScanReady && state.Roots.Count > 0 && account != null && account.Ready;
        }
        public static bool CanClose(AppInspection state, bool busy)
        {
            return !busy && state != null && (state.DiscordCount > 0 || state.ProtonCount > 0 || state.UnknownCount > 0);
        }
    }
    public sealed class DesktopBackend : IUpdaterBackend
    {
        private readonly string packageFolder;
        private readonly object sync = new object();
        private readonly HashSet<string> rememberedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> extraAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] mainNames = { "Discord", "DiscordPTB", "DiscordCanary", "ProtonVPN.Client", "ProtonVPN" };
        private static readonly string[] helperNames = { "Update", "updater_launcher", "DiscordHookHelper", "DiscordHookHelper64", "DiscordSystemHelper", "audio_effects_helper", "gpu_encoder_helper" };
        public DesktopBackend(string folder) { packageFolder = folder; }
        public static JavaScriptSerializer Json()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer(); serializer.MaxJsonLength = 8 * 1024 * 1024; return serializer;
        }
        private static bool IsDiscordName(string name)
        { return String.Equals(name, "Discord", StringComparison.OrdinalIgnoreCase) || String.Equals(name, "DiscordPTB", StringComparison.OrdinalIgnoreCase) || String.Equals(name, "DiscordCanary", StringComparison.OrdinalIgnoreCase); }
        private static bool IsProtonName(string name)
        { return String.Equals(name, "ProtonVPN.Client", StringComparison.OrdinalIgnoreCase) || String.Equals(name, "ProtonVPN", StringComparison.OrdinalIgnoreCase); }
        private static bool Under(string path, IEnumerable<string> roots)
        {
            if (String.IsNullOrEmpty(path)) return false;
            foreach (string root in roots) if (path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder path, ref uint length);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        public static string Image(Process process)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                // Querying the image needs less access than enumerating modules.
                handle = OpenProcess(0x1000, false, process.Id);
                if (handle != IntPtr.Zero)
                {
                    StringBuilder path = new StringBuilder(32768); uint length = (uint)path.Capacity;
                    if (QueryFullProcessImageNameW(handle, 0, path, ref length)) return path.ToString();
                }
            }
            catch { }
            finally { if (handle != IntPtr.Zero) CloseHandle(handle); }
            try { return process.MainModule.FileName; } catch { return null; }
        }
        public static void ObserveProcess(AppInspection state, string name, int session, Func<bool> hasExited, Func<int> sessionId, Func<string> readImage)
        {
            try
            {
                if (hasExited() || sessionId() != session) return;
                string image = readImage();
                // A shutdown may complete between enumeration and image lookup.
                if (hasExited()) return;
                if (IsProtonName(name)) state.ProtonCount++;
                else if (IsDiscordName(name) || Under(image, state.Roots)) state.DiscordCount++;
                if (image == null)
                {
                    state.UnknownCount++;
                    if (!state.UnverifiedProcesses.Contains(name)) state.UnverifiedProcesses.Add(name);
                }
            }
            catch
            {
                try { if (hasExited()) return; } catch { }
                state.Known = false;
                if (!state.UnverifiedProcesses.Contains(name)) state.UnverifiedProcesses.Add(name);
            }
        }
        private static string ReadSharedText(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true)) return reader.ReadToEnd();
        }
        public void AddRoot(string path)
        {
            DirectoryInfo folder = new DirectoryInfo(System.IO.Path.GetFullPath(path));
            if (!folder.Exists || !IsDiscordName(folder.Name)) throw new InvalidOperationException("Choose the installation folder named Discord, DiscordPTB, or DiscordCanary.");
            if ((folder.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Choose the real installation folder, not a linked folder.");
            lock (sync) rememberedRoots.Add(folder.FullName.TrimEnd('\\'));
        }
        public void AddAccount(string path)
        { lock (sync) extraAccounts.Add(System.IO.Path.GetFullPath(path)); }
        public static AccountInfo ReadAccount(string path)
        {
            AccountInfo account = new AccountInfo(); account.Path = path;
            account.Display = System.IO.Path.GetFileName(path) + "  |  saved " + File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss");
            try
            {
                if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidOperationException();
                Dictionary<string, object> data = Json().DeserializeObject(ReadSharedText(path)) as Dictionary<string, object>;
                if (data == null || !(data.ContainsKey("IsUserSettingsMigrationDone") || data.ContainsKey("VpnProtocol") || data.ContainsKey("IsSplitTunnelingEnabled") || data.ContainsKey("SplitTunnelingMode") || data.ContainsKey("SplitTunnelingInverseAppsList")))
                { account.Detail = "Unsupported Proton settings format."; return account; }
                object enabledValue, modeValue, appsValue;
                data.TryGetValue("IsSplitTunnelingEnabled", out enabledValue); data.TryGetValue("SplitTunnelingMode", out modeValue); data.TryGetValue("SplitTunnelingInverseAppsList", out appsValue);
                if ((enabledValue != null && !(enabledValue is string)) || (modeValue != null && !(modeValue is string)) || (appsValue != null && !(appsValue is string)))
                { account.Detail = "Unsupported Proton preference encoding."; return account; }
                string enabled = enabledValue as string ?? "false", mode = modeValue as string ?? "Standard";
                if ((enabled != "true" && enabled != "false") || (mode != "Standard" && mode != "Inverse"))
                { account.Detail = "Unsupported Proton split tunneling values."; return account; }
                if (appsValue != null && !(Json().DeserializeObject((string)appsValue) is object[]))
                { account.Detail = "Included apps is not a valid list. Apply Include mode in Proton again."; return account; }
                account.Ready = true;
                account.NeedsSplitTunnelingActivation = enabled != "true" || mode != "Inverse";
                account.Detail = enabled != "true" ? "Split tunneling is off. Refresh will enable Include mode and update Discord's rules automatically." :
                    mode != "Inverse" ? "This account uses Exclude mode. Refresh will switch to Include mode and update Discord's rules automatically." :
                    appsValue == null ? "Include mode is enabled. No saved list yet; Refresh will create the Discord entries." : "Include mode is enabled. This account is ready.";
            }
            catch { account.Detail = "Settings could not be read. Reload after Proton finishes saving, or choose another file."; account.Ready = false; }
            return account;
        }
        private List<AccountInfo> Accounts()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] folders = { System.IO.Path.Combine(local, "Proton", "Proton VPN", "Storage"), System.IO.Path.Combine(local, "Proton", "VPN", "Storage"), System.IO.Path.Combine(local, "ProtonVPN", "Storage") };
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < folders.Length; i++)
            {
                if (!Directory.Exists(folders[i])) continue;
                string[] files = Directory.GetFiles(folders[i], "UserSettings*.json", SearchOption.TopDirectoryOnly);
                foreach (string path in files) paths.Add(path);
                if (i == 0 && files.Length > 0) break;
            }
            lock (sync) foreach (string path in extraAccounts) if (File.Exists(path)) paths.Add(path);
            List<string> sorted = new List<string>(paths); sorted.Sort(delegate(string a, string b) { return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)); });
            List<AccountInfo> result = new List<AccountInfo>(); foreach (string path in sorted) result.Add(ReadAccount(path)); return result;
        }
        public AppInspection Inspect()
        {
            AppInspection result = new AppInspection();
            int session; using (Process self = Process.GetCurrentProcess()) session = self.SessionId;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (string name in new string[] { "Discord", "DiscordPTB", "DiscordCanary" })
            { string path = System.IO.Path.Combine(local, name); if (Directory.Exists(path)) lock (sync) rememberedRoots.Add(path); }
            // Remember custom installations before closing the apps, so the same
            // roots remain available after the owning process disappears.
            foreach (string name in new string[] { "Discord", "DiscordPTB", "DiscordCanary" })
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId != session) continue;
                        string path = Image(process); if (path == null) continue;
                        DirectoryInfo directory = new DirectoryInfo(System.IO.Path.GetDirectoryName(path));
                        while (directory != null) { if (IsDiscordName(directory.Name)) { lock (sync) rememberedRoots.Add(directory.FullName); break; } directory = directory.Parent; }
                    }
                    catch { }
                }
            }
            lock (sync) foreach (string root in rememberedRoots) if (Directory.Exists(root)) result.Roots.Add(root);
            HashSet<string> names = new HashSet<string>(mainNames, StringComparer.OrdinalIgnoreCase);
            foreach (string name in helperNames) names.Add(name);
            try
            {
                foreach (string root in result.Roots)
                {
                    Queue<string> queue = new Queue<string>(); queue.Enqueue(root); bool foundMain = false;
                    while (queue.Count > 0)
                    {
                        string directory = queue.Dequeue();
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("A linked folder was found. Choose the real Discord installation.");
                        foreach (string file in Directory.GetFiles(directory))
                        {
                            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("A linked file was found in the installation.");
                            if (!String.Equals(System.IO.Path.GetExtension(file), ".exe", StringComparison.OrdinalIgnoreCase)) continue;
                            string name = System.IO.Path.GetFileNameWithoutExtension(file); names.Add(name); result.ExecutableCount++;
                            if (IsDiscordName(name)) foundMain = true;
                        }
                        foreach (string child in Directory.GetDirectories(directory)) queue.Enqueue(child);
                    }
                    if (!foundMain) throw new IOException("The selected folder does not contain a Discord application executable.");
                }
                result.ScanReady = result.Roots.Count > 0;
                result.ScanDetail = result.ScanReady ? result.ExecutableCount + " executables found in " + result.Roots.Count + " Discord installation(s)." : "Discord installation not found. Choose its folder below.";
            }
            catch (Exception error) { result.ScanReady = false; result.ScanDetail = "Scan needs attention: " + error.Message; }
            foreach (string name in names)
            {
                Process[] processes;
                try { processes = Process.GetProcessesByName(name); } catch { result.Known = false; continue; }
                foreach (Process process in processes)
                {
                    using (process)
                    {
                        ObserveProcess(result, name, session, delegate { return process.HasExited; }, delegate { return process.SessionId; }, delegate { return Image(process); });
                    }
                }
            }
            try { result.Accounts = Accounts(); } catch { result.ScanDetail += " Account settings could not be listed; use Browse settings."; }
            return result;
        }
        public JobResult Execute(OperationRequest request, Action<string> log)
        {
            string worker = System.IO.Path.Combine(packageFolder, "UpdaterWorker.ps1");
            if (!File.Exists(worker)) throw new FileNotFoundException("UpdaterWorker.ps1 is missing. Keep all package files together.");
            string runFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscordProtonUpdater", "Runs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runFolder);
            string requestPath = System.IO.Path.Combine(runFolder, "request.json");
            File.WriteAllText(requestPath, Json().Serialize(request), new UTF8Encoding(false));
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            start.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + worker + "\" -RequestPath \"" + requestPath + "\"";
            start.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            start.UseShellExecute = false; start.CreateNoWindow = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
            using (Process process = new Process())
            {
                process.StartInfo = start;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) log(e.Data); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) log(e.Data); };
                process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
                string resultPath = System.IO.Path.Combine(runFolder, "result.json");
                if (!File.Exists(resultPath)) throw new InvalidOperationException("The operation could not finish. See the activity log for the PowerShell error.");
                return Json().Deserialize<JobResult>(File.ReadAllText(resultPath));
            }
        }
    }

    public sealed class UpdaterForm : Form
    {
        private readonly IUpdaterBackend backend;
        private readonly BackgroundWorker inspection = new BackgroundWorker();
        private readonly BackgroundWorker operation = new BackgroundWorker();
        private readonly Timer poll = new Timer();
        private readonly ToolTip tips = new ToolTip();
        private AppInspection last;
        private int clearPolls;
        private bool busy;
        private bool bindingAccounts;
        private bool closing;
        private int inspectionGeneration, pendingInspectionGeneration;
        private string successText;
        private bool refreshSucceeded;
        private Label protonStatus, discordStatus, protonDetail, discordDetail, warning, scanDetail, accountDetail, footer;
        private ComboBox accounts;
        private Button closeButton, refreshButton;
        private RichTextBox log;
        private Image headerArtwork;
        private Icon appIcon;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private bool resourcesDisposed;
        private static readonly Color ink = Color.FromArgb(29, 39, 58);
        private static readonly Color muted = Color.FromArgb(93, 107, 130);
        private static readonly Color accent = Color.FromArgb(105, 74, 224);
        public Button CloseAppsButton { get { return closeButton; } }
        public Button RefreshRulesButton { get { return refreshButton; } }
        public AppInspection LastInspection { get { return last; } }
        public UpdaterForm(IUpdaterBackend value)
            : this(value, null) { }
        public UpdaterForm(IUpdaterBackend value, JobResult setupResult)
        {
            backend = value; Text = AppIdentity.Name; Name = "DiscordProtonUpdater";
            ClientSize = new Size(900, 792); MinimumSize = new Size(880, 768); StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10F); BackColor = Color.FromArgb(243, 246, 250); ForeColor = ink;
            using (Stream resource = typeof(UpdaterForm).Assembly.GetManifestResourceStream("AppIcon.ico"))
            {
                if (resource != null) { using (Icon original = new Icon(resource)) appIcon = (Icon)original.Clone(); Icon = appIcon; }
            }
            Build();
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Open " + AppIdentity.Name, null, delegate { Show(); WindowState = FormWindowState.Normal; Activate(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit", null, delegate { Close(); });
            trayIcon = new NotifyIcon { Text = AppIdentity.Name, Icon = appIcon ?? SystemIcons.Application, ContextMenuStrip = trayMenu, Visible = true };
            trayIcon.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
            inspection.DoWork += delegate(object sender, DoWorkEventArgs e) { e.Result = backend.Inspect(); };
            inspection.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                if (closing || IsDisposed) return;
                if (busy || pendingInspectionGeneration != inspectionGeneration) { if (!busy) RequestInspection(); return; }
                if (e.Error != null) { last = new AppInspection(); last.Known = false; last.ScanDetail = "Could not inspect apps: " + e.Error.Message; }
                else last = (AppInspection)e.Result;
                clearPolls = last.AppsExited ? Math.Min(3, clearPolls + 1) : 0;
                BindAccounts(); RenderState();
            };
            operation.WorkerReportsProgress = true;
            operation.ProgressChanged += delegate(object sender, ProgressChangedEventArgs e) { AppendLog(Convert.ToString(e.UserState)); };
            operation.DoWork += delegate(object sender, DoWorkEventArgs e)
            {
                try { e.Result = backend.Execute((OperationRequest)e.Argument, delegate(string text) { operation.ReportProgress(0, text); }); }
                catch (Exception error) { e.Result = new JobResult { Success = false, Message = error.Message }; }
            };
            operation.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                busy = false; clearPolls = 0; inspectionGeneration++;
                last = new AppInspection { Known = false, ScanDetail = "Rechecking app exit and account settings..." };
                JobResult result = e.Error == null ? (JobResult)e.Result : new JobResult { Success = false, Message = e.Error.Message };
                if (result.Success)
                {
                    refreshSucceeded = result.Action == "Refresh";
                    successText = result.Message; AppendLog("SUCCESS: " + result.Message);
                    if (!String.IsNullOrEmpty(result.ProtonLaunchWarning)) AppendLog("WARNING: " + result.ProtonLaunchWarning);
                    if (result.Action == "Refresh") AppendLog("Discord executable rules: " + result.Total);
                    if (!String.IsNullOrEmpty(result.BackupPath)) AppendLog("Backup: " + result.BackupPath);
                }
                else { successText = null; AppendLog("STOPPED: " + result.Message); MessageBox.Show(this, result.Message, "Operation did not complete", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                RenderState(); RequestInspection();
            };
            poll.Interval = 1000; poll.Tick += delegate { RequestInspection(); };
            Shown += delegate {
                if (setupResult != null) { AppendLog(setupResult.Message); if (!String.IsNullOrEmpty(setupResult.BackupPath)) AppendLog("First-time setup backups: " + setupResult.BackupPath); }
                AppendLog("Monitoring the desktop apps. Select the Proton account you want to update."); RequestInspection(); poll.Start();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (busy) { e.Cancel = true; footer.Text = "Wait for the current close/refresh operation to finish."; return; }
                closing = true; poll.Stop();
            };
            FormClosed += delegate { closing = true; poll.Stop(); };
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !resourcesDisposed)
            {
                resourcesDisposed = true; closing = true; poll.Stop();
                if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); }
                if (trayMenu != null) trayMenu.Dispose();
                tips.Dispose(); poll.Dispose();
                if (headerArtwork != null) headerArtwork.Dispose();
                if (appIcon != null) appIcon.Dispose();
            }
            base.Dispose(disposing);
        }
        private Label Label(string text, float size, bool bold)
        { return new Label { Text = text, AutoSize = false, Dock = DockStyle.Fill, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = ink, TextAlign = ContentAlignment.MiddleLeft }; }
        private Button ActionButton(string text, bool primary)
        {
            Button button = new Button { Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Font = new Font("Segoe UI Semibold", 11F), BackColor = primary ? accent : Color.White, ForeColor = primary ? Color.White : accent, UseVisualStyleBackColor = false, Enabled = false };
            button.FlatAppearance.BorderSize = 1; button.FlatAppearance.BorderColor = accent; return button;
        }
        private Panel Card(string title, out Label status, out Label detail)
        {
            Panel card = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, Padding = new Padding(16, 9, 16, 9), BorderStyle = BorderStyle.FixedSingle };
            TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.Controls.Add(Label(title, 10F, true), 0, 0); status = Label("Checking...", 16F, true); table.Controls.Add(status, 0, 1);
            detail = Label("Reading process status", 9F, false); detail.ForeColor = muted; table.Controls.Add(detail, 0, 2); card.Controls.Add(table); return card;
        }
        private void Build()
        {
            TableLayoutPanel main = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 14, 22, 14), ColumnCount = 1, RowCount = 8 };
            float[] heights = { 120, 104, 66, 56, 120, 30 };
            foreach (float height in heights) main.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); main.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            using (Stream resource = typeof(UpdaterForm).Assembly.GetManifestResourceStream("HeaderArtwork.png"))
            {
                if (resource != null) using (Image original = Image.FromStream(resource)) headerArtwork = new Bitmap(original);
            }
            PictureBox artwork = new PictureBox { Name = "HeaderArtwork", AccessibleName = "Header artwork", Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom, Image = headerArtwork, Margin = new Padding(0, 4, 18, 4), TabStop = false };
            header.Controls.Add(artwork, 0, 0);
            TableLayoutPanel heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0), Padding = new Padding(0, 20, 0, 20) };
            heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            heading.Controls.Add(Label(AppIdentity.Name, 22F, true), 0, 0);
            Label subtitle = Label("Close the apps, then refresh Proton VPN's Included apps.", 10F, false); subtitle.ForeColor = muted; heading.Controls.Add(subtitle, 0, 1);
            header.Controls.Add(heading, 1, 0); main.Controls.Add(header, 0, 0);
            TableLayoutPanel cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            Panel proton = Card("PROTON VPN CLIENT", out protonStatus, out protonDetail); proton.Margin = new Padding(0, 0, 6, 0);
            Panel discord = Card("DISCORD", out discordStatus, out discordDetail); discord.Margin = new Padding(6, 0, 0, 0); cards.Controls.Add(proton, 0, 0); cards.Controls.Add(discord, 1, 0); main.Controls.Add(cards, 0, 1);
            warning = Label("Checking whether the apps have exited...", 10F, false); warning.Padding = new Padding(13, 5, 13, 5); warning.BackColor = Color.FromArgb(233, 237, 247); warning.Margin = new Padding(0, 0, 0, 10); main.Controls.Add(warning, 0, 2);
            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            closeButton = ActionButton("1. Close both apps", false); closeButton.Name = "CloseAppsButton"; closeButton.Margin = new Padding(0, 0, 6, 0);
            refreshButton = ActionButton("2. Refresh split tunneling rules", true); refreshButton.Name = "RefreshRulesButton"; refreshButton.Margin = new Padding(6, 0, 0, 0);
            closeButton.Click += delegate { StartOperation("Close"); }; refreshButton.Click += delegate { StartOperation("Refresh"); };
            actions.Controls.Add(closeButton, 0, 0); actions.Controls.Add(refreshButton, 1, 0); main.Controls.Add(actions, 0, 3);
            TableLayoutPanel setup = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0, 6, 0, 0) };
            setup.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); setup.RowStyles.Add(new RowStyle(SizeType.Absolute, 31)); setup.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); setup.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            setup.Controls.Add(Label("PROTON ACCOUNT SETTINGS", 9F, true), 0, 0);
            accounts = new ComboBox { Name = "AccountSelector", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F), DropDownWidth = 850 };
            accounts.SelectedIndexChanged += delegate { if (!bindingAccounts) { successText = null; RenderState(); } }; setup.Controls.Add(accounts, 0, 1);
            accountDetail = Label("Choose the account file to update. No account data is shared.", 9F, false); accountDetail.ForeColor = muted; setup.Controls.Add(accountDetail, 0, 2);
            FlowLayoutPanel links = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            LinkLabel reload = new LinkLabel { Text = "Reload setup", AutoSize = true, LinkColor = accent, Margin = new Padding(0, 3, 20, 0) };
            reload.LinkClicked += delegate { if (!busy) { clearPolls = 0; successText = null; RequestInspection(); } };
            LinkLabel browse = new LinkLabel { Text = "Browse settings file...", AutoSize = true, LinkColor = accent, Margin = new Padding(0, 3, 20, 0) };
            browse.LinkClicked += delegate
            {
                if (busy) return;
                using (OpenFileDialog picker = new OpenFileDialog { Title = "Select the Proton account settings file", Filter = "Proton account settings (UserSettings*.json)|UserSettings*.json|JSON files (*.json)|*.json", CheckFileExists = true, RestoreDirectory = true })
                { if (picker.ShowDialog(this) == DialogResult.OK) { backend.AddAccount(picker.FileName); RequestInspection(); } }
            };
            LinkLabel folder = new LinkLabel { Text = "Choose Discord folder...", AutoSize = true, LinkColor = accent, Margin = new Padding(0, 3, 0, 0) };
            folder.LinkClicked += delegate
            {
                if (busy) return;
                using (FolderBrowserDialog picker = new FolderBrowserDialog { Description = "Select the installation folder named Discord, DiscordPTB, or DiscordCanary", ShowNewFolderButton = false })
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    try { backend.AddRoot(picker.SelectedPath); clearPolls = 0; RequestInspection(); }
                    catch (Exception error) { MessageBox.Show(this, error.Message, "Folder could not be selected", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                }
            };
            links.Controls.Add(reload); links.Controls.Add(browse); links.Controls.Add(folder); setup.Controls.Add(links, 0, 3); main.Controls.Add(setup, 0, 4);
            scanDetail = Label("Scanning Discord executables...", 9F, false); scanDetail.ForeColor = muted; main.Controls.Add(scanDetail, 0, 5);
            TableLayoutPanel activity = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 5, 0, 0) };
            activity.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); activity.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); activity.Controls.Add(Label("ACTIVITY", 9F, true), 0, 0);
            log = new RichTextBox { Name = "ActivityLog", Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, ForeColor = ink, Font = new Font("Consolas", 9F), BorderStyle = BorderStyle.FixedSingle, DetectUrls = false, WordWrap = true };
            activity.Controls.Add(log, 0, 1); main.Controls.Add(activity, 0, 6);
            footer = Label("Desktop clients and Discord helpers are monitored. Proton's background service is left running.", 8.5F, false); footer.ForeColor = muted; main.Controls.Add(footer, 0, 7); Controls.Add(main);
            RenderState();
        }
        private void RequestInspection()
        { if (!closing && !busy && !inspection.IsBusy) { pendingInspectionGeneration = inspectionGeneration; inspection.RunWorkerAsync(); } }
        private AccountInfo SelectedAccount { get { return accounts.SelectedItem as AccountInfo; } }
        private void BindAccounts()
        {
            if (last == null || accounts.DroppedDown || busy) return;
            string selected = SelectedAccount == null ? null : SelectedAccount.Path;
            bindingAccounts = true;
            try
            {
                accounts.Items.Clear();
                foreach (AccountInfo account in last.Accounts) accounts.Items.Add(account);
                for (int i = 0; i < accounts.Items.Count; i++) if (String.Equals(((AccountInfo)accounts.Items[i]).Path, selected, StringComparison.OrdinalIgnoreCase)) { accounts.SelectedIndex = i; break; }
                if (accounts.SelectedIndex < 0 && accounts.Items.Count == 1) accounts.SelectedIndex = 0;
            }
            finally { bindingAccounts = false; }
        }
        private static void PaintStatus(Label status, Label detail, int count, string qualifier)
        {
            status.Text = count > 0 ? "Running" : "Exited"; status.ForeColor = count > 0 ? Color.FromArgb(156, 93, 8) : Color.FromArgb(35, 119, 77);
            detail.Text = count > 0 ? count + " " + qualifier : "No matching process is running";
        }
        private void RenderState()
        {
            AccountInfo account = SelectedAccount;
            closeButton.Enabled = UiPolicy.CanClose(last, busy);
            refreshButton.Enabled = UiPolicy.CanRefresh(last, clearPolls, busy, account);
            accounts.Enabled = !busy;
            refreshButton.BackColor = refreshButton.Enabled ? accent : Color.FromArgb(222, 227, 236);
            refreshButton.ForeColor = refreshButton.Enabled ? Color.White : Color.FromArgb(130, 141, 159);
            if (last == null) return;
            PaintStatus(protonStatus, protonDetail, last.ProtonCount, "client process(es)");
            PaintStatus(discordStatus, discordDetail, last.DiscordCount, "process(es), including helpers");
            if (!last.Known)
            {
                protonStatus.Text = "Unverified"; discordStatus.Text = "Unverified";
                protonDetail.Text = "Process status could not be read"; discordDetail.Text = "Refresh remains locked";
            }
            scanDetail.Text = last.ScanDetail; tips.SetToolTip(scanDetail, String.Join(Environment.NewLine, last.Roots.ToArray()));
            accountDetail.Text = account == null ? (last.Accounts.Count == 0 ? "No settings file found. Sign in to Proton or browse to the account file." : "Select the account you want to update; saved times help identify it.") : account.Detail;
            tips.SetToolTip(accounts, account == null ? "" : account.Path);
            string text;
            if (busy) text = "Working... The buttons stay locked until the current operation finishes.";
            else if (!last.Known || last.UnknownCount > 0) text = last.UnverifiedProcesses.Count > 0 ? "Cannot verify process paths: " + String.Join(", ", last.UnverifiedProcesses.ToArray()) + ". Refresh stays locked." : "App status cannot be fully verified. Refresh is locked. Close any inaccessible apps manually, then reload.";
            else if (refreshSucceeded && !String.IsNullOrEmpty(successText) && last.ProtonCount > 0 && last.DiscordCount == 0) text = successText;
            else if (!last.AppsExited) text = "Apps are running. Click Close both apps; Refresh unlocks only after all matching processes have exited.";
            else if (clearPolls < 2) text = "Confirming both apps have fully exited...";
            else if (!last.ScanReady) text = last.ScanDetail;
            else if (account == null) text = "Both apps are closed. Choose your Proton account settings file to enable Refresh.";
            else if (!account.Ready) text = account.Detail;
            else if (account.NeedsSplitTunnelingActivation) text = "Both apps are fully exited. Refresh will turn Split tunneling ON in Include mode and update Discord's rules.";
            else text = successText ?? "Both apps are fully exited. You can now refresh the split tunneling rules.";
            warning.Text = text;
            bool ready = !busy && last.AppsExited && clearPolls >= 2 && account != null && account.Ready && last.ScanReady;
            warning.BackColor = ready ? Color.FromArgb(225, 243, 234) : Color.FromArgb(255, 242, 219);
        }
        private void StartOperation(string action)
        {
            if (busy || last == null) return;
            if (action == "Refresh" && !UiPolicy.CanRefresh(last, clearPolls, false, SelectedAccount)) return;
            if (action == "Close" && !UiPolicy.CanClose(last, false)) return;
            OperationRequest request = new OperationRequest { Action = action, SettingsPath = SelectedAccount == null ? null : SelectedAccount.Path, Roots = new List<string>(last.Roots) };
            busy = true; successText = null; refreshSucceeded = false; clearPolls = 0; inspectionGeneration++;
            AppendLog(action == "Close" ? "Closing Discord and the Proton VPN desktop client..." : "Rechecking app exit, enabling Include mode if needed, and refreshing the selected account's rules...");
            RenderState(); operation.RunWorkerAsync(request);
        }
        private void AppendLog(string text)
        {
            if (String.IsNullOrWhiteSpace(text) || IsDisposed) return;
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); log.SelectionStart = log.TextLength; log.ScrollToCaret();
        }
    }
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                DesktopBackend backend = new DesktopBackend(AppDomain.CurrentDomain.BaseDirectory);
                FirstLaunchSetup setup = new FirstLaunchSetup(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscordProtonUpdater"), FirstLaunchSetup.AssertProtonClosed);
                JobResult result;
                if (!setup.RunRequiredSetup(delegate {
                    using (FirstLaunchForm first = new FirstLaunchForm(backend, setup)) { return first.ShowDialog() == DialogResult.OK ? first.Result : null; }
                }, out result)) return;
                Application.Run(new UpdaterForm(backend, result));
            }
            catch (Exception error) { MessageBox.Show(error.Message, AppIdentity.Name, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
