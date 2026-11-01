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

    enum ThemeMode { System = 0, Light = 1, Dark = 2, EyeComfort = 3 }

    // Colors for the menu and the calculator flyout.
    class Palette
    {
        public Color Back, Input, Text, Muted, Border, Highlight;

        static readonly Palette Light = new Palette
        {
            Back = Color.FromArgb(0xFF, 0xFF, 0xFF), Input = Color.FromArgb(0xFF, 0xFF, 0xFF),
            Text = Color.FromArgb(0x1B, 0x1B, 0x1B), Muted = Color.FromArgb(0x6E, 0x6E, 0x6E),
            Border = Color.FromArgb(0xCC, 0xCC, 0xCC), Highlight = Color.FromArgb(0xE5, 0xE5, 0xE5),
        };
        static readonly Palette Dark = new Palette
        {
            Back = Color.FromArgb(0x20, 0x20, 0x20), Input = Color.FromArgb(0x2D, 0x2D, 0x2D),
            Text = Color.FromArgb(0xF2, 0xF2, 0xF2), Muted = Color.FromArgb(0xA0, 0xA0, 0xA0),
            Border = Color.FromArgb(0x45, 0x45, 0x45), Highlight = Color.FromArgb(0x3A, 0x3A, 0x3A),
        };
        // Warm sepia: no pure white, little blue, softer contrast for long reading.
        static readonly Palette EyeComfort = new Palette
        {
            Back = Color.FromArgb(0xF4, 0xEC, 0xD8), Input = Color.FromArgb(0xFB, 0xF5, 0xE6),
            Text = Color.FromArgb(0x4B, 0x3A, 0x2A), Muted = Color.FromArgb(0x8C, 0x7A, 0x62),
            Border = Color.FromArgb(0xD8, 0xC7, 0xA6), Highlight = Color.FromArgb(0xE8, 0xDB, 0xBE),
        };

        public static Palette For(ThemeMode theme, bool appsLight)
        {
            if (theme == ThemeMode.Light) return Light;
            if (theme == ThemeMode.Dark) return Dark;
            if (theme == ThemeMode.EyeComfort) return EyeComfort;
            return appsLight ? Light : Dark;
        }
    }

    class ThemedColorTable : ProfessionalColorTable
    {
        readonly Palette p;
        public ThemedColorTable(Palette palette) { p = palette; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return p.Back; } }
        public override Color ImageMarginGradientBegin { get { return p.Back; } }
        public override Color ImageMarginGradientMiddle { get { return p.Back; } }
        public override Color ImageMarginGradientEnd { get { return p.Back; } }
        public override Color MenuBorder { get { return p.Border; } }
        public override Color MenuItemBorder { get { return p.Highlight; } }
        public override Color MenuItemSelected { get { return p.Highlight; } }
        public override Color MenuItemSelectedGradientBegin { get { return p.Highlight; } }
        public override Color MenuItemSelectedGradientEnd { get { return p.Highlight; } }
        public override Color MenuItemPressedGradientBegin { get { return p.Highlight; } }
        public override Color MenuItemPressedGradientEnd { get { return p.Highlight; } }
        public override Color SeparatorDark { get { return p.Border; } }
        public override Color SeparatorLight { get { return p.Back; } }
        public override Color CheckBackground { get { return p.Back; } }
        public override Color CheckSelectedBackground { get { return p.Highlight; } }
        public override Color CheckPressedBackground { get { return p.Highlight; } }
    }

    class ThemedRenderer : ToolStripProfessionalRenderer
    {
        readonly Palette p;
        public ThemedRenderer(Palette palette) : base(new ThemedColorTable(palette)) { p = palette; RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = p.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = p.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(p.Back))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        // The stock check mark is a black bitmap, invisible on dark; draw one in the text color.
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(p.Text, Math.Max(1.5f, r.Height / 9f)))
            {
                e.Graphics.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                    new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f),
                });
            }
        }
    }

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
        readonly ToolStripMenuItem[] themeItems;
        readonly ContextMenuStrip menu;
        DateCalculatorForm calculator;
        DisplayMode mode;
        ThemeMode theme;
        Icon currentIcon;
        string shownKey;

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        public WeekNumberContext()
        {
            startupItem = new ToolStripMenuItem("Start with Windows", null, OnToggleStartup);
            startupItem.Checked = IsStartupEnabled();

            mode = (DisplayMode)LoadSetting("DisplayMode", typeof(DisplayMode), (int)DisplayMode.Both);
            theme = (ThemeMode)LoadSetting("Theme", typeof(ThemeMode), (int)ThemeMode.System);
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

            themeItems = new[]
            {
                new ToolStripMenuItem("Same as Windows") { Tag = ThemeMode.System },
                new ToolStripMenuItem("Light") { Tag = ThemeMode.Light },
                new ToolStripMenuItem("Dark") { Tag = ThemeMode.Dark },
                new ToolStripMenuItem("Eye comfort") { Tag = ThemeMode.EyeComfort },
            };
            var themeItem = new ToolStripMenuItem("Theme");
            foreach (ToolStripMenuItem item in themeItems)
            {
                item.Click += OnSelectTheme;
                themeItem.DropDownItems.Add(item);
            }

            menu = new ContextMenuStrip();
            menu.Items.Add(showItem);
            menu.Items.Add(themeItem);
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

            ApplyTheme();
            UpdateIcon(true);
            trayIcon.Visible = true;
        }

        void OnSystemChanged(object sender, EventArgs e)
        {
            ApplyTheme();
            UpdateIcon(true);
        }

        void ApplyTheme()
        {
            foreach (ToolStripMenuItem item in themeItems)
                item.Checked = (ThemeMode)item.Tag == theme;

            Palette palette = Palette.For(theme, IsAppsLight());
            menu.Renderer = new ThemedRenderer(palette);
            foreach (ToolStripItem item in menu.Items)
            {
                var menuItem = item as ToolStripMenuItem;
                if (menuItem == null) continue;
                menuItem.DropDown.Renderer = menu.Renderer;
            }
            if (calculator != null && !calculator.IsDisposed)
                calculator.ApplyTheme(palette);
        }

        void OnSelectTheme(object sender, EventArgs e)
        {
            theme = (ThemeMode)((ToolStripMenuItem)sender).Tag;
            SaveSetting("Theme", (int)theme);
            ApplyTheme();
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
            SaveSetting("DisplayMode", (int)mode);
            UpdateModeChecks();
            UpdateIcon(true);
        }

        void UpdateModeChecks()
        {
            foreach (ToolStripMenuItem item in modeItems)
                item.Checked = (DisplayMode)item.Tag == mode;
        }

        static int LoadSetting(string name, Type enumType, int fallback)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKey))
            {
                object value = key == null ? null : key.GetValue(name);
                if (value is int && Enum.IsDefined(enumType, (int)value))
                    return (int)value;
                return fallback;
            }
        }

        static void SaveSetting(string name, int value)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                key.SetValue(name, value, RegistryValueKind.DWord);
        }

        // Apps can be light while the taskbar is dark, so the menu and flyout use the apps setting.
        static bool IsAppsLight()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
            {
                object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                return !(value is int) || (int)value != 0;
            }
        }

        void ShowCalculator()
        {
            if (calculator == null || calculator.IsDisposed)
            {
                calculator = new DateCalculatorForm();
                calculator.ApplyTheme(Palette.For(theme, IsAppsLight()));
            }
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
        readonly Panel inputFrame;
        readonly Label resultLabel;
        readonly Label hintLabel;
        Palette palette;
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
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(10);

            // Borderless text box in a panel that draws the border, so it can take the theme's color.
            daysInput = new TextBox { BorderStyle = BorderStyle.None, Location = new Point(6, 5), Width = 248 };
            inputFrame = new Panel { Size = new Size(260, daysInput.Height + 10) };
            inputFrame.Controls.Add(daysInput);
            inputFrame.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (palette == null) return;
                using (var pen = new Pen(palette.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, inputFrame.Width - 1, inputFrame.Height - 1);
            };
            resultLabel = new Label
            {
                AutoSize = true,
                Font = new Font(Font.FontFamily, Font.Size * 1.2f, FontStyle.Bold),
                Margin = new Padding(3, 8, 3, 0),
            };
            hintLabel = new Label
            {
                AutoSize = true,
                // Sample date in the user's own short format, e.g. 25.12.2026 or 12/25/2026.
                Text = "-97 = 97 days ago     97 = in 97 days\n" +
                    new DateTime(DateTime.Today.Year, 12, 25).ToString("d", CultureInfo.CurrentCulture) +
                    " = days until or since that date",
                Margin = new Padding(3, 4, 3, 0),
            };

            var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            layout.Controls.Add(inputFrame);
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
            if (palette == null) return;
            using (var pen = new Pen(palette.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        public void ApplyTheme(Palette p)
        {
            palette = p;
            BackColor = p.Back;
            inputFrame.BackColor = p.Input;
            daysInput.BackColor = p.Input;
            daysInput.ForeColor = p.Text;
            resultLabel.ForeColor = p.Text;
            hintLabel.ForeColor = p.Muted;
            inputFrame.Invalidate();
            Invalidate();
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

        // A number is an offset in days from today; a date gives the days until or since it.
        void UpdateResult()
        {
            string text = daysInput.Text.Trim();
            DateTime today = DateTime.Today;
            int days = 0;
            DateTime picked;
            if (text.Length == 0 || int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out days))
            {
                if (Math.Abs(days) > MaxDays)
                {
                    resultLabel.Text = "Enter a whole number of days or a date";
                    return;
                }
                resultLabel.Text = FormatDate(today.AddDays(days));
            }
            else if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out picked))
            {
                int diff = (picked.Date - today).Days;
                string span = Math.Abs(diff) == 1 ? "1 day" : Math.Abs(diff) + " days";
                string relative = diff == 0 ? "Today" : diff > 0 ? "In " + span : span + " ago";
                resultLabel.Text = relative + "\n" + FormatDate(picked.Date);
            }
            else
            {
                resultLabel.Text = "Enter a whole number of days or a date";
            }
        }

        static string FormatDate(DateTime date)
        {
            // Some long date formats (e.g. en-GB) omit the weekday; always show it.
            string pattern = CultureInfo.CurrentCulture.DateTimeFormat.LongDatePattern;
            if (!pattern.Contains("dddd")) pattern = "dddd, " + pattern;
            return date.ToString(pattern, CultureInfo.CurrentCulture) + "  \u00B7  CW " + WeekNumberContext.GetIsoWeek(date);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) resultLabel.Font.Dispose();
            base.Dispose(disposing);
        }
    }
}
