using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Windows.Forms;

namespace DiscordProtonUpdater
{
    // This service never launches processes. Its process guard and state folder
    // are injected so tests cannot edit real preferences or completion records.
    public sealed class FirstLaunchSetup
    {
        private readonly string stateFolder;
        private readonly Action assertProtonClosed;
        public string MarkerPath { get { return Path.Combine(stateFolder, "FirstLaunchSetup.json"); } }
        public bool IsPending { get { return !File.Exists(MarkerPath); } }
        public FirstLaunchSetup(string folder, Action guard)
        { stateFolder = Path.GetFullPath(folder); assertProtonClosed = guard; }

        public static void AssertProtonClosed()
        {
            int session; using (Process self = Process.GetCurrentProcess()) session = self.SessionId;
            foreach (string name in new string[] { "ProtonVPN.Client", "ProtonVPN" })
            foreach (Process process in Process.GetProcessesByName(name))
            using (process)
            {
                try
                {
                    if (process.HasExited) continue;
                    if (process.SessionId == session) throw new InvalidOperationException("Exit Proton VPN from its system-tray icon before first-time setup.");
                }
                catch (InvalidOperationException) { if (!process.HasExited) throw; }
                catch { throw new InvalidOperationException("Proton VPN's exit could not be verified. Close its desktop client and retry."); }
            }
        }
        private static void RejectLinks(string path)
        {
            string current = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked settings or state paths are not supported.");
                current = Path.GetDirectoryName(current);
            }
        }
        private sealed class SettingsFile
        {
            public string PathName, Text;
            public byte[] Original, Candidate;
            public bool Bom;
            public Dictionary<string, object> Values;
            // Proton's outer settings format is a dictionary of strings/nulls.
            // Editing value spans leaves all unrelated bytes (including tokens)
            // untouched. Embedded JSON is decoded only for the edited keys.
            private static readonly Regex Entries = new Regex("(?<key>\"(?:\\\\.|[^\"\\\\])*\")\\s*:\\s*(?<value>\"(?:\\\\.|[^\"\\\\])*\"|null)");
            public SettingsFile(string path)
            {
                PathName = Path.GetFullPath(path); RejectLinks(PathName);
                if (new FileInfo(PathName).Length > 8 * 1024 * 1024) throw new IOException("Settings file is too large.");
                Original = File.ReadAllBytes(PathName);
                Bom = Original.Length >= 3 && Original[0] == 239 && Original[1] == 187 && Original[2] == 191;
                try
                {
                    Text = new UTF8Encoding(false, true).GetString(Original, Bom ? 3 : 0, Original.Length - (Bom ? 3 : 0));
                    Values = DesktopBackend.Json().DeserializeObject(Text) as Dictionary<string, object>;
                    if (Values == null) throw new FormatException();
                    foreach (object value in Values.Values) if (value != null && !(value is string)) throw new FormatException();
                    MatchCollection entries = Entries.Matches(Text);
                    HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (Match entry in entries)
                        if (!names.Add(DesktopBackend.Json().Deserialize<string>(entry.Groups["key"].Value))) throw new FormatException();
                    if (entries.Count != Values.Count) throw new FormatException();
                }
                catch { throw new IOException("Unsupported or invalid Proton settings format. No settings were changed."); }
            }
            public string Get(string key)
            { object value; return Values.TryGetValue(key, out value) ? value as string : null; }
            public void Build(Dictionary<string, string> updates)
            {
                string result = Text;
                MatchCollection entries = Entries.Matches(Text);
                HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    Match entry = entries[i];
                    string key = DesktopBackend.Json().Deserialize<string>(entry.Groups["key"].Value);
                    string value;
                    if (!updates.TryGetValue(key, out value)) continue;
                    found.Add(key);
                    if (String.Equals(Get(key), value, StringComparison.Ordinal)) continue;
                    Group span = entry.Groups["value"];
                    result = result.Remove(span.Index, span.Length).Insert(span.Index, DesktopBackend.Json().Serialize(value));
                }
                int count = Values.Count;
                foreach (KeyValuePair<string, string> item in updates)
                {
                    if (found.Contains(item.Key)) continue;
                    int end = result.LastIndexOf('}');
                    result = result.Insert(end, (count++ > 0 ? "," : "") + "\n  " + DesktopBackend.Json().Serialize(item.Key) + ": " + DesktopBackend.Json().Serialize(item.Value) + "\n");
                }
                Dictionary<string, object> parsed = DesktopBackend.Json().DeserializeObject(result) as Dictionary<string, object>;
                foreach (KeyValuePair<string, string> item in updates)
                    if (!String.Equals(parsed[item.Key] as string, item.Value, StringComparison.Ordinal)) throw new IOException("Setup candidate validation failed.");
                byte[] body = new UTF8Encoding(false).GetBytes(result);
                if (!Bom) Candidate = body;
                else { Candidate = new byte[body.Length + 3]; Candidate[0] = 239; Candidate[1] = 187; Candidate[2] = 191; Buffer.BlockCopy(body, 0, Candidate, 3, body.Length); }
            }
            public void AssertUnchanged(byte[] expected)
            {
                RejectLinks(PathName);
                if (!Equal(File.ReadAllBytes(PathName), expected)) throw new IOException("Proton settings changed during first-time setup. Retry to preserve the newer settings.");
            }
        }
        private static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        private static ArrayList ReadList(string text, string name)
        {
            if (text == null) return new ArrayList();
            try
            {
                object[] list = DesktopBackend.Json().DeserializeObject(text) as object[];
                if (list == null) throw new FormatException();
                return new ArrayList(list);
            }
            catch { throw new IOException("Unsupported " + name + " format. No settings were changed."); }
        }
        private static string Exclusions(string original)
        {
            ArrayList list = ReadList(original, "excluded locations"); bool hasBrazil = false;
            foreach (object item in list)
            {
                Dictionary<string, object> location = item as Dictionary<string, object>;
                if (location == null || !location.ContainsKey("Type") || !location.ContainsKey("CountryCode") || !(location["CountryCode"] is string))
                    throw new IOException("Unsupported excluded location entry. No settings were changed.");
                string type = Convert.ToString(location["Type"], CultureInfo.InvariantCulture);
                if (type != "0" && type != "1" && type != "2") throw new IOException("Unsupported excluded location type.");
                if (type == "0" && String.Equals((string)location["CountryCode"], "BR", StringComparison.OrdinalIgnoreCase)) hasBrazil = true;
            }
            if (hasBrazil) return original;
            list.Add(new Dictionary<string, object> { { "Type", 0 }, { "CountryCode", "BR" } });
            return DesktopBackend.Json().Serialize(list);
        }
        private static void CheckBool(SettingsFile file, string key)
        {
            string value = file.Get(key);
            if (file.Values.ContainsKey(key) && value != "true" && value != "false") throw new IOException("Unsupported " + key + " representation.");
        }
        public JobResult Apply(string accountPath)
        {
            RejectLinks(stateFolder); Directory.CreateDirectory(stateFolder);
            // An exclusive lock also prevents two copies from both doing setup.
            string lockPath = Path.Combine(stateFolder, "FirstLaunchSetup.lock"); RejectLinks(lockPath);
            using (FileStream gate = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (!IsPending) return new JobResult { Success = true, Action = "Setup", Message = "First-time setup was already completed. Preferences were left unchanged." };
                assertProtonClosed();
                string full = Path.GetFullPath(accountPath);
                if (!Regex.IsMatch(Path.GetFileName(full), "^UserSettings(?:\\.[a-zA-Z0-9_-]+)?\\.json$", RegexOptions.IgnoreCase))
                    throw new IOException("Select a Proton UserSettings account file.");
                SettingsFile user = new SettingsFile(full);
                SettingsFile global = new SettingsFile(Path.Combine(Path.GetDirectoryName(full), "GlobalSettings.json"));
                if (!user.Values.ContainsKey("IsUserSettingsMigrationDone") || user.Get("IsUserSettingsMigrationDone") != "true" || !global.Values.ContainsKey("FeatureFlags"))
                    throw new IOException("Unsupported or unfinished Proton settings. Sign in to Proton and let it finish setting up first.");
                CheckBool(global, "IsAutoLaunchEnabled"); CheckBool(user, "IsAutoConnectEnabled");
                CheckBool(user, "AreProtonProtocolsEnabled"); CheckBool(user, "IsSplitTunnelingEnabled");
                string protocol = user.Get("VpnProtocol");
                if (protocol != null && !Regex.IsMatch(protocol, "^(Smart|OpenVpnTcp|OpenVpnUdp|WireGuardUdp|WireGuardTcp|WireGuardTls|ProTunUdp|ProTunTcp|ProTunTls)$"))
                    throw new IOException("Unsupported Proton protocol representation.");
                string mode = user.Get("SplitTunnelingMode");
                if (mode != null && mode != "Standard" && mode != "Inverse") throw new IOException("Unsupported split tunneling mode.");
                foreach (object item in ReadList(user.Get("SplitTunnelingInverseAppsList"), "Included apps"))
                {
                    Dictionary<string, object> app = item as Dictionary<string, object>;
                    if (app == null || !app.ContainsKey("AppFilePath") || !(app["AppFilePath"] is string) ||
                        !app.ContainsKey("IsActive") || !(app["IsActive"] is bool) ||
                        !app.ContainsKey("AlternateAppFilePaths") || !(app["AlternateAppFilePaths"] is object[]))
                        throw new IOException("Unsupported Included apps entry. No settings were changed.");
                }
                // Numeric Type=0 is Fastest; ProTunUdp is Proton WireGuard,
                // distinct from the older WireGuardUdp implementation.
                string fastest = "{\"Type\":0,\"RecentId\":\"00000000-0000-0000-0000-000000000000\"}";
                user.Build(new Dictionary<string, string> {
                    { "IsAutoConnectEnabled", "true" }, { "DefaultConnection", fastest },
                    { "ExcludedLocationsList", Exclusions(user.Get("ExcludedLocationsList")) },
                    { "AreProtonProtocolsEnabled", "true" }, { "VpnProtocol", "ProTunUdp" },
                    { "IsSplitTunnelingEnabled", "true" }, { "SplitTunnelingMode", "Inverse" },
                    { "SplitTunnelingInverseAppsList", user.Get("SplitTunnelingInverseAppsList") ?? "[]" }
                });
                global.Build(new Dictionary<string, string> { { "IsAutoLaunchEnabled", "true" } });
                SettingsFile[] files = { user, global };
                List<SettingsFile> written = new List<SettingsFile>(); List<string> backups = new List<string>();
                string id = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 16);
                string markerTemporary = MarkerPath + "." + id + ".tmp";
                try
                {
                    foreach (SettingsFile file in files)
                    {
                        assertProtonClosed();
                        foreach (SettingsFile check in files) check.AssertUnchanged(written.Contains(check) ? check.Candidate : check.Original);
                        if (Equal(file.Original, file.Candidate)) continue;
                        string backupFolder = Path.Combine(Path.GetDirectoryName(file.PathName), "DiscordRuleBackups");
                        RejectLinks(backupFolder); Directory.CreateDirectory(backupFolder);
                        string backup = Path.Combine(backupFolder, "first-setup-" + (file == user ? "account-" : "global-") + id + ".json");
                        string temporary = file.PathName + "." + id + ".tmp";
                        try
                        {
                            File.WriteAllBytes(temporary, file.Candidate);
                            assertProtonClosed();
                            foreach (SettingsFile check in files) check.AssertUnchanged(written.Contains(check) ? check.Candidate : check.Original);
                            File.Replace(temporary, file.PathName, backup);
                            written.Add(file); backups.Add(backup);
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                    assertProtonClosed();
                    foreach (SettingsFile file in files) file.AssertUnchanged(file.Candidate);
                    File.WriteAllText(markerTemporary, "{\"SetupVersion\":1,\"CompletedUtc\":\"" + DateTime.UtcNow.ToString("o") + "\"}", new UTF8Encoding(false));
                    File.Move(markerTemporary, MarkerPath);
                }
                catch
                {
                    bool restored = true;
                    for (int i = written.Count - 1; i >= 0; i--)
                    {
                        SettingsFile file = written[i]; string temporary = file.PathName + ".rollback-" + id;
                        try
                        {
                            // Never overwrite a newer external edit while undoing
                            // a partial setup. Exact originals remain in backups.
                            assertProtonClosed(); file.AssertUnchanged(file.Candidate);
                            File.WriteAllBytes(temporary, file.Original); File.Replace(temporary, file.PathName, null);
                        }
                        catch { restored = false; }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                    if (!restored) throw new IOException("First-time setup stopped after a concurrent change. Review the exact originals in DiscordRuleBackups before retrying.");
                    throw;
                }
                finally { if (File.Exists(markerTemporary)) File.Delete(markerTemporary); }
                return new JobResult { Success = true, Action = "Setup", BackupPath = backups.Count == 0 ? null : String.Join(Environment.NewLine, backups.ToArray()),
                    Message = "First-time setup saved: start Proton with Windows, auto-connect, Fastest country excluding Brazil, Proton Protocols with Proton WireGuard, and Split tunneling in Include mode. Reopen Proton to register Windows startup and apply the preferences; account and feature availability still apply. Later launches will leave these preferences unchanged." };
            }
        }
    }

    public sealed class FirstLaunchForm : Form
    {
        private readonly IUpdaterBackend backend;
        private readonly FirstLaunchSetup setup;
        private readonly BackgroundWorker inspect = new BackgroundWorker(), apply = new BackgroundWorker();
        private readonly Timer timer = new Timer();
        private readonly ComboBox accounts = new ComboBox();
        private readonly Label status = new Label();
        private readonly Button continueButton = new Button(), retryButton = new Button();
        private bool busy, stopped, failed, binding;
        private int clearPolls;
        private AppInspection latest;
        private Icon appIcon;
        public JobResult Result { get; private set; }
        public FirstLaunchForm(IUpdaterBackend value, FirstLaunchSetup service)
        {
            backend = value; setup = service; Text = AppIdentity.Name + " | First-time setup";
            ClientSize = new Size(650, 430); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10F); BackColor = Color.FromArgb(243, 246, 250);
            using (Stream resource = typeof(FirstLaunchForm).Assembly.GetManifestResourceStream("AppIcon.ico"))
                if (resource != null) using (Icon original = new Icon(resource)) { appIcon = (Icon)original.Clone(); Icon = appIcon; }
            Label title = new Label { Text = "Set up Proton VPN once", Font = new Font("Segoe UI", 18F, FontStyle.Bold), Location = new Point(22, 16), Size = new Size(600, 40) };
            Label description = new Label { Location = new Point(24, 65), Size = new Size(600, 145), Text =
                "\u2022 Start with Windows and connect automatically\n\u2022 Default connection: Fastest country; exclude Brazil\n\u2022 Proton Protocols: ON, using Proton WireGuard\n\u2022 Split tunneling: ON, using Include mode (if supported)\n\nSelect your account below and exit Proton VPN from its tray icon. Setup applies automatically, then opens the usual updater." };
            accounts.Name = "FirstLaunchAccount"; accounts.Location = new Point(24, 220); accounts.Size = new Size(600, 30); accounts.DropDownStyle = ComboBoxStyle.DropDownList;
            accounts.SelectedIndexChanged += delegate { if (!binding) clearPolls = 0; };
            status.Location = new Point(24, 265); status.Size = new Size(600, 82); status.Text = "Checking Proton VPN and saved accounts...";
            continueButton.Text = "Continue without setup"; continueButton.Location = new Point(400, 370); continueButton.Size = new Size(224, 36);
            continueButton.Click += delegate { if (!busy) Close(); };
            retryButton.Text = "Retry setup"; retryButton.Location = new Point(24, 370); retryButton.Size = new Size(150, 36); retryButton.Visible = false;
            retryButton.Click += delegate { failed = false; clearPolls = 0; retryButton.Visible = false; Poll(); };
            Controls.AddRange(new Control[] { title, description, accounts, status, continueButton, retryButton });
            inspect.DoWork += delegate(object sender, DoWorkEventArgs e) { e.Result = backend.Inspect(); };
            inspect.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                if (stopped || IsDisposed) return;
                if (e.Error != null) { clearPolls = 0; status.Text = "Could not inspect Proton. Close its desktop client and retry."; return; }
                latest = (AppInspection)e.Result;
                string selected = accounts.SelectedItem == null ? null : ((AccountInfo)accounts.SelectedItem).Path;
                binding = true; accounts.Items.Clear();
                foreach (AccountInfo account in latest.Accounts) { int index = accounts.Items.Add(account); if (String.Equals(account.Path, selected, StringComparison.OrdinalIgnoreCase)) accounts.SelectedIndex = index; }
                if (selected == null && latest.Accounts.Count == 1) accounts.SelectedIndex = 0;
                binding = false;
                clearPolls = latest.Known && latest.ProtonCount == 0 ? Math.Min(2, clearPolls + 1) : 0;
                if (failed) return;
                if (!latest.Known) status.Text = "Proton's exit could not be verified. Close its desktop client and wait.";
                else if (latest.ProtonCount > 0) status.Text = "Proton VPN is running. Exit it from its system-tray icon; setup will continue automatically. Discord may stay open.";
                else if (accounts.SelectedItem == null) status.Text = latest.Accounts.Count == 0 ? "No Proton account found. Sign in to Proton once, then exit it. You can continue to the updater and browse to a settings file." : "Choose the Proton account to configure. Other saved accounts will be left unchanged.";
                else if (clearPolls < 2) status.Text = "Confirming Proton VPN has fully exited...";
                else
                {
                    busy = true; accounts.Enabled = false; continueButton.Enabled = false; timer.Stop();
                    status.Text = "Saving first-time preferences and exact backups...";
                    apply.RunWorkerAsync(((AccountInfo)accounts.SelectedItem).Path);
                }
            };
            apply.DoWork += delegate(object sender, DoWorkEventArgs e) { e.Result = setup.Apply((string)e.Argument); };
            apply.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                if (stopped || IsDisposed) return;
                busy = false; accounts.Enabled = true; continueButton.Enabled = true;
                if (e.Error == null) { Result = (JobResult)e.Result; Close(); }
                else { failed = true; status.Text = e.Error.Message; retryButton.Visible = true; timer.Start(); }
            };
            timer.Interval = 1000; timer.Tick += delegate { Poll(); };
            Shown += delegate { Poll(); timer.Start(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; return; } stopped = true; timer.Stop(); };
        }
        private void Poll() { if (!busy && !stopped && !inspect.IsBusy) inspect.RunWorkerAsync(); }
        protected override void Dispose(bool disposing)
        { if (disposing) { stopped = true; timer.Stop(); timer.Dispose(); if (appIcon != null) { appIcon.Dispose(); appIcon = null; } } base.Dispose(disposing); }
    }
}
