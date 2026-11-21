using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DiscordProtonUpdater;

class PrototypeFormTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    class Backend : IPrototypeBackend
    {
        public volatile bool Discord = true, Proton = true;
        public readonly List<string> Actions = new List<string>();
        public AppInspection Inspect()
        {
            AppInspection state = new AppInspection { ScanReady = true, DiscordCount = Discord ? 1 : 0, ProtonCount = Proton ? 1 : 0, ScanDetail = "Fixture data" };
            state.Roots.Add(@"C:\Fixture\Discord");
            state.Accounts.Add(new AccountInfo { Path = @"C:\Fixture\UserSettings.json", Display = "Example account", Ready = true });
            return state;
        }
        public PrototypeResult Execute(PrototypeRequest request, Action<string> log)
        {
            Actions.Add(request.Action);
            if (request.Action == "Disconnect") { Check(request.AccountConfirmed, "Account not confirmed."); Discord = false; }
            else if (request.Action == "Refresh") { Check(request.AllowGracefulRestart, "Restart not confirmed."); Proton = false; }
            else if (request.Action == "Reconnect") { Check(request.DefaultConnectionConfirmed, "Connection not confirmed."); Proton = true; }
            else { Check(request.RoutingConfirmed, "Routing not confirmed."); Discord = true; }
            return new PrototypeResult { Success = true, Message = request.Action + " fixture success" };
        }
        public void AddAccount(string path) { }
        public void AddRoot(string path) { }
    }
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Backend backend = new Backend();
        using (PrototypeForm form = new PrototypeForm(backend))
        using (Timer timer = new Timer { Interval = 30 })
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
            Button[] buttons = new Button[4];
            for (int i = 0; i < 4; i++) buttons[i] = (Button)form.Controls.Find("PrototypeStep" + (i+1), true)[0];
            string[] checks = { "AccountConfirmed", "RestartConfirmed", "ConnectionConfirmed", "RoutingConfirmed" };
            int stage = 0; bool checkedChoice = false; Exception failure = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            timer.Tick += delegate
            {
                try
                {
                    if (DateTime.UtcNow > deadline) throw new Exception("Prototype fixture timed out.");
                    if (stage == 4) return;
                    Check(form.Controls.Find(checks[stage], true).Length == 1, "Missing choice.");
                    CheckBox choice = (CheckBox)form.Controls.Find(checks[stage], true)[0];
                    if (!choice.Enabled) return;
                    if (!checkedChoice)
                    {
                        Check(!buttons[stage].Enabled, "Step bypassed its explicit choice.");
                        choice.Checked = true; checkedChoice = true;
                    }
                    if (!buttons[stage].Enabled) return;
                    for (int i = 0; i < stage; i++) Check(!buttons[i].Enabled, "Previous step reactivated.");
                    if (stage == 2)
                        using (Bitmap preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size)); preview.Save(args[0]); }
                    buttons[stage].PerformClick(); stage++; checkedChoice = false;
                }
                catch (Exception error) { failure = error; timer.Stop(); form.Close(); }
            };
            timer.Start(); Application.Run(form);
            if (failure != null) throw failure;
            Check(stage == 4 && backend.Actions.Count == 4, "Prototype did not complete all four steps.");
        }
        Console.WriteLine("PASS: Prototype requires each explicit choice, locks completed steps, renders, and closes after completion.");
        return 0;
    }
}
