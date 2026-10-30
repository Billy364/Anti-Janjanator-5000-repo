using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using DiscordProtonUpdater;

class CompatibilityTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    class SlowBackend : IUpdaterBackend
    {
        public readonly ManualResetEvent ScanStarted = new ManualResetEvent(false), ReleaseScan = new ManualResetEvent(false);
        public volatile bool Running = true, DelayNextScan;
        public AppInspection Inspect()
        {
            bool captured = Running;
            if (DelayNextScan) { DelayNextScan = false; ScanStarted.Set(); if (!ReleaseScan.WaitOne(5000)) throw new Exception("Fixture scan timed out."); }
            AppInspection state = new AppInspection { DiscordCount = captured ? 1 : 0, ScanReady = true, ScanDetail = "Fixture scan" };
            state.Roots.Add("C:\\Fixture\\Discord");
            state.Accounts.Add(new AccountInfo { Path = "C:\\Fixture\\UserSettings.fixture.json", Display = "Example account", Ready = true });
            return state;
        }
        public JobResult Execute(OperationRequest request, Action<string> log) { Running = false; return new JobResult { Success = true, Action = "Close", Message = "Fixture apps exited." }; }
        public void AddRoot(string path) { }
        public void AddAccount(string path) { }
    }
    [STAThread]
    static int Main(string[] args)
    {
        Directory.CreateDirectory(args[0]); string path = Path.Combine(args[0], "UserSettings.fixture.json");
        File.WriteAllText(path, "{\"IsUserSettingsMigrationDone\":\"true\"}");
        AccountInfo account = DesktopBackend.ReadAccount(path);
        Check(!account.Ready && account.Detail.Contains("off") && !account.Detail.Contains("Unsupported"), "Default account is incorrectly unsupported.");
        File.WriteAllText(path, "{\"IsSplitTunnelingEnabled\":\"true\"}");
        account = DesktopBackend.ReadAccount(path);
        Check(!account.Ready && account.Detail.Contains("Exclude mode"), "Missing mode did not use Proton's Standard default.");
        foreach (string json in new string[] {
            "{\"IsSplitTunnelingEnabled\":\"true\",\"SplitTunnelingMode\":\"Inverse\"}",
            "{\"IsSplitTunnelingEnabled\":\"true\",\"SplitTunnelingMode\":\"Inverse\",\"SplitTunnelingInverseAppsList\":null}",
            "{\"IsSplitTunnelingEnabled\":\"true\",\"SplitTunnelingMode\":\"Inverse\",\"SplitTunnelingInverseAppsList\":\"[]\"}" })
        { File.WriteAllText(path, json); Check(DesktopBackend.ReadAccount(path).Ready, "Missing/null/empty saved list blocked valid Include mode."); }
        foreach (string json in new string[] { "{\"unrelated\":\"file\"}", "{\"IsSplitTunnelingEnabled\":true,\"SplitTunnelingMode\":\"Inverse\"}", "{\"IsSplitTunnelingEnabled\":\"true\",\"SplitTunnelingMode\":\"Unknown\"}" })
        { File.WriteAllText(path, json); Check(!DesktopBackend.ReadAccount(path).Ready, "Unsupported settings unlocked refresh."); }
        Console.WriteLine("PASS: Account reader distinguishes omitted defaults, disabled/Exclude mode, missing/null/empty lists, and unsupported encodings.");

        foreach (string name in new string[] { "Discord", "ProtonVPN.Client", "Update" })
        {
            bool exited = false; AppInspection state = new AppInspection(); state.Roots.Add("C:\\Fixture\\Discord");
            DesktopBackend.ObserveProcess(state, name, 1, delegate { return exited; }, delegate { return 1; }, delegate { exited = true; return null; });
            Check(state.AppsExited && state.UnknownCount == 0, "Image-lookup exit race counted an exited process: " + name);
            state = new AppInspection();
            DesktopBackend.ObserveProcess(state, name, 1, delegate { return false; }, delegate { return 1; }, delegate { return null; });
            Check(!state.AppsExited && state.UnknownCount == 1 && state.UnverifiedProcesses.Contains(name), "Unreadable live process was ignored.");
        }
        AppInspection otherSession = new AppInspection();
        DesktopBackend.ObserveProcess(otherSession, "Discord", 1, delegate { return false; }, delegate { return 2; }, delegate { throw new Exception("Other session path must not be read"); });
        Check(otherSession.AppsExited, "Other session blocked refresh.");
        using (Process self = Process.GetCurrentProcess()) Check(String.Equals(DesktopBackend.Image(self), self.MainModule.FileName, StringComparison.OrdinalIgnoreCase), "Limited-rights image query failed.");
        Console.WriteLine("PASS: Exit races are ignored, unreadable live processes remain blocked, other sessions are excluded, and native path lookup works.");

        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        SlowBackend backend = new SlowBackend();
        using (UpdaterForm form = new UpdaterForm(backend))
        using (System.Windows.Forms.Timer steps = new System.Windows.Forms.Timer())
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
            int phase = 0; Exception failure = null; DateTime deadline = DateTime.UtcNow.AddSeconds(6);
            steps.Interval = 20;
            steps.Tick += delegate
            {
                try
                {
                    if (DateTime.UtcNow > deadline) throw new Exception("Shutdown/snapshot fixture timed out at phase " + phase);
                    if (phase == 0 && form.CloseAppsButton.Enabled)
                    {
                        backend.DelayNextScan = true;
                        typeof(UpdaterForm).GetMethod("RequestInspection", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null); phase = 1;
                    }
                    else if (phase == 1 && backend.ScanStarted.WaitOne(0)) { form.CloseAppsButton.PerformClick(); phase = 2; }
                    else if (phase == 2 && form.LastInspection != null && !form.LastInspection.Known && !backend.Running) { backend.ReleaseScan.Set(); phase = 3; }
                    else if (phase == 3 && form.LastInspection != null && form.LastInspection.AppsExited && form.RefreshRulesButton.Enabled)
                    {
                        Check(form.LastInspection.DiscordCount == 0 && !form.CloseAppsButton.Enabled, "Pre-close running snapshot overwrote shutdown.");
                        phase = 4; steps.Stop(); form.Close();
                    }
                }
                catch (Exception error) { failure = error; backend.ReleaseScan.Set(); steps.Stop(); form.Close(); }
            };
            steps.Start();
            try
            {
                Application.Run(form);
                if (failure != null) throw failure;
                Check(phase == 4, "Post-close fresh polls were not verified.");
            }
            finally { backend.ReleaseScan.Set(); }
        }
        Console.WriteLine("PASS: A delayed pre-close inspection cannot overwrite shutdown; fresh post-close polls unlock refresh.");
        return 0;
    }
}
