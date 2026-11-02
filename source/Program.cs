using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace ExperimentUI
{
    internal static class Program
    {
        public const string Version = "1.4.0";
        [STAThread]
        private static int Main(string[] args)
        {
            Native.EnableDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e) {
                MessageBox.Show(e.Exception.Message, "Experiment UI", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            bool smoke = Array.IndexOf(args, "--smoke") >= 0;
            bool participantMode = false;
#if PARTICIPANT_BUILD
            participantMode = true;
#endif
            if (Array.IndexOf(args, "--participant") >= 0) participantMode = true;
            if (Array.IndexOf(args, "--debug") >= 0) participantMode = false;
            bool isPractice = false;
#if TEST_BUILD
            isPractice = true;
#endif
            try {
                using (SetupForm setup = new SetupForm(smoke, participantMode, isPractice)) Application.Run(setup);
            } catch (ConfigurationErrorsException ex) {
                MessageBox.Show(participantMode ? "設定ファイルを読み込めません。実験担当者にお知らせください。" : ex.Message, "設定ファイルの確認", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            return SmokeDriver.ExitCode;
        }
    }

    internal static class Native
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string device, int mode, ref DevMode data);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string deviceName;
            public short specVersion, driverVersion, size, driverExtra;
            public int fields, positionX, positionY, orientation, fixedOutput;
            public short color, duplex, yResolution, ttOption, collate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string formName;
            public short logPixels;
            public int bitsPerPixel, width, height, displayFlags, frequency, icmMethod, icmIntent, mediaType, ditherType, reserved1, reserved2, panningWidth, panningHeight;
        }
        public static void EnableDpi()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (EntryPointNotFoundException) { }
            SetProcessDPIAware();
        }
        public static int Dpi(Control control)
        {
            try { uint dpi = GetDpiForWindow(control.Handle); if (dpi > 0) return (int)dpi; } catch (EntryPointNotFoundException) { }
            using (Graphics g = control.CreateGraphics()) return (int)Math.Round(g.DpiX);
        }
        public static bool Present()
        {
            try { return DwmFlush() == 0; } catch (DllNotFoundException) { return false; }
        }
        public static void BeginTiming() { timeBeginPeriod(1); }
        public static void EndTiming() { timeEndPeriod(1); }
        public static object Display(Screen screen, Control control)
        {
            DevMode mode = new DevMode();
            mode.size = (short)Marshal.SizeOf(typeof(DevMode));
            bool found = EnumDisplaySettings(screen.DeviceName, -1, ref mode);
            return new {
                device = screen.DeviceName, primary = screen.Primary,
                width_px = screen.Bounds.Width, height_px = screen.Bounds.Height,
                left_px = screen.Bounds.Left, top_px = screen.Bounds.Top,
                current_mode_width_px = found ? mode.width : screen.Bounds.Width,
                current_mode_height_px = found ? mode.height : screen.Bounds.Height,
                refresh_hz = found ? mode.frequency : 0,
                dpi = Dpi(control), windows_scale_percent = Dpi(control) / 96.0 * 100.0,
                aspect_ratio = (double)screen.Bounds.Width / screen.Bounds.Height,
                is_16_9 = Math.Abs((double)screen.Bounds.Width / screen.Bounds.Height - 16.0 / 9.0) < .01,
                recorded_utc = DateTime.UtcNow.ToString("o"),
                operating_system = Environment.OSVersion.ToString()
            };
        }
    }

    internal static class StimulusLayout
    {
        public static Rectangle Viewport(Size size)
        {
            int width = Math.Min(size.Width, (int)Math.Floor(size.Height * 16.0 / 9.0));
            int height = (int)Math.Floor(width * 9.0 / 16.0);
            return new Rectangle((size.Width - width) / 2, (size.Height - height) / 2, width, height);
        }
        public static Rectangle[] Images(Size size)
        {
            Rectangle view = Viewport(size);
            int side = Math.Max(1, (int)Math.Floor(view.Width * .3125));
            int gap = (int)Math.Floor(view.Width * .018);
            int left = view.Left + (view.Width - side * 3 - gap * 2) / 2;
            Rectangle[] result = new Rectangle[3];
            for (int i = 0; i < 3; i++) result[i] = new Rectangle(left + i * (side + gap), view.Top + (view.Height - side) / 2, side, side);
            return result;
        }
        public static object Describe(Size size)
        {
            List<object> images = new List<object>();
            foreach (Rectangle r in Images(size)) images.Add(new { x = r.X, y = r.Y, width = r.Width, height = r.Height });
            Rectangle v = Viewport(size);
            return new { canvas_width_px = size.Width, canvas_height_px = size.Height,
                viewport = new { x = v.X, y = v.Y, width = v.Width, height = v.Height },
                images = images, reference = "CVPR2027 UI案.pptx; centered 3 squares on white; side=31.25% viewport width" };
        }
    }

    internal static class AppPaths
    {
        public static string ResolveFolder(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("フォルダのパスを入力してください。");
            string path = value.Trim();
            // Drive-relative and root-relative paths depend on the process's current
            // directory/drive. Require a complete absolute path or an ordinary relative one.
            bool drive = path.Length >= 2 && path[1] == ':';
            bool completeDrive = drive && path.Length >= 3 && (path[2] == '\\' || path[2] == '/');
            bool unc = path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);
            if ((drive && !completeDrive) || (Path.IsPathRooted(path) && !completeDrive && !unc))
                throw new ArgumentException("相対パス（例：.\\experiment_data）または完全な絶対パスを入力してください。");
            return Path.GetFullPath(completeDrive || unc ? path : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path));
        }

        public static string ForDisplay(string absolutePath)
        {
            string full = Path.GetFullPath(absolutePath);
            string root = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\', '/');
            if (String.Equals(full.TrimEnd('\\', '/'), root, StringComparison.OrdinalIgnoreCase)) return ".";
            string prefix = root + Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return ".\\" + full.Substring(prefix.Length);
            return full;
        }
    }

    internal static class ExperimentSettings
    {
        public static string ReadFolder(string key, string defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (value == null) value = defaultValue;
            if (String.IsNullOrWhiteSpace(value)) throw new ConfigurationErrorsException(key + " のパスが空欄です。");
            try { AppPaths.ResolveFolder(value); }
            catch (ArgumentException ex) { throw new ConfigurationErrorsException(key + " のパスが無効です。", ex); }
            catch (NotSupportedException ex) { throw new ConfigurationErrorsException(key + " のパスが無効です。", ex); }
            catch (PathTooLongException ex) { throw new ConfigurationErrorsException(key + " のパスが長すぎます。", ex); }
            return value.Trim();
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly TextBox dataPath = new TextBox(), outputPath = new TextBox(), participant = new TextBox();
        private readonly Label status = new Label(), displayLabel = new Label();
        private readonly Button start = new Button(), inspect = new Button();
        private bool busy;
        private readonly bool smoke;
        private readonly bool participantMode;
        private readonly bool isPractice;
        private readonly string baseFolder = AppDomain.CurrentDomain.BaseDirectory;
        public SetupForm(bool runSmoke) : this(runSmoke, false) { }
        public SetupForm(bool runSmoke, bool useParticipantMode) : this(runSmoke, useParticipantMode, false) { }
        public SetupForm(bool runSmoke, bool useParticipantMode, bool practice)
        {
            smoke = runSmoke;
            participantMode = useParticipantMode;
            isPractice = practice;
            Text = isPractice ? "Experiment UI — Test" : participantMode ? "Experiment UI" : "Experiment UI — Debug";
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Yu Gothic UI", 10F);
            dataPath.Text = ExperimentSettings.ReadFolder("DataFolder", @".\experiment_data");
            outputPath.Text = ExperimentSettings.ReadFolder("ResultsFolder", isPractice ? @".\results_test" : @".\results");
            StartPosition = FormStartPosition.CenterScreen;
            if (participantMode) { BuildParticipantLayout(); WireEvents(); return; }
            BackColor = Color.FromArgb(246, 248, 250);
            ClientSize = new Size(920, 610);
            MinimumSize = new Size(790, 620);
            StartPosition = FormStartPosition.CenterScreen;
            TableLayoutPanel outer = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(36, 24, 36, 24), ColumnCount = 1, RowCount = 10 };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 9; i++) outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.Controls.Add(new Label { Text = "3枚の画像から選択", AutoSize = true, Font = new Font(Font.FontFamily, 22F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) });
            outer.Controls.Add(new Label { Text = "A：左  /  B：中央  /  C：右　　画像をマウスでクリックしてください。", AutoSize = true, Margin = new Padding(0, 0, 0, 18) });
            participant.Width = 290;
            participant.MaxLength = 80;
            outer.Controls.Add(Field("参加者 ID（任意）", participant, null));
            Button browseData = new Button { Text = "参照…", AutoSize = true };
            browseData.Click += delegate { ChooseFolder(dataPath); };
            Button browseOutput = new Button { Text = "参照…", AutoSize = true };
            browseOutput.Click += delegate { ChooseFolder(outputPath); };
            outer.Controls.Add(Field("実験データフォルダ（相対パス可）", dataPath, browseData));
            outer.Controls.Add(Field("結果の保存先（相対パス可）", outputPath, browseOutput));
            displayLabel.AutoSize = true;
            displayLabel.Margin = new Padding(0, 12, 0, 8);
            outer.Controls.Add(displayLabel);
            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 4) };
            inspect.Text = "データを確認"; inspect.AutoSize = true; inspect.Height = 38;
            inspect.Click += async delegate { await ValidateAndStart(false); };
            buttons.Controls.Add(inspect); outer.Controls.Add(buttons);
            status.AutoSize = true; status.MaximumSize = new Size(820, 0); status.ForeColor = Color.FromArgb(65, 80, 95);
            status.Text = "相対パスは実行ファイルのあるフォルダが基準です。\r\nファイル名：{試行番号}_{A,B,C}_{Name}.png（番号順に提示）";
            status.Margin = new Padding(0, 8, 0, 14); outer.Controls.Add(status);
            start.Text = "開始"; start.Size = new Size(190, 52); start.Font = new Font(Font.FontFamily, 16F, FontStyle.Bold);
            start.BackColor = Color.FromArgb(24, 93, 120); start.ForeColor = Color.White; start.FlatStyle = FlatStyle.Flat;
            start.Click += async delegate { await ValidateAndStart(true); };
            outer.Controls.Add(start);
            outer.Controls.Add(new Label { Text = "開始後はこのウィンドウのあるディスプレイを全画面表示します。\r\n選択枠 500ms → 注視点 ＋ 1000ms → 次の試行。Esc で中断・終了できます。", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 14, 0, 0) });
            Controls.Add(outer);
            WireEvents();
        }
        private void BuildParticipantLayout()
        {
            BackColor = Color.White;
            ClientSize = new Size(640, 360);
            MinimumSize = new Size(540, 330);
            TableLayoutPanel panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(32), ColumnCount = 1, RowCount = 4 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            Panel idPanel = new Panel { Size = new Size(330, 80), Anchor = AnchorStyles.None, TabIndex = 0 };
            Label idLabel = new Label { Text = "参加者ID", AutoSize = true, Location = new Point(0, 0), Font = new Font(Font.FontFamily, 12F) };
            participant.Location = new Point(0, 30); participant.Size = new Size(330, 32);
            participant.Font = new Font(Font.FontFamily, 13F); participant.MaxLength = 80; participant.TabIndex = 0;
            idPanel.Controls.Add(idLabel); idPanel.Controls.Add(participant);
            start.Text = "開始"; start.Size = new Size(240, 60); start.Anchor = AnchorStyles.None;
            start.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold);
            start.TabIndex = 1;
            start.BackColor = Color.FromArgb(24, 93, 120); start.ForeColor = Color.White; start.FlatStyle = FlatStyle.Flat;
            start.Click += async delegate { await ValidateAndStart(true); };
            panel.Controls.Add(idPanel, 0, 1); panel.Controls.Add(start, 0, 2);
            Controls.Add(panel);
        }
        private void WireEvents()
        {
            LocationChanged += delegate { if (IsHandleCreated) UpdateDisplay(); };
            Shown += async delegate {
                UpdateDisplay();
                if (smoke) {
                    participant.Text = "AUTOMATED_TEST";
                    outputPath.Text = @".\test_artifacts\smoke_results";
                    SavePreview(Path.Combine(baseFolder, "test_artifacts", participantMode ? "setup_participant.png" : "setup_debug.png"));
                    await ValidateAndStart(true);
                }
            };
        }
        private static Control Field(string label, TextBox text, Button browse)
        {
            TableLayoutPanel row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 10) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 0, 0, 4) }, 0, 0);
            row.SetColumnSpan(row.GetControlFromPosition(0, 0), 2);
            text.Dock = DockStyle.Fill; text.Margin = new Padding(0, 0, 8, 0); row.Controls.Add(text, 0, 1);
            if (browse != null) row.Controls.Add(browse, 1, 1);
            return row;
        }
        private void ChooseFolder(TextBox target)
        {
            if (busy) return;
            string initial = baseFolder;
            try { string resolved = AppPaths.ResolveFolder(target.Text); if (Directory.Exists(resolved)) initial = resolved; } catch (ArgumentException) { } catch (NotSupportedException) { }
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { SelectedPath = initial, Description = "フォルダを選択してください" })
                if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = AppPaths.ForDisplay(dialog.SelectedPath);
        }
        private void UpdateDisplay()
        {
            Screen screen = Screen.FromControl(this);
            if (participantMode) return;
            displayLabel.Text = String.Format("表示先：{0}    {1} × {2} px    Windows 拡大率 {3:0}%", screen.DeviceName, screen.Bounds.Width, screen.Bounds.Height, Native.Dpi(this) / 96.0 * 100);
        }
        protected override void WndProc(ref Message m)
        {
            int message = m.Msg;
            base.WndProc(ref m);
            if ((message == 0x007E || message == 0x02E0) && IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(UpdateDisplay));
        }
        private void SetBusy(bool value)
        {
            busy = value; start.Enabled = inspect.Enabled = !value;
            if (participantMode) start.Text = value ? "準備中…" : "開始";
            dataPath.ReadOnly = outputPath.ReadOnly = participant.ReadOnly = value;
        }
        private async Task ValidateAndStart(bool run)
        {
            if (busy) return;
            SetBusy(true); status.Text = "画像・ファイル名を確認しています…";
            try {
                UpdateDisplay();
                string folder = AppPaths.ResolveFolder(dataPath.Text);
                Dataset dataset = await Task.Run(delegate { return Dataset.Load(folder); });
                status.Text = dataset.Trials.Count + " 試行 / " + dataset.Trials.Count * 3 + " 枚を確認しました。";
                if (dataset.Warnings.Count > 0) status.Text += "\r\n" + String.Join("\r\n", dataset.Warnings.GetRange(0, Math.Min(3, dataset.Warnings.Count)).ToArray());
                if (!run) return;
                // The complete warning list is retained in session.json.
                Screen screen = Screen.FromControl(this);
                using (ExperimentForm experiment = new ExperimentForm(dataset, participant.Text, AppPaths.ResolveFolder(outputPath.Text), screen, smoke, participantMode, isPractice)) {
                    Hide();
                    experiment.ShowDialog();
                    if (smoke) { Close(); return; }
                    Show(); Activate();
                    status.Text = "保存先：" + AppPaths.ForDisplay(experiment.ResultFolder);
                }
            } catch (Exception ex) {
                if (smoke) { SmokeDriver.ExitCode = 1; Directory.CreateDirectory(Path.Combine(baseFolder, "test_artifacts")); File.WriteAllText(Path.Combine(baseFolder, "test_artifacts", "smoke_error.txt"), ex.ToString()); Close(); }
                else { Show(); ShowError(ex); }
            } finally { if (!IsDisposed) SetBusy(false); }
        }
        private void ShowError(Exception ex) { status.Text = ex.Message; MessageBox.Show(this, participantMode ? "操作を完了できませんでした。実験担当者にお知らせください。" : ex.Message, "確認が必要です", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        private void SavePreview(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)); using (Bitmap image = new Bitmap(Width, Height)) { DrawToBitmap(image, new Rectangle(Point.Empty, Size)); image.Save(path, ImageFormat.Png); } }
    }

    internal enum Phase { Preparing, Fixation, Stimulus, Feedback, Break, Paused, Completed, Error }

    internal sealed class ExperimentForm : Form
    {
        private readonly Dataset dataset;
        private readonly Screen screen;
        private readonly SessionStore store;
        private readonly Timer timer = new Timer { Interval = 5 };
        private readonly Rectangle[] imageRects;
        private readonly bool smoke;
        private readonly bool isPractice;
        private readonly int breakEvery;
        private readonly Button breakResume = new Button();
        private Bitmap[] images;
        private Task<Bitmap[]> prefetch;
        private Phase phase = Phase.Preparing, onScreen = Phase.Preparing, pausedFrom;
        private bool pendingPaint = true, finished, closing, logFailed, accepted, nextAfterPause, timingActive;
        private string errorMessage = "", pauseReason = "", phaseReason = "";
        private int trialIndex, attempt = 1, selectedIndex = -1, onScreenTrial, onScreenAttempt;
        private long shownTicks, stimulusTicks, trialResponseTicks;
        private DateTime shownUtc, stimulusUtc;
        internal int CompletedCount;
        internal readonly List<double> FixationDurations = new List<double>(), FeedbackDurations = new List<double>();
        internal int InterruptedCount;
        internal int BreakCount, BreakResumeCount;
        internal readonly List<int> BreaksAfter = new List<int>();
        internal int BreakInterval { get { return breakEvery; } }
        internal int TestBreakAfter { get { return BreakSchedule.Midpoint(dataset.Trials.Count); } }
        internal bool IsPractice { get { return isPractice; } }
        internal int TotalTrials { get { return dataset.Trials.Count; } }
        internal int TrialIndex { get { return trialIndex; } }
        internal Phase CurrentPhase { get { return phase; } }
        internal double Elapsed { get { return shownTicks == 0 ? 0 : Milliseconds(Stopwatch.GetTimestamp() - shownTicks); } }
        public string ResultFolder { get { return store.Folder; } }
        internal Rectangle[] ImageRectangles { get { return imageRects; } }
        public ExperimentForm(Dataset data, string participant, string outputRoot, Screen target, bool runSmoke, bool participantMode = false, bool practice = false)
        {
            dataset = data; screen = target; smoke = runSmoke;
            isPractice = practice; breakEvery = BreakSchedule.ProductionInterval;
            Text = "Experiment UI"; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; Bounds = target.Bounds; BackColor = Color.White; KeyPreview = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            imageRects = StimulusLayout.Images(target.Bounds.Size);
            store = new SessionStore(data, participant, outputRoot, Native.Display(target, this), StimulusLayout.Describe(target.Bounds.Size), smoke, participantMode ? "participant" : "debug", isPractice, breakEvery);
            breakResume.Text = "再開"; breakResume.Visible = false; breakResume.TabStop = true;
            breakResume.Size = new Size(Math.Max(260, target.Bounds.Width / 7), Math.Max(64, target.Bounds.Height / 15));
            breakResume.Location = new Point((target.Bounds.Width - breakResume.Width) / 2, (int)(target.Bounds.Height * .65));
            breakResume.Font = new Font("Yu Gothic UI", Math.Max(18F, target.Bounds.Height / 60F), FontStyle.Bold);
            breakResume.BackColor = Color.FromArgb(24, 93, 120); breakResume.ForeColor = Color.White; breakResume.FlatStyle = FlatStyle.Flat;
            breakResume.Click += delegate { ResumeBreak(); };
            Controls.Add(breakResume);
            timer.Tick += delegate { TickState(); };
            Shown += delegate {
                Native.BeginTiming(); timingActive = true;
                Activate(); Refresh();
                prefetch = LoadImages(0); timer.Start();
                if (smoke) new SmokeDriver(this).Start();
            };
            Deactivate += delegate { if (!closing && !finished && phase != Phase.Preparing && phase != Phase.Error) Pause("focus_lost"); };
            FormClosing += OnClosing;
        }
        private static double Milliseconds(long ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }
        private Trial CurrentTrial { get { return dataset.Trials[trialIndex]; } }
        private Task<Bitmap[]> LoadImages(int index)
        {
            Trial trial = dataset.Trials[index]; int side = imageRects[0].Width;
            return Task.Run(delegate {
                Bitmap[] result = new Bitmap[3];
                try {
                    for (int i = 0; i < 3; i++) {
                        byte[] bytes = File.ReadAllBytes(trial.Images[i].FullPath);
                        string hash;
                        using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                        if (hash != trial.Images[i].Sha256) throw new InvalidDataException("検証後に画像が変更されました：" + trial.Images[i].FileName);
                        using (MemoryStream stream = new MemoryStream(bytes))
                        using (Image original = Image.FromStream(stream, false, true)) {
                            Bitmap resized = new Bitmap(side, side, PixelFormat.Format24bppRgb);
                            result[i] = resized;
                            using (Graphics g = Graphics.FromImage(resized))
                            using (ImageAttributes attributes = new ImageAttributes()) {
                                g.Clear(Color.White); g.CompositingQuality = CompositingQuality.HighQuality;
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                attributes.SetWrapMode(WrapMode.TileFlipXY);
                                g.DrawImage(original, new Rectangle(0, 0, side, side), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                            }
                        }
                    }
                    return result;
                } catch { DisposeImages(result); throw; }
            });
        }
        private static void DisposeImages(Bitmap[] values) { if (values != null) foreach (Bitmap value in values) if (value != null) value.Dispose(); }
        private void UsePreparedImages()
        {
            if (prefetch.IsFaulted) { Fail(prefetch.Exception.GetBaseException()); return; }
            if (prefetch.IsCanceled) { Fail(new IOException("画像の準備が中止されました。")); return; }
            DisposeImages(images); images = prefetch.Result; prefetch = null;
            selectedIndex = -1; accepted = false;
            if (trialIndex + 1 < dataset.Trials.Count) prefetch = LoadImages(trialIndex + 1);
            SetPhase(Phase.Fixation, "before_trial");
        }
        private void TickState()
        {
            try {
                if (finished || phase == Phase.Error) return;
                if (phase != Phase.Paused && (Bounds != screen.Bounds || Screen.FromControl(this).DeviceName != screen.DeviceName)) { Fail(new InvalidOperationException("実験中にディスプレイ設定が変わりました。現在のセッションを終了して再度開始してください。")); return; }
                if (phase == Phase.Preparing && prefetch != null && prefetch.IsCompleted) {
                    if (Form.ActiveForm == this || smoke) UsePreparedImages();
                    return;
                }
                if (pendingPaint || shownTicks == 0) return;
                if (phase == Phase.Fixation && Elapsed >= 1000) { selectedIndex = -1; SetPhase(Phase.Stimulus, "await_response"); }
                else if (phase == Phase.Feedback && Elapsed >= 500) Advance();
            } catch (Exception ex) { Fail(ex); }
        }
        private void SetPhase(Phase value, string reason)
        {
            phase = value; phaseReason = reason; pendingPaint = true;
            breakResume.Visible = value == Phase.Break;
            breakResume.Enabled = value == Phase.Break;
            Cursor = value == Phase.Stimulus ? Cursors.Default : Cursors.Arrow;
            Refresh();
        }
        protected override void WndProc(ref Message m)
        {
            if ((m.Msg == 0x007E || m.Msg == 0x02E0) && shownTicks > 0 && !finished && !closing && phase != Phase.Error) {
                base.WndProc(ref m);
                Fail(new InvalidOperationException("実験中に解像度または表示倍率が変わりました。現在のセッションを終了して再度開始してください。"));
                return;
            }
            bool painting = m.Msg == 0x000F;
            base.WndProc(ref m);
            // Double-buffered WM_PAINT has returned; synchronise with DWM before
            // timestamping. This is a software presentation estimate, not photon onset.
            if (painting && pendingPaint && IsHandleCreated && Visible && !closing) {
                bool dwmSynchronized = Native.Present();
                long now = Stopwatch.GetTimestamp(); DateTime utc = DateTime.UtcNow;
                pendingPaint = false;
                try {
                    if (!dwmSynchronized) store.Event("dwm_sync_unavailable", new { method = "WM_PAINT_completion_fallback", trial_number = CurrentTrial.Number, phase = phase.ToString() });
                    if (shownTicks > 0) {
                        store.Phase(onScreen.ToString(), onScreenTrial, onScreenAttempt, shownTicks, now, shownUtc, phaseReason);
                        if (onScreen == Phase.Fixation && phase == Phase.Stimulus) FixationDurations.Add(Milliseconds(now - shownTicks));
                        if (onScreen == Phase.Feedback && phase != Phase.Paused) FeedbackDurations.Add(Milliseconds(now - shownTicks));
                    }
                    shownTicks = now; shownUtc = utc; onScreen = phase; onScreenTrial = CurrentTrial.Number; onScreenAttempt = attempt;
                    if (phase == Phase.Stimulus) { stimulusTicks = now; stimulusUtc = utc; }
                } catch (Exception ex) { Fail(ex); }
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.Clear(Color.White);
            if (phase == Phase.Stimulus || phase == Phase.Feedback) {
                if (images != null) for (int i = 0; i < 3; i++) g.DrawImageUnscaled(images[i], imageRects[i].Location);
                if (phase == Phase.Feedback && selectedIndex >= 0) {
                    Rectangle r = imageRects[selectedIndex]; int line = Math.Max(5, r.Width / 75);
                    // Frame is just outside the image, so original stimulus pixels stay intact.
                    r.Inflate(line / 2 + 2, line / 2 + 2);
                    using (Pen pen = new Pen(Color.FromArgb(0, 126, 167), line)) g.DrawRectangle(pen, r);
                }
            } else if (phase == Phase.Fixation) {
                int half = Math.Max(12, ClientSize.Height / 60);
                int x = ClientSize.Width / 2, y = ClientSize.Height / 2;
                using (Pen pen = new Pen(Color.FromArgb(28, 35, 40), Math.Max(2, ClientSize.Height / 360))) {
                    g.DrawLine(pen, x - half, y, x + half, y); g.DrawLine(pen, x, y - half, x, y + half);
                }
            } else if (phase == Phase.Preparing) DrawMessage(g, "画像を準備しています…", "", Color.DimGray);
            else if (phase == Phase.Break) DrawBreak(g);
            else if (phase == Phase.Paused) DrawMessage(g, "一時停止", "Enter または中央をクリックして再開\nEsc で終了", Color.FromArgb(35, 55, 65));
            else if (phase == Phase.Completed) DrawMessage(g, "終了しました", "ご協力ありがとうございました。\nEnter または中央をクリックして閉じる", Color.FromArgb(24, 93, 120));
            else if (phase == Phase.Error) DrawMessage(g, "実験を停止しました", errorMessage + "\n\nEnter または中央をクリックして閉じる", Color.Firebrick);
        }
        private void DrawBreak(Graphics g)
        {
            string message = isPractice
                ? "本番では100試行に一度、この画面が表示されます。\n休憩していただき、再度始める準備が整ったら進めてください。"
                : "休憩してください。\n再度始める準備が整ったら、再開ボタンを押してください。";
            using (Font heading = new Font("Yu Gothic UI", Math.Max(26F, ClientSize.Height / 30F), FontStyle.Bold))
            using (Font body = new Font("Yu Gothic UI", Math.Max(16F, ClientSize.Height / 60F)))
            using (Brush brush = new SolidBrush(Color.FromArgb(35, 55, 65)))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {
                g.DrawString("休憩", heading, brush, new RectangleF(40, ClientSize.Height * .25F, ClientSize.Width - 80, ClientSize.Height * .14F), format);
                g.DrawString(message, body, brush, new RectangleF(ClientSize.Width * .06F, ClientSize.Height * .40F, ClientSize.Width * .88F, ClientSize.Height * .20F), format);
            }
        }
        private void DrawMessage(Graphics g, string title, string detail, Color color)
        {
            using (Font heading = new Font("Yu Gothic UI", Math.Max(22, ClientSize.Height / 30F), FontStyle.Bold))
            using (Font body = new Font("Yu Gothic UI", Math.Max(14, ClientSize.Height / 65F)))
            using (Brush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {
                g.DrawString(title, heading, brush, new RectangleF(40, ClientSize.Height * .32F, ClientSize.Width - 80, ClientSize.Height * .17F), format);
                g.DrawString(detail, body, brush, new RectangleF(60, ClientSize.Height * .49F, ClientSize.Width - 120, ClientSize.Height * .3F), format);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            long response = Stopwatch.GetTimestamp(); base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (phase == Phase.Paused) { if (CentralButton.Contains(e.Location)) Resume(); return; }
            if (phase == Phase.Completed || phase == Phase.Error) { if (CentralButton.Contains(e.Location)) Close(); return; }
            Choose(e.Location, response);
        }
        private Rectangle CentralButton { get { return new Rectangle(ClientSize.Width / 4, ClientSize.Height / 3, ClientSize.Width / 2, ClientSize.Height / 2); } }
        internal void TestChoose(Point point) { Choose(point, Stopwatch.GetTimestamp()); }
        private void Choose(Point point, long response)
        {
            if (phase != Phase.Stimulus || pendingPaint || accepted || stimulusTicks == 0) return;
            if (Form.ActiveForm != this && !smoke) return;
            if (Bounds != screen.Bounds) { Fail(new InvalidOperationException("表示サイズが変わりました。")); return; }
            int index = -1; for (int i = 0; i < 3; i++) if (imageRects[i].Contains(point)) index = i;
            if (index < 0) return;
            accepted = true; selectedIndex = index; trialResponseTicks = response;
            try {
                SetPhase(Phase.Feedback, "selection");
                store.Response(CurrentTrial, trialIndex + 1, attempt, index, stimulusTicks, stimulusUtc, response, point.X, point.Y, "selected", "");
                CompletedCount++;
            } catch (Exception ex) { Fail(ex); }
        }
        private void Advance()
        {
            if (trialIndex + 1 >= dataset.Trials.Count) {
                SetPhase(Phase.Completed, "all_trials_answered");
                store.Finish("completed", CompletedCount); finished = true; timer.Stop(); return;
            }
            bool breakDue = isPractice ? BreakSchedule.IsMidpointDue(CompletedCount, dataset.Trials.Count)
                : BreakSchedule.IsDue(CompletedCount, dataset.Trials.Count, breakEvery);
            if (breakDue) {
                BreakCount++; BreaksAfter.Add(CompletedCount);
                store.Event("break_started", new { completed_trials = CompletedCount, after_trial_number = CurrentTrial.Number, next_trial_number = dataset.Trials[trialIndex + 1].Number, schedule = isPractice ? "once_at_midpoint" : "every_n_completed_trials", every_completed_trials = isPractice ? (int?)null : breakEvery, after_completed_trials = isPractice ? (int?)TestBreakAfter : null });
                SetPhase(Phase.Break, "scheduled_break");
                return;
            }
            AdvanceToNextTrial();
        }
        private void AdvanceToNextTrial()
        {
            trialIndex++; attempt = 1; accepted = false; selectedIndex = -1;
            if (prefetch != null && prefetch.IsCompleted) UsePreparedImages();
            else SetPhase(Phase.Preparing, "waiting_for_preloaded_images");
        }
        private void ResumeBreak()
        {
            if (phase != Phase.Break || pendingPaint || finished || closing) return;
            try {
                if (Bounds != screen.Bounds || Screen.FromControl(this).DeviceName != screen.DeviceName) { Fail(new InvalidOperationException("ディスプレイ設定が変わりました。再度開始してください。")); return; }
                breakResume.Enabled = false;
                store.Event("break_resumed", new { completed_trials = CompletedCount, next_trial_number = dataset.Trials[trialIndex + 1].Number, rest_ms = Milliseconds(Stopwatch.GetTimestamp() - shownTicks) });
                BreakResumeCount++;
                AdvanceToNextTrial();
            } catch (Exception ex) { Fail(ex); }
        }
        internal void TestResumeBreak() { breakResume.PerformClick(); }
        internal void Pause(string reason)
        {
            if (finished || phase == Phase.Paused || phase == Phase.Error || phase == Phase.Preparing || phase == Phase.Break) return;
            try {
                pausedFrom = phase; pauseReason = reason; nextAfterPause = phase == Phase.Feedback;
                if (phase == Phase.Stimulus && !accepted && !pendingPaint && stimulusTicks > 0) {
                    store.Response(CurrentTrial, trialIndex + 1, attempt, -1, stimulusTicks, stimulusUtc, Stopwatch.GetTimestamp(), -1, -1, "interrupted", reason);
                    InterruptedCount++;
                    attempt++;
                }
                store.Event("paused", new { reason = reason, trial_number = CurrentTrial.Number, previous_phase = pausedFrom.ToString() });
                SetPhase(Phase.Paused, reason);
            } catch (Exception ex) { Fail(ex); }
        }
        internal void Resume()
        {
            if (phase != Phase.Paused) return;
            try {
                if (Bounds != screen.Bounds || Screen.FromControl(this).DeviceName != screen.DeviceName) { Fail(new InvalidOperationException("ディスプレイ設定が変わりました。再度開始してください。")); return; }
                store.Event("resumed", new { reason = pauseReason, trial_number = CurrentTrial.Number });
                if (nextAfterPause) {
                    // A response was already saved. Re-show its frame for a fresh 500ms
                    // before moving on; never repeat or double-count that response.
                    nextAfterPause = false; SetPhase(Phase.Feedback, "feedback_after_pause");
                } else { selectedIndex = -1; accepted = false; SetPhase(Phase.Fixation, "restart_after_pause"); }
            } catch (Exception ex) { Fail(ex); }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter && phase == Phase.Break) { e.Handled = e.SuppressKeyPress = true; ResumeBreak(); }
            else if (e.KeyCode == Keys.Enter && phase == Phase.Paused) { e.Handled = true; Resume(); }
            else if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape) && (phase == Phase.Completed || phase == Phase.Error)) Close();
            else if (e.KeyCode == Keys.Escape) {
                e.Handled = true; Pause("escape");
                DialogResult answer = MessageBox.Show(this, "実験を終了しますか？\nここまでの記録は保存されます。", "実験の終了", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer == DialogResult.Yes) { closing = true; Close(); }
                else if (phase == Phase.Paused) Resume();
            }
        }
        private void Fail(Exception ex)
        {
            if (phase == Phase.Error) return;
            errorMessage = ex.Message; timer.Stop(); logFailed = true;
            try { store.Event("error", new { message = ex.ToString() }); store.Finish("error", CompletedCount); } catch { }
            phase = Phase.Error; pendingPaint = false; breakResume.Visible = false; Invalidate();
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!finished && !closing && !smoke && phase != Phase.Error) {
                Pause("window_close");
                if (MessageBox.Show(this, "実験を終了しますか？\nここまでの記録は保存されます。", "実験の終了", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) { e.Cancel = true; Resume(); return; }
            }
            closing = true; timer.Stop();
            try {
                long now = Stopwatch.GetTimestamp();
                if (shownTicks > 0 && !finished && !logFailed) store.Phase(onScreen.ToString(), onScreenTrial, onScreenAttempt, shownTicks, now, shownUtc, "window_closed");
                if (!finished && !logFailed) store.Finish("aborted", CompletedCount);
            } catch (Exception ex) { if (!smoke) MessageBox.Show(ex.Message, "記録エラー"); }
            if (timingActive) { Native.EndTiming(); timingActive = false; }
            store.Dispose(); DisposeImages(images); images = null;
            Task<Bitmap[]> outstanding = prefetch; prefetch = null;
            if (outstanding != null) outstanding.ContinueWith(delegate(Task<Bitmap[]> t) { if (t.Status == TaskStatus.RanToCompletion) DisposeImages(t.Result); else if (t.IsFaulted) { var ignored = t.Exception; } });
        }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); if (timingActive) { Native.EndTiming(); timingActive = false; } } base.Dispose(disposing); }
        internal void SavePreview(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (Bitmap bitmap = new Bitmap(ClientSize.Width, ClientSize.Height)) { DrawToBitmap(bitmap, ClientRectangle); bitmap.Save(path, ImageFormat.Png); }
        }
    }

    internal sealed class SmokeDriver
    {
        internal static int ExitCode;
        private readonly ExperimentForm form;
        private readonly Timer timer = new Timer { Interval = 25 };
        private readonly Stopwatch timeout = Stopwatch.StartNew();
        private bool interrupted, capturedStimulus, capturedFeedback, capturedFixation, capturedBreak;
        private int clickCount;
        private readonly string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_artifacts");
        public SmokeDriver(ExperimentForm target) { form = target; }
        public void Start()
        {
            timer.Tick += delegate { Step(); }; timer.Start();
            form.FormClosed += delegate { timer.Stop(); timer.Dispose(); };
        }
        private void Step()
        {
            try {
                if (timeout.Elapsed.TotalSeconds > 60) throw new Exception("Smoke test timed out: " + form.CurrentPhase);
                if (form.CurrentPhase == Phase.Error) throw new Exception("GUI entered Error state.");
                if (form.CurrentPhase == Phase.Fixation && form.Elapsed > 100 && !capturedFixation) { form.SavePreview(Path.Combine(folder, "fixation.png")); capturedFixation = true; }
                if (form.CurrentPhase == Phase.Stimulus && form.Elapsed > 220) {
                    if (!interrupted) { interrupted = true; form.Pause("automated_focus_loss"); return; }
                    if (!capturedStimulus) { form.SavePreview(Path.Combine(folder, "stimulus.png")); capturedStimulus = true; }
                    // Click outside first (ignored), then select, then duplicate click (ignored).
                    form.TestChoose(new Point(1, 1));
                    Rectangle r = form.ImageRectangles[form.TrialIndex % 3];
                    form.TestChoose(new Point(r.X + r.Width / 2, r.Y + r.Height / 2));
                    form.TestChoose(new Point(r.X + r.Width / 2, r.Y + r.Height / 2));
                    clickCount++;
                }
                if (form.CurrentPhase == Phase.Paused && form.Elapsed > 150) form.Resume();
                if (form.CurrentPhase == Phase.Feedback && form.Elapsed > 100 && !capturedFeedback) { form.SavePreview(Path.Combine(folder, "feedback.png")); capturedFeedback = true; }
                if (form.CurrentPhase == Phase.Break) {
                    int completed = form.CompletedCount;
                    // Focus loss during an untimed break must keep the same break screen.
                    form.Pause("automated_focus_loss_during_break");
                    form.TestChoose(new Point(1, 1));
                    if (form.CurrentPhase != Phase.Break || form.CompletedCount != completed) throw new Exception("Break changed without a resume action.");
                    if (!capturedBreak && form.Elapsed > 100) { form.SavePreview(Path.Combine(folder, form.IsPractice ? "break_test.png" : "break_production.png")); capturedBreak = true; }
                    if (form.Elapsed > 1100) { form.TestResumeBreak(); form.TestResumeBreak(); }
                }
                if (form.CurrentPhase == Phase.Completed) {
                    int count = form.TotalTrials;
                    if (form.CompletedCount != count || clickCount != count || form.InterruptedCount != 1) throw new Exception("Response gating/count mismatch.");
                    if (form.FixationDurations.Count != count + 1 || form.FeedbackDurations.Count != count) throw new Exception("Timing phase count mismatch.");
                    int expectedBreaks = form.IsPractice ? (count >= 2 ? 1 : 0) : (count - 1) / form.BreakInterval;
                    if (form.BreakCount != expectedBreaks || form.BreakResumeCount != expectedBreaks) throw new Exception("Break/resume count mismatch.");
                    for (int i = 0; i < expectedBreaks; i++) if (form.BreaksAfter[i] != (form.IsPractice ? form.TestBreakAfter : (i + 1) * form.BreakInterval)) throw new Exception("Break scheduled at wrong count.");
                    foreach (double ms in form.FixationDurations) if (ms < 999 || ms > 1250) throw new Exception("Fixation timing out of bounds: " + ms);
                    foreach (double ms in form.FeedbackDurations) if (ms < 499 || ms > 750) throw new Exception("Feedback timing out of bounds: " + ms);
                    form.SavePreview(Path.Combine(folder, "completed.png"));
                    File.WriteAllText(Path.Combine(folder, "smoke_result.json"), new JavaScriptSerializer().Serialize(new { passed = true, completed_trials = form.CompletedCount, interrupted_attempts = form.InterruptedCount, experiment_mode = form.IsPractice ? "test" : "production", break_interval = form.IsPractice ? (int?)null : form.BreakInterval, break_after_completed_trials = form.IsPractice && count >= 2 ? (int?)form.TestBreakAfter : null, breaks_after_completed_trials = form.BreaksAfter, breaks_resumed = form.BreakResumeCount, fixation_ms = form.FixationDurations, feedback_ms = form.FeedbackDurations, result_folder = form.ResultFolder, elapsed_seconds = timeout.Elapsed.TotalSeconds }));
                    timer.Stop(); form.Close();
                }
            } catch (Exception ex) {
                ExitCode = 1; Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "smoke_error.txt"), ex.ToString()); timer.Stop(); form.Close();
            }
        }
    }
}
