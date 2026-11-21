using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DiscordProtonUpdater
{
    // Reuse the existing window and first-launch setup with a different worker.
    public sealed class PrototypeBackend : IUpdaterBackend
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
        public JobResult Execute(OperationRequest request, Action<string> log)
        {
            string action;
            switch (request.Action)
            {
                case "Close": action = "Disconnect"; break;
                case "Refresh": action = "Refresh"; break;
                case "LaunchProton": action = "Reconnect"; break;
                case "LaunchDiscord": action = "LaunchDiscord"; break;
                default: throw new InvalidOperationException("Unknown prototype action.");
            }
            OperationRequest command = new OperationRequest
            {
                Action = action, SettingsPath = request.SettingsPath,
                Roots = request.Roots, DiscordPath = request.DiscordPath
            };
            string run = Path.Combine(workflow, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(run);
            string input = Path.Combine(run, "request.json");
            File.WriteAllText(input, DesktopBackend.Json().Serialize(command), new UTF8Encoding(false));
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
                    return new JobResult { Message = "Automation timed out. Observe Proton before retrying. Its connection state is uncertain." };
                }
                worker.WaitForExit();
                string output = Path.Combine(run, "result.json");
                if (!File.Exists(output)) return new JobResult { Message = "Prototype worker failed. Check Activity and retry after inspecting Proton." };
                JobResult result = DesktopBackend.Json().Deserialize<JobResult>(File.ReadAllText(output));
                result.Action = request.Action;
                return result;
            }
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
                Application.Run(new UpdaterForm(new PrototypeBackend(folder), setupResult));
            }
            catch (Exception error) { MessageBox.Show(error.Message, AppIdentity.Name + " prototype", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
