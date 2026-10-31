using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using DiscordProtonUpdater;

class FirstLaunchSetupTests
{
    class FixtureBackend : IUpdaterBackend
    {
        public volatile int ProtonCount;
        public List<AccountInfo> Accounts = new List<AccountInfo>();
        public AppInspection Inspect() { return new AppInspection { ProtonCount = ProtonCount, Accounts = new List<AccountInfo>(Accounts) }; }
        public JobResult Execute(OperationRequest request, Action<string> log) { throw new Exception("First-time form must not invoke Close or Refresh."); }
        public void AddRoot(string path) { throw new Exception("Unexpected installation change."); }
        public void AddAccount(string path) { throw new Exception("Unexpected account change."); }
    }
    static void Pump(int milliseconds)
    { DateTime until = DateTime.UtcNow.AddMilliseconds(milliseconds); while (DateTime.UtcNow < until) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } }
    static void PrepareForm(FirstLaunchForm form)
    { form.ShowInTaskbar = false; form.Opacity = 0; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.Show(); }
    static void Click(Button button)
    { button.BeginInvoke(new MethodInvoker(delegate { button.PerformClick(); })); Application.DoEvents(); }
    class Fixture
    {
        public string Folder, Account, Global, State;
        public byte[] AccountBefore, GlobalBefore;
        public Fixture(string root, string name, bool bom)
        {
            Folder = Path.Combine(root, name); Directory.CreateDirectory(Folder);
            State = Path.Combine(Folder, "state"); Account = Path.Combine(Folder, "UserSettings.fixture.json"); Global = Path.Combine(Folder, "GlobalSettings.json");
            Dictionary<string, string> user = new Dictionary<string, string> {
                { "IsUserSettingsMigrationDone", "true" }, { "IsAutoConnectEnabled", "false" },
                { "DefaultConnection", "{\"Type\":1,\"RecentId\":\"00000000-0000-0000-0000-000000000000\"}" },
                { "ExcludedLocationsList", "[{\"Type\":0,\"CountryCode\":\"US\",\"Extra\":\"keep\"}]" },
                { "AreProtonProtocolsEnabled", "false" }, { "VpnProtocol", "Smart" },
                { "IsSplitTunnelingEnabled", "false" }, { "SplitTunnelingMode", "Standard" },
                { "SplitTunnelingInverseAppsList", "[{\"AppFilePath\":\"C:\\\\Other\\\\Browser.exe\",\"AlternateAppFilePaths\":[],\"IsActive\":false}]" },
                { "Theme", "Light" }, { "FakeOpaqueAccountData", "synthetic value; never decrypted" }
            };
            File.WriteAllText(Account, DesktopBackend.Json().Serialize(user), new UTF8Encoding(bom));
            // Intentional whitespace/escape sequence: must survive byte-for-byte.
            File.WriteAllText(Global, "{\n  \"FeatureFlags\": \"synthetic opaque flags\",\n  \"IsAutoLaunchEnabled\": \"false\",\n  \"FakeOpaqueToken\"  :  \"keep\\u0020exact\\\\value\"\n}", new UTF8Encoding(bom));
            Capture();
        }
        public void Capture() { AccountBefore = File.ReadAllBytes(Account); GlobalBefore = File.ReadAllBytes(Global); }
        public FirstLaunchSetup Service(Action guard) { return new FirstLaunchSetup(State, guard); }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static Dictionary<string, object> Read(string path) { return DesktopBackend.Json().DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>; }
    static void Set(string path, string key, string value)
    { Dictionary<string, object> data = Read(path); data[key] = value; File.WriteAllText(path, DesktopBackend.Json().Serialize(data), new UTF8Encoding(false)); }
    static bool Equal(byte[] a, byte[] b) { return Convert.ToBase64String(a) == Convert.ToBase64String(b); }
    static void Unchanged(Fixture f)
    { Check(Equal(f.AccountBefore, File.ReadAllBytes(f.Account)) && Equal(f.GlobalBefore, File.ReadAllBytes(f.Global)), "Failed setup changed preferences."); Check(!File.Exists(Path.Combine(f.State, "FirstLaunchSetup.json")), "Failed setup recorded completion."); }
    static void Reject(Action action)
    { bool rejected = false; try { action(); } catch (IOException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } Check(rejected, "Expected setup rejection."); }
    [STAThread]
    static int Main(string[] args)
    {
        string root = args[0]; Directory.CreateDirectory(root);
        Fixture basic = new Fixture(root, "success", true);
        FirstLaunchSetup service = basic.Service(delegate { });
        Check(service.IsPending, "New user skipped setup."); JobResult result = service.Apply(basic.Account);
        Check(result.Success && !service.IsPending, "Setup did not finish.");
        Dictionary<string, object> user = Read(basic.Account), global = Read(basic.Global);
        foreach (string key in new string[] { "IsAutoConnectEnabled", "AreProtonProtocolsEnabled", "IsSplitTunnelingEnabled" }) Check((string)user[key] == "true", "Wrong toggle: " + key);
        Check((string)global["IsAutoLaunchEnabled"] == "true" && (string)user["VpnProtocol"] == "ProTunUdp" && (string)user["SplitTunnelingMode"] == "Inverse", "Wrong startup/protocol/mode.");
        Dictionary<string, object> connection = DesktopBackend.Json().DeserializeObject((string)user["DefaultConnection"]) as Dictionary<string, object>;
        Check((int)connection["Type"] == 0, "Default is not Fastest.");
        object[] exclusions = DesktopBackend.Json().DeserializeObject((string)user["ExcludedLocationsList"]) as object[];
        Check(exclusions.Length == 2 && (string)((Dictionary<string, object>)exclusions[1])["CountryCode"] == "BR", "Brazil not added.");
        Check((string)((Dictionary<string, object>)exclusions[0])["Extra"] == "keep", "Existing exclusion changed.");
        Check((string)user["SplitTunnelingInverseAppsList"] == (string)DesktopBackend.Json().Deserialize<Dictionary<string, object>>(new UTF8Encoding(true).GetString(basic.AccountBefore).TrimStart('\uFEFF'))["SplitTunnelingInverseAppsList"], "Other Included apps changed.");
        Check(File.ReadAllBytes(basic.Account)[0] == 239 && File.ReadAllBytes(basic.Global)[0] == 239, "BOM lost.");
        Check(File.ReadAllText(basic.Global).Contains("\"FakeOpaqueToken\"  :  \"keep\\u0020exact\\\\value\""), "Unrelated token bytes rewritten.");
        string backupFolder = Path.Combine(basic.Folder, "DiscordRuleBackups");
        Check(Equal(basic.AccountBefore, File.ReadAllBytes(Directory.GetFiles(backupFolder, "first-setup-account-*")[0])), "Account backup differs.");
        Check(Equal(basic.GlobalBefore, File.ReadAllBytes(Directory.GetFiles(backupFolder, "first-setup-global-*")[0])), "Global backup differs.");
        Console.WriteLine("PASS: Requested preferences, preserved exclusions/apps/opaque bytes/BOM, exact backups, completion marker.");

        Set(basic.Account, "VpnProtocol", "Smart"); Set(basic.Global, "IsAutoLaunchEnabled", "false"); basic.Capture();
        JobResult repeated = basic.Service(delegate { throw new Exception("Completed setup must not inspect processes."); }).Apply(Path.Combine(root, "missing-account.json"));
        Check(repeated.Success && Equal(basic.AccountBefore, File.ReadAllBytes(basic.Account)) && Equal(basic.GlobalBefore, File.ReadAllBytes(basic.Global)) && Directory.GetFiles(backupFolder).Length == 2, "Later launch overwrote manual preferences.");
        Console.WriteLine("PASS: New instance/extracted copy skips completed setup and preserves later manual changes.");

        Fixture running = new Fixture(root, "running", false);
        Reject(delegate { running.Service(delegate { throw new InvalidOperationException("fake running Proton"); }).Apply(running.Account); }); Unchanged(running);
        Console.WriteLine("PASS: Running Proton prevents writes and completion.");

        Fixture racing = new Fixture(root, "racing", false); int calls = 0;
        Reject(delegate { racing.Service(delegate { if (++calls == 3) Set(racing.Global, "NewExternalSetting", "keep"); }).Apply(racing.Account); });
        Check(Equal(racing.AccountBefore, File.ReadAllBytes(racing.Account)) && (string)Read(racing.Global)["NewExternalSetting"] == "keep" && racing.Service(delegate { }).IsPending, "Concurrent change was overwritten.");
        Console.WriteLine("PASS: Settings changed before replacement are preserved and setup remains pending.");

        Fixture partial = new Fixture(root, "partial", false); calls = 0;
        Reject(delegate { partial.Service(delegate { if (++calls == 4) throw new IOException("Simulated failure before second file write"); }).Apply(partial.Account); }); Unchanged(partial);
        Check(partial.Service(delegate { }).Apply(partial.Account).Success, "Failed setup could not retry.");
        Console.WriteLine("PASS: Failure between account/global writes restores exact originals; retry succeeds.");

        Fixture marker = new Fixture(root, "marker-error", false); Directory.CreateDirectory(Path.Combine(marker.State, "FirstLaunchSetup.json"));
        Reject(delegate { marker.Service(delegate { }).Apply(marker.Account); }); Unchanged(marker);
        Console.WriteLine("PASS: Completion-marker failure rolls back both settings files.");

        foreach (string bad in new string[] { "{\"IsUserSettingsMigrationDone\":\"true\",\"VpnProtocol\":\"Smart\",\"VpnProtocol\":\"Smart\"}", "{\"IsUserSettingsMigrationDone\":true}", "{broken" })
        {
            Fixture invalid = new Fixture(root, "invalid-" + Guid.NewGuid().ToString("N"), false); File.WriteAllText(invalid.Account, bad); invalid.Capture();
            Reject(delegate { invalid.Service(delegate { }).Apply(invalid.Account); }); Unchanged(invalid);
        }
        Console.WriteLine("PASS: Invalid JSON, duplicate keys, and unsupported value types fail before writes.");

        Fixture malformed = new Fixture(root, "bad-lists", false); Set(malformed.Account, "SplitTunnelingInverseAppsList", "[42]"); malformed.Capture();
        Reject(delegate { malformed.Service(delegate { }).Apply(malformed.Account); }); Unchanged(malformed);
        Console.WriteLine("PASS: Unsupported Included app entries do not enable an invalid configuration.");

        Fixture duplicate = new Fixture(root, "existing-Brazil", false); string existing = "[ {\"Type\":0,\"CountryCode\":\"BR\"}, {\"Type\":2,\"CountryCode\":\"US\",\"CityName\":\"Example\"} ]";
        Set(duplicate.Account, "ExcludedLocationsList", existing); duplicate.Service(delegate { }).Apply(duplicate.Account);
        Check((string)Read(duplicate.Account)["ExcludedLocationsList"] == existing, "Existing Brazil duplicated or exclusions rewritten.");
        int backups = Directory.GetFiles(Path.Combine(duplicate.Folder, "DiscordRuleBackups")).Length;
        new FirstLaunchSetup(Path.Combine(duplicate.Folder, "second-state"), delegate { }).Apply(duplicate.Account);
        Check(Directory.GetFiles(Path.Combine(duplicate.Folder, "DiscordRuleBackups")).Length == backups, "Already-correct preferences created a backup.");
        Console.WriteLine("PASS: Existing Brazil exclusion is unchanged; already-correct settings need only a marker.");

        Fixture defaults = new Fixture(root, "defaults", false); File.WriteAllText(defaults.Account, "{\"IsUserSettingsMigrationDone\":\"true\"}"); defaults.Service(delegate { }).Apply(defaults.Account);
        Check((string)Read(defaults.Account)["SplitTunnelingInverseAppsList"] == "[]", "Missing default list not initialized.");
        Console.WriteLine("PASS: Omitted default preference keys are added with Proton's string encoding.");

        Fixture sparseGlobal = new Fixture(root, "sparse-global", false);
        File.WriteAllText(sparseGlobal.Global, "{\"UserId\":\"synthetic opaque identity\",\"IsAutoLaunchEnabled\":null}");
        Set(sparseGlobal.Account, "IsAutoConnectEnabled", null);
        sparseGlobal.Service(delegate { }).Apply(sparseGlobal.Account);
        Check((string)Read(sparseGlobal.Global)["UserId"] == "synthetic opaque identity" && (string)Read(sparseGlobal.Global)["IsAutoLaunchEnabled"] == "true", "Omitted global defaults or null toggles were rejected.");
        Console.WriteLine("PASS: First-launch setup accepts omitted feature flags and null default toggles without changing opaque identity data.");

        Fixture locked = new Fixture(root, "locked", false); Directory.CreateDirectory(locked.State);
        using (FileStream gate = new FileStream(Path.Combine(locked.State, "FirstLaunchSetup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Reject(delegate { locked.Service(delegate { }).Apply(locked.Account); });
        Unchanged(locked); Console.WriteLine("PASS: Concurrent updater instances cannot both perform setup.");

        Fixture other = new Fixture(root, "other-account", false); byte[] otherBytes = File.ReadAllBytes(other.Account);
        Fixture selected = new Fixture(root, "selected-account", false); selected.Service(delegate { }).Apply(selected.Account);
        Check(Equal(otherBytes, File.ReadAllBytes(other.Account)), "Unselected account was changed.");
        Console.WriteLine("PASS: Only the selected account is changed.");

        Fixture firstUi = new Fixture(root, "first-ui", false); FixtureBackend backend = new FixtureBackend { ProtonCount = 1 };
        backend.Accounts.Add(new AccountInfo { Path = firstUi.Account, Display = "Example account" });
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using (FirstLaunchForm form = new FirstLaunchForm(backend, firstUi.Service(delegate { if (backend.ProtonCount > 0) throw new IOException("Fake Proton is running"); })))
        {
            PrepareForm(form); Pump(1250); Check(form.Result == null && !form.ApplySetupButton.Enabled && !File.Exists(Path.Combine(firstUi.State, "FirstLaunchSetup.json")), "Form allowed setup while Proton was running.");
            Click(form.ApplySetupButton); Check(form.Result == null, "Disabled setup button was bypassed.");
            backend.ProtonCount = 0; Pump(2500); Check(form.Result == null && form.ApplySetupButton.Enabled && firstUi.Service(delegate { }).IsPending, "Setup did not wait for an explicit click after Proton exited.");
            using (Bitmap preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size)); preview.Save(Path.Combine(root, "first-launch-preview.png")); }
            Click(form.ApplySetupButton); Pump(350); Check(form.Result != null && form.Result.Success, "Setup click did not complete after verified exit.");
        }
        Console.WriteLine("PASS: First-launch setup is disabled until Proton exits, waits for an explicit click, then completes and renders with example data.");

        Fixture multi = new Fixture(root, "multi-ui", false); string second = Path.Combine(multi.Folder, "UserSettings.second.json"); File.Copy(multi.Account, second);
        backend = new FixtureBackend(); backend.Accounts.Add(new AccountInfo { Path = multi.Account, Display = "Account one" }); backend.Accounts.Add(new AccountInfo { Path = second, Display = "Account two" });
        using (FirstLaunchForm form = new FirstLaunchForm(backend, multi.Service(delegate { })))
        {
            PrepareForm(form); Pump(1250); Check(form.Result == null && !form.ApplySetupButton.Enabled, "Form guessed an account on a multi-account PC.");
            ComboBox choice = (ComboBox)form.Controls.Find("FirstLaunchAccount", true)[0]; Check(choice.SelectedIndex == -1, "An account was preselected.");
            choice.SelectedIndex = 1; Pump(2500); Check(form.Result == null && form.ApplySetupButton.Enabled, "Choosing an account bypassed the required click.");
            Click(form.ApplySetupButton); Pump(350); Check(form.Result != null && form.Result.Success, "Selected account was not configured.");
        }
        Check(Equal(multi.AccountBefore, File.ReadAllBytes(multi.Account)) && (string)Read(second)["VpnProtocol"] == "ProTunUdp", "Multi-account setup changed the wrong account.");
        Console.WriteLine("PASS: Multi-account first launch requires a selection and only configures that account.");

        Fixture canceled = new Fixture(root, "canceled", false); FirstLaunchSetup pending = canceled.Service(delegate { }); JobResult gateResult;
        Check(!pending.RunRequiredSetup(delegate { return null; }, out gateResult), "Canceled setup opened the updater.");
        Check(!pending.RunRequiredSetup(delegate { return new JobResult { Success = true }; }, out gateResult), "A success result without a completion marker opened the updater.");
        Unchanged(canceled);
        using (FirstLaunchForm form = new FirstLaunchForm(new FixtureBackend(), pending))
        {
            PrepareForm(form); Pump(1250); Check(!form.ApplySetupButton.Enabled, "Setup with no account was enabled.");
            form.Close(); Check(form.Result == null && pending.IsPending, "Closing setup fabricated completion.");
        }
        Check(pending.RunRequiredSetup(delegate { return pending.Apply(canceled.Account); }, out gateResult) && gateResult.Success, "Completed setup did not unlock startup.");
        Check(pending.RunRequiredSetup(delegate { throw new Exception("Completed startup must not show setup again."); }, out gateResult), "Later launch did not skip completed setup.");
        Console.WriteLine("PASS: Cancel/X, no account, and missing completion records cannot open the updater; verified completion unlocks later launches.");

        Fixture retryUi = new Fixture(root, "retry-ui", false); bool failOnce = true; backend = new FixtureBackend(); backend.Accounts.Add(new AccountInfo { Path = retryUi.Account, Display = "Example retry account" });
        using (FirstLaunchForm form = new FirstLaunchForm(backend, retryUi.Service(delegate { if (failOnce) { failOnce = false; throw new IOException("Synthetic setup failure; retry required."); } })))
        {
            PrepareForm(form); Pump(2500); Check(form.ApplySetupButton.Enabled, "Retry fixture was not ready for its first click."); Click(form.ApplySetupButton); Pump(350);
            Check(!failOnce && form.Result == null && form.Visible && retryUi.Service(delegate { }).IsPending, "Failed setup progressed into the updater or was not attempted.");
            Pump(2500); Check(form.ApplySetupButton.Enabled && form.ApplySetupButton.Text == "Retry setup", "Failure could not be retried after fresh exit checks.");
            Click(form.ApplySetupButton); Pump(350); Check(form.Result != null && form.Result.Success, "Required setup retry did not complete.");
        }
        Console.WriteLine("PASS: Setup errors keep the user at setup; an explicit successful retry is required to continue.");
        return 0;
    }
}
