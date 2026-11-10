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
        public volatile bool Running = true, DelayNextScan, ProtonRunning, Refreshed, DiscordTargetCorrect;
        public volatile int ProtonLaunches, DiscordLaunches;
        public AppInspection Inspect()
        {
            bool captured = Running;
            if (DelayNextScan) { DelayNextScan = false; ScanStarted.Set(); if (!ReleaseScan.WaitOne(5000)) throw new Exception("Fixture scan timed out."); }
            AppInspection state = new AppInspection { DiscordCount = captured || DiscordLaunches > 0 ? 1 : 0, ProtonCount = ProtonRunning ? 1 : 0, ScanReady = true, ScanDetail = "Fixture scan" };
            state.Roots.Add("C:\\Fixture\\Discord");
            state.Accounts.Add(new AccountInfo { Path = "C:\\Fixture\\UserSettings.fixture.json", Display = "Example account", Ready = true,
                NeedsSplitTunnelingActivation = !Refreshed, Detail = Refreshed ? "Include mode is enabled." : "Split tunneling is off. Refresh will enable Include mode." });
            return state;
        }
        public JobResult Execute(OperationRequest request, Action<string> log)
        {
            if (request.Action == "Close") Running = false;
            else if (request.Action == "Refresh") Refreshed = true;
            else if (request.Action == "LaunchProton") { ProtonRunning = true; ProtonLaunches++; }
            else if (request.Action == "LaunchDiscord")
            {
                DiscordTargetCorrect = ProtonRunning && request.DiscordPath == @"C:\Fixture\Discord\Discord.exe" &&
                    request.Roots != null && request.Roots.Count == 1 && request.Roots[0] == @"C:\Fixture\Discord";
                DiscordLaunches++;
            }
            return new JobResult { Success = true, Action = request.Action, Message = "Fixture " + request.Action + " succeeded.", DiscordPath = @"C:\Fixture\Discord\Discord.exe" };
        }
        public void AddRoot(string path) { }
        public void AddAccount(string path) { }
    }
    [STAThread]
    static int Main(string[] args)
    {
        Directory.CreateDirectory(args[0]); string path = Path.Combine(args[0], "UserSettings.fixture.json");
        File.WriteAllText(path, "{\"IsUserSettingsMigrationDone\":\"true\"}");
        AccountInfo account = DesktopBackend.ReadAccount(path);
        Check(account.Ready && account.NeedsSplitTunnelingActivation && account.Detail.Contains("Refresh will enable"), "Default account cannot use automatic activation.");
        File.WriteAllText(path, "{\"IsSplitTunnelingEnabled\":\"true\"}");
        account = DesktopBackend.ReadAccount(path);
        Check(account.Ready && account.NeedsSplitTunnelingActivation && account.Detail.Contains("Exclude mode"), "Exclude mode cannot use automatic activation.");
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
            int phase = 0; Exception failure = null; DateTime deadline = DateTime.UtcNow.AddSeconds(40);
            Stopwatch sinceClick = new Stopwatch();
            Button proton = (Button)form.Controls.Find("LaunchProtonButton", true)[0];
            Button discord = (Button)form.Controls.Find("LaunchDiscordButton", true)[0];
            Check(form.Controls.Find("ExitButton", true).Length == 0, "The fifth-step Exit button is still present.");
            Label warning = (Label)typeof(UpdaterForm).GetField("warning", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            steps.Interval = 20;
            steps.Tick += delegate
            {
                try
                {
                    if (DateTime.UtcNow > deadline) throw new Exception("Shutdown/snapshot fixture timed out at phase " + phase);
                    if (phase == 0 && form.CloseAppsButton.Enabled)
                    {
                        Check(!proton.Enabled && !discord.Enabled, "A launch was available before refresh.");
                        backend.DelayNextScan = true;
                        typeof(UpdaterForm).GetMethod("RequestInspection", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null); phase = 1;
                    }
                    else if (phase == 1 && backend.ScanStarted.WaitOne(0)) { form.CloseAppsButton.PerformClick(); phase = 2; }
                    else if (phase == 2 && form.LastInspection != null && !form.LastInspection.Known && !backend.Running) { backend.ReleaseScan.Set(); phase = 3; }
                    else if (phase == 3 && form.LastInspection.AppsExited && form.RefreshRulesButton.Enabled)
                    {
                        Check(!form.CloseAppsButton.Enabled, "Pre-close running snapshot overwrote shutdown.");
                        backend.Running = true;
                        phase = 4;
                    }
                    else if (phase == 4 && form.LastInspection.DiscordCount == 1)
                    {
                        Check(!form.CloseAppsButton.Enabled && !form.RefreshRulesButton.Enabled &&
                            warning.Text.StartsWith("An app reopened."), "A reopened app reactivated the completed close step or did not block refresh.");
                        backend.Running = false;
                        phase = 5;
                    }
                    else if (phase == 5 && form.LastInspection.AppsExited && form.RefreshRulesButton.Enabled)
                    {
                        Check(form.LastInspection.DiscordCount == 0 && !form.CloseAppsButton.Enabled, "Pre-close running snapshot overwrote shutdown.");
                        using (Bitmap preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size)); preview.Save(Path.Combine(args[0], "automatic-activation.png")); }
                        sinceClick.Restart(); form.RefreshRulesButton.PerformClick();
                        Check(!form.RefreshRulesButton.Enabled && form.RefreshRulesButton.BackColor == form.CloseAppsButton.BackColor,
                            "Refresh did not lock and match Close both apps immediately after the click.");
                        phase = 6;
                    }
                    else if (phase == 6 && warning.Text.StartsWith("Please wait, rules are being applied... Next step unlocks in "))
                    {
                        Check(proton.Text == "3. Launch Proton VPN" && !proton.Enabled && !discord.Enabled, "Rules countdown changed the button label or unlocked a launch step.");
                        Check(warning.BackColor == Color.FromArgb(225, 243, 234), "Rules countdown was missing the green status background.");
                        using (Bitmap preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size)); preview.Save(Path.Combine(args[0], "proton-countdown.png")); }
                        phase = 7;
                    }
                    else if (phase == 7 && proton.Enabled)
                    {
                        Check(sinceClick.Elapsed.TotalSeconds >= 10 && !discord.Enabled && warning.Text == "Click Launch Proton VPN.", "Proton unlocked early or left the countdown visible.");
                        Check(form.LastInspection.AppsExited && !form.RefreshRulesButton.Enabled && form.RefreshRulesButton.BackColor == form.CloseAppsButton.BackColor,
                            "Refresh reactivated or changed color when Launch Proton VPN unlocked.");
                        using (Bitmap preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size)); preview.Save(Path.Combine(args[0], "proton-ready.png")); }
                        sinceClick.Restart(); phase = 8;
                    }
                    else if (phase == 8 && sinceClick.ElapsedMilliseconds >= 300)
                    {
                        Check(backend.ProtonLaunches == 0, "Proton opened without a click.");
                        sinceClick.Restart(); proton.PerformClick(); phase = 9;
                    }
                    else if (phase == 9 && warning.Text.StartsWith("Please wait, launching ProtonVPN... Next step unlocks in "))
                    {
                        Check(discord.Text == "4. Launch Discord" && !discord.Enabled, "Discord countdown changed the button label or unlocked a launch step.");
                        Check(warning.BackColor == Color.FromArgb(225, 243, 234), "Discord countdown was missing the green status background.");
                        using (Bitmap preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size)); preview.Save(Path.Combine(args[0], "discord-countdown.png")); }
                        phase = 10;
                    }
                    else if (phase == 10 && discord.Enabled && form.LastInspection.ProtonCount == 1)
                    {
                        Check(sinceClick.Elapsed.TotalSeconds >= 10 && backend.ProtonLaunches == 1 && !proton.Enabled &&
                            warning.Text == "Check that Proton VPN is connected, then click Launch Discord.", "Discord unlocked early or left the countdown visible.");
                        Color disabled = Color.FromArgb(222, 227, 236);
                        Check(!form.CloseAppsButton.Enabled && form.CloseAppsButton.BackColor == disabled &&
                            !form.RefreshRulesButton.Enabled && form.RefreshRulesButton.BackColor == disabled &&
                            proton.BackColor == disabled, "A completed step was active or did not match the greyed-out buttons.");
                        using (Bitmap preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size)); preview.Save(Path.Combine(args[0], "discord-ready.png")); }
                        sinceClick.Restart(); phase = 11;
                    }
                    else if (phase == 11 && sinceClick.ElapsedMilliseconds >= 300)
                    {
                        Check(backend.DiscordLaunches == 0, "Discord opened without a click.");
                        sinceClick.Restart(); discord.PerformClick(); phase = 12;
                    }
                    else if (phase == 12 && warning.Text == "Process completed" && form.LastInspection.DiscordCount == 1)
                    {
                        Check(backend.DiscordLaunches == 1 && backend.DiscordTargetCorrect && !discord.Enabled &&
                            !form.CloseAppsButton.Enabled && !form.RefreshRulesButton.Enabled, "Discord launch or completion used the wrong state.");
                        Label detail = (Label)typeof(UpdaterForm).GetField("accountDetail", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                        Check(detail.Text == "Include mode is enabled.", "Completion displayed stale account settings.");
                        using (Bitmap preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size)); preview.Save(Path.Combine(args[0], "completed.png")); }
                        phase = 13;
                    }
                }
                catch (Exception error) { failure = error; backend.ReleaseScan.Set(); steps.Stop(); form.Close(); }
            };
            steps.Start();
            try
            {
                Application.Run(form);
                if (failure != null) throw failure;
                Check(phase == 13 && sinceClick.ElapsedMilliseconds >= 2400, "Completion did not display before the automatic exit.");
            }
            finally { backend.ReleaseScan.Set(); }
        }
        Console.WriteLine("PASS: A delayed pre-close inspection cannot overwrite shutdown; fresh post-close polls unlock refresh.");
        Console.WriteLine("PASS: Completed steps stay greyed out after Proton starts, then the completion message appears before automatic exit.");
        return 0;
    }
}
