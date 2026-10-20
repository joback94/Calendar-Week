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
        static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "WeekNumberTrayApp_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("WeekNumber is already running.\n\nTo replace it with this copy, right-click the week number " +
                        "in the taskbar notification area, choose Exit, then start this copy again.",
                        "WeekNumber", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new WeekNumberContext());
            }
        }
    }

    enum DisplayMode { Both = 0, Week = 1, Date = 2 }

    class WeekNumberContext : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string SettingsKey = @"Software\WeekNumber";
        const string AppName = "WeekNumber";

        readonly NotifyIcon trayIcon;
        readonly System.Windows.Forms.Timer timer;
        readonly ToolStripMenuItem startupItem;
        readonly ToolStripMenuItem[] modeItems;
        DateCalculatorForm calculator;
        DisplayMode mode;
        Icon currentIcon;
        string shownKey;

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        public WeekNumberContext()
        {
            startupItem = new ToolStripMenuItem("Start with Windows", null, OnToggleStartup);
            startupItem.Checked = IsStartupEnabled();

            mode = LoadDisplayMode();
            modeItems = new[]
            {
                new ToolStripMenuItem("Calendar week and date") { Tag = DisplayMode.Both },
                new ToolStripMenuItem("Calendar week only") { Tag = DisplayMode.Week },
                new ToolStripMenuItem("Date only") { Tag = DisplayMode.Date },
            };
            var showItem = new ToolStripMenuItem("Show in tray icon");
            foreach (ToolStripMenuItem item in modeItems)
            {
                item.Click += OnSelectMode;
                showItem.DropDownItems.Add(item);
            }
            UpdateModeChecks();

            var menu = new ContextMenuStrip();
            menu.Items.Add(showItem);
            menu.Items.Add("Date calculator...", null, delegate { ShowCalculator(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });

            trayIcon = new NotifyIcon();
            trayIcon.ContextMenuStrip = menu;
            trayIcon.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowCalculator();
            };

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

            string tip = "CW " + week + " - " + now.ToString("D", CultureInfo.CurrentCulture);
            trayIcon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            string[] lines;
            if (mode == DisplayMode.Week) lines = new[] { week.ToString() };
            else if (mode == DisplayMode.Date) lines = new[] { now.Day.ToString() };
            else lines = new[] { week.ToString(), now.Day.ToString() };

            string key = string.Join("|", lines) + "|" + light;
            if (!force && key == shownKey) return;

            Icon old = currentIcon;
            currentIcon = CreateIcon(lines, light);
            trayIcon.Icon = currentIcon;
            if (old != null) old.Dispose();

            shownKey = key;
        }

        void OnSelectMode(object sender, EventArgs e)
        {
            mode = (DisplayMode)((ToolStripMenuItem)sender).Tag;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                key.SetValue("DisplayMode", (int)mode, RegistryValueKind.DWord);
            UpdateModeChecks();
            UpdateIcon(true);
        }

        void UpdateModeChecks()
        {
            foreach (ToolStripMenuItem item in modeItems)
                item.Checked = (DisplayMode)item.Tag == mode;
        }

        static DisplayMode LoadDisplayMode()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKey))
            {
                object value = key == null ? null : key.GetValue("DisplayMode");
                if (value is int && Enum.IsDefined(typeof(DisplayMode), (int)value))
                    return (DisplayMode)(int)value;
                return DisplayMode.Both;
            }
        }

        void ShowCalculator()
        {
            if (calculator == null || calculator.IsDisposed)
                calculator = new DateCalculatorForm();
            calculator.Toggle(Cursor.Position);
        }

        // ISO 8601: weeks start on Monday, week 1 contains the first Thursday of the year.
        internal static int GetIsoWeek(DateTime date)
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

        // One line fills the icon; two lines (week above day) are stacked and scaled together.
        Icon CreateIcon(string[] lines, bool light)
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
                using (var format = new StringFormat(StringFormat.GenericTypographic))
                {
                    format.Alignment = StringAlignment.Center;
                    for (int i = 0; i < lines.Length; i++)
                        path.AddString(lines[i], family, (int)FontStyle.Bold, 100f,
                            new RectangleF(-500f, i * 90f, 1000f, 200f), format);
                }
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
            if (calculator != null) calculator.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            if (currentIcon != null) currentIcon.Dispose();
            base.ExitThreadCore();
        }
    }
    // Small flyout next to the tray icon: type -97 (97 days ago) or 97 (in 97 days) and read the full date.
    class DateCalculatorForm : Form
    {
        const int MaxDays = 100000;
        const int CS_DROPSHADOW = 0x20000;

        readonly TextBox daysInput;
        readonly Label resultLabel;
        DateTime hiddenAt = DateTime.MinValue;

        public DateCalculatorForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = SystemColors.Window;
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(10);

            daysInput = new TextBox { Width = 260, BorderStyle = BorderStyle.FixedSingle };
            resultLabel = new Label
            {
                AutoSize = true,
                Font = new Font(Font.FontFamily, Font.Size * 1.2f, FontStyle.Bold),
                Margin = new Padding(3, 8, 3, 0),
            };
            var hintLabel = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Text = "-97 = 97 days ago     97 = in 97 days",
                Margin = new Padding(3, 4, 3, 0),
            };

            var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            layout.Controls.Add(daysInput);
            layout.Controls.Add(resultLabel);
            layout.Controls.Add(hintLabel);
            Controls.Add(layout);

            daysInput.TextChanged += delegate { UpdateResult(); };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) HideFlyout();
            };
            Deactivate += delegate { HideFlyout(); };
            UpdateResult();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.DrawRectangle(SystemPens.ControlDark, 0, 0, Width - 1, Height - 1);
        }

        // Shows the flyout above the point (the tray icon), or hides it if it is open.
        public void Toggle(Point anchor)
        {
            // Clicking the icon while the flyout is open deactivates (hides) it first; don't reopen on that same click.
            if (Visible || (DateTime.UtcNow - hiddenAt).TotalMilliseconds < 300)
            {
                HideFlyout();
                return;
            }

            UpdateResult();
            PerformLayout();
            Rectangle area = Screen.FromPoint(anchor).WorkingArea;
            int x = Math.Max(area.Left, Math.Min(anchor.X - Width / 2, area.Right - Width));
            int y = Math.Max(area.Top, Math.Min(anchor.Y - Height - 12, area.Bottom - Height));
            Location = new Point(x, y);

            Show();
            Activate();
            daysInput.Focus();
            daysInput.SelectAll();
        }

        void HideFlyout()
        {
            if (!Visible) return;
            hiddenAt = DateTime.UtcNow;
            Hide();
        }

        void UpdateResult()
        {
            string text = daysInput.Text.Trim();
            int days = 0;
            if (text.Length > 0 && (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out days)
                || Math.Abs(days) > MaxDays))
            {
                resultLabel.Text = "Enter a whole number of days";
                return;
            }
            DateTime date = DateTime.Today.AddDays(days);
            // Some long date formats (e.g. en-GB) omit the weekday; always show it.
            string pattern = CultureInfo.CurrentCulture.DateTimeFormat.LongDatePattern;
            if (!pattern.Contains("dddd")) pattern = "dddd, " + pattern;
            resultLabel.Text = date.ToString(pattern, CultureInfo.CurrentCulture) + "  \u00B7  CW " + WeekNumberContext.GetIsoWeek(date);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) resultLabel.Font.Dispose();
            base.Dispose(disposing);
        }
    }
}
