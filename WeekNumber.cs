using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WeekNumber
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            AppUpdate.Updater.WaitForPrevious(args);
            bool createdNew;
            using (var mutex = new Mutex(true, "WeekNumberTrayApp_SingleInstance", out createdNew))
            {
                if (!createdNew) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new WeekNumberContext());
            }
        }
    }

    class WeekNumberContext : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string AppName = "WeekNumber";

        readonly NotifyIcon trayIcon;
        readonly System.Windows.Forms.Timer timer;
        readonly ToolStripMenuItem startupItem;
        readonly AppUpdate.Updater updater = new AppUpdate.Updater("joback94/Calendar_Week_Public", "WeekNumber.exe");
        readonly Control invoker = new Control();
        Icon currentIcon;
        int shownWeek = -1;
        bool shownLight;

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        public WeekNumberContext()
        {
            startupItem = new ToolStripMenuItem("Start with Windows", null, OnToggleStartup);
            startupItem.Checked = IsStartupEnabled();

            var menu = new ContextMenuStrip();
            menu.Items.Add(startupItem);
            menu.Items.Add("Check for updates", null, delegate { updater.CheckNow(true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });

            trayIcon = new NotifyIcon();
            trayIcon.ContextMenuStrip = menu;

            // Poll every 30s so the icon flips shortly after midnight on Monday,
            // and also after sleep/resume.
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 30000;
            timer.Tick += delegate { UpdateIcon(false); };
            timer.Start();

            SystemEvents.UserPreferenceChanged += OnSystemChanged;
            SystemEvents.TimeChanged += OnSystemChanged;
            SystemEvents.DisplaySettingsChanged += OnSystemChanged;

            UpdateIcon(true);
            trayIcon.Visible = true;

            GC.KeepAlive(invoker.Handle);
            updater.Checked += (r, detail, manual) => invoker.BeginInvoke(new Action(() => OnUpdateChecked(r, detail, manual)));
            updater.Start();
        }

        // Nothing here needs saving, so a new version takes over right away.
        void OnUpdateChecked(AppUpdate.UpdateResult r, string detail, bool manual)
        {
            if (r == AppUpdate.UpdateResult.Installed)
            {
                try { updater.Relaunch(null); ExitThread(); }
                catch (Exception) { }
                return;
            }
            if (!manual) return;
            string text = r == AppUpdate.UpdateResult.UpToDate ? "WeekNumber " + AppUpdate.Updater.CurrentVersion + " is the latest version." : detail;
            trayIcon.ShowBalloonTip(5000, "WeekNumber", text, r == AppUpdate.UpdateResult.Failed ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        void OnSystemChanged(object sender, EventArgs e)
        {
            UpdateIcon(true);
        }

        void UpdateIcon(bool force)
        {
            DateTime now = DateTime.Now;
            int week = GetIsoWeek(now);
            bool light = IsTaskbarLight();

            string tip = "Week " + week + " - " + now.ToString("D", CultureInfo.CurrentCulture);
            trayIcon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            if (!force && week == shownWeek && light == shownLight) return;

            Icon old = currentIcon;
            currentIcon = CreateIcon(week, light);
            trayIcon.Icon = currentIcon;
            if (old != null) old.Dispose();

            shownWeek = week;
            shownLight = light;
        }

        // ISO 8601: weeks start on Monday, week 1 contains the first Thursday of the year.
        static int GetIsoWeek(DateTime date)
        {
            Calendar cal = CultureInfo.InvariantCulture.Calendar;
            DayOfWeek day = cal.GetDayOfWeek(date);
            if (day >= DayOfWeek.Monday && day <= DayOfWeek.Wednesday)
                date = date.AddDays(3);
            return cal.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        }

        static bool IsTaskbarLight()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
            {
                object value = key == null ? null : key.GetValue("SystemUsesLightTheme");
                return value is int && (int)value == 1;
            }
        }

        Icon CreateIcon(int week, bool light)
        {
            Size size = SystemInformation.SmallIconSize;
            using (var bmp = new Bitmap(size.Width, size.Height))
            using (Graphics g = Graphics.FromImage(bmp))
            using (var path = new GraphicsPath())
            using (var family = new FontFamily("Segoe UI"))
            using (var brush = new SolidBrush(light ? Color.Black : Color.White))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Build the glyphs as a path, then scale it to fill the icon with a small margin.
                path.AddString(week.ToString(), family, (int)FontStyle.Bold, 100f, PointF.Empty, StringFormat.GenericTypographic);
                RectangleF bounds = path.GetBounds();
                float margin = Math.Max(1f, size.Width / 16f);
                float scale = Math.Min((size.Width - 2 * margin) / bounds.Width, (size.Height - 2 * margin) / bounds.Height);

                using (var m = new Matrix())
                {
                    m.Translate(size.Width / 2f, size.Height / 2f);
                    m.Scale(scale, scale);
                    m.Translate(-(bounds.X + bounds.Width / 2f), -(bounds.Y + bounds.Height / 2f));
                    path.Transform(m);
                }
                g.FillPath(brush, path);

                IntPtr handle = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(handle))
                        return (Icon)temp.Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        static bool IsStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(AppName) != null;
        }

        void OnToggleStartup(object sender, EventArgs e)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (startupItem.Checked)
                    key.DeleteValue(AppName, false);
                else
                    key.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
            }
            startupItem.Checked = IsStartupEnabled();
        }

        protected override void ExitThreadCore()
        {
            SystemEvents.UserPreferenceChanged -= OnSystemChanged;
            SystemEvents.TimeChanged -= OnSystemChanged;
            SystemEvents.DisplaySettingsChanged -= OnSystemChanged;
            timer.Stop();
            timer.Dispose();
            invoker.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            if (currentIcon != null) currentIcon.Dispose();
            base.ExitThreadCore();
        }
    }
}
