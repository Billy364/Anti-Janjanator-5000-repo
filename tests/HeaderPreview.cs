using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DiscordProtonUpdater;

class PreviewBackend : IUpdaterBackend
{
    public AppInspection Inspect()
    {
        AppInspection state = new AppInspection { ProtonCount = 1, DiscordCount = 3, ScanReady = true, ExecutableCount = 9, ScanDetail = "9 executables found in 1 Discord installation(s)." };
        state.Roots.Add("C:\\Preview\\Discord");
        state.Accounts.Add(new AccountInfo { Path = "C:\\Preview\\UserSettings.preview.json", Display = "UserSettings.preview.json | Include mode ready", Ready = true, Detail = "Include mode is enabled. This account is ready." });
        return state;
    }
    public JobResult Execute(OperationRequest request, Action<string> log) { throw new Exception("Preview must not perform app operations."); }
    public void AddRoot(string path) { }
    public void AddAccount(string path) { }
}
class HeaderPreview
{
    static void CreateControls(Control control)
    { control.CreateControl(); foreach (Control child in control.Controls) CreateControls(child); control.PerformLayout(); }
    static Label FindHeading(Control control)
    {
        Label label = control as Label;
        if (label != null && label.Text == "Democracy Enforcer 5000") return label;
        foreach (Control child in control.Controls) { Label found = FindHeading(child); if (found != null) return found; }
        return null;
    }
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using (UpdaterForm form = new UpdaterForm(new PreviewBackend()))
        {
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000); form.Opacity = 0;
            form.Show(); Application.DoEvents(); System.Threading.Thread.Sleep(150); Application.DoEvents();
            CreateControls(form); form.PerformLayout();
            PictureBox art = (PictureBox)form.Controls.Find("HeaderArtwork", true)[0];
            if (art.Image == null || art.SizeMode != PictureBoxSizeMode.Zoom) throw new Exception("Missing proportional header image.");
            if (form.Icon == null) throw new Exception("Missing form icon.");
            FieldInfo field = typeof(UpdaterForm).GetField("trayIcon", BindingFlags.NonPublic | BindingFlags.Instance);
            NotifyIcon tray = (NotifyIcon)field.GetValue(form);
            if (tray == null || tray.Icon == null || !tray.Visible || tray.ContextMenuStrip.Items.Count != 3) throw new Exception("Tray icon/menu incomplete.");
            if (form.Text != "Democracy Enforcer 5000" || tray.Text != form.Text || tray.ContextMenuStrip.Items[0].Text != "Open " + form.Text) throw new Exception("App name mismatch.");
            Label heading = FindHeading(form);
            if (heading == null || TextRenderer.MeasureText(heading.Text, heading.Font).Width > heading.ClientSize.Width) throw new Exception("Header name missing or clipped.");
            using (Bitmap preview = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size)); preview.Save(args[0], System.Drawing.Imaging.ImageFormat.Png); }
            Console.WriteLine("PASS: New window/header/tray name, header fit, embedded artwork, app/tray icons, and render.");
        }
        return 0;
    }
}
