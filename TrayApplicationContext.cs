using Microsoft.Win32;

namespace ScreenshotApp;

public sealed class TrayApplicationContext : ApplicationContext
{
    private const string AppName = "ScreenshotApp";
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // キャプチャ画像はアスペクト比を維持したままこの倍率でリサイズする(60%)
    private const double ResizeScale = 0.6;

    // オーバーレイを閉じてから実際に画面をキャプチャするまでの待機時間(ms)。
    // 短すぎるとオーバーレイの色が画面に残った状態でキャプチャされてしまう。
    private const int PreCaptureDelayMs = 150;

    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyManager _hotkeyManager;
    private readonly ToolStripMenuItem _autoStartMenuItem;
    private readonly ToolStripMenuItem _copyOnlyMenuItem;

    private readonly int _captureHotkeyId;
    private readonly int _repeatHotkeyId;

    private bool _copyToClipboardOnly; // true: クリップボードのみ / false: 保存もする
    private Rectangle? _lastCaptureRect; // Ctrl+1 での再撮影用に、直前の選択範囲を保持

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();

        var captureItem = new ToolStripMenuItem("範囲選択して撮影 (Ctrl+Shift+S)");
        captureItem.Click += (_, _) => StartCapture();
        menu.Items.Add(captureItem);

        var repeatItem = new ToolStripMenuItem("前回と同じ範囲を再撮影 (Ctrl+1)");
        repeatItem.Click += async (_, _) => await RepeatLastCaptureAsync();
        menu.Items.Add(repeatItem);

        menu.Items.Add(new ToolStripSeparator());

        var openFolderItem = new ToolStripMenuItem("保存フォルダを開く");
        openFolderItem.Click += (_, _) =>
        {
            string dir = ScreenCapture.GetSaveDirectory();
            System.Diagnostics.Process.Start("explorer.exe", dir);
        };
        menu.Items.Add(openFolderItem);

        _copyOnlyMenuItem = new ToolStripMenuItem("クリップボードにのみコピー（ファイル保存しない）")
        {
            CheckOnClick = true
        };
        _copyOnlyMenuItem.CheckedChanged += (_, _) => _copyToClipboardOnly = _copyOnlyMenuItem.Checked;
        menu.Items.Add(_copyOnlyMenuItem);

        _autoStartMenuItem = new ToolStripMenuItem("Windows起動時に自動起動する")
        {
            CheckOnClick = true,
            Checked = IsAutoStartEnabled()
        };
        _autoStartMenuItem.CheckedChanged += (_, _) => SetAutoStart(_autoStartMenuItem.Checked);
        menu.Items.Add(_autoStartMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("終了");
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Visible = true,
            Text = "ScreenshotApp - Ctrl+Shift+S で範囲選択キャプチャ",
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => StartCapture();

        _hotkeyManager = new HotkeyManager();

        _captureHotkeyId = _hotkeyManager.Register(
            HotkeyManager.Modifiers.Control | HotkeyManager.Modifiers.Shift,
            Keys.S);

        _repeatHotkeyId = _hotkeyManager.Register(
            HotkeyManager.Modifiers.Control,
            Keys.D1);

        _hotkeyManager.HotkeyPressed += async id =>
        {
            if (id == _captureHotkeyId)
            {
                StartCapture();
            }
            else if (id == _repeatHotkeyId)
            {
                await RepeatLastCaptureAsync();
            }
        };

        if (_captureHotkeyId == -1 || _repeatHotkeyId == -1)
        {
            _trayIcon.ShowBalloonTip(
                4000,
                "ScreenshotApp",
                "ホットキーの登録に一部失敗しました(Ctrl+Shift+S または Ctrl+1)。他のアプリと競合している可能性があります。トレイアイコンのメニューから手動でキャプチャできます。",
                ToolTipIcon.Warning);
        }
        else
        {
            _trayIcon.ShowBalloonTip(
                2500,
                "ScreenshotApp",
                "常駐を開始しました。Ctrl+Shift+S で範囲選択キャプチャ、Ctrl+1 で前回と同じ範囲を再撮影できます。",
                ToolTipIcon.Info);
        }
    }

    private bool _isCapturing;

    private void StartCapture()
    {
        // 選択中の多重起動を防止
        if (_isCapturing) return;
        _isCapturing = true;

        var overlay = new OverlayForm();
        overlay.SelectionCompleted += async rect =>
        {
            _isCapturing = false;
            if (rect is not Rectangle r) return; // キャンセル

            _lastCaptureRect = r; // Ctrl+1 での再撮影用に記憶

            // オーバーレイ(半透明の灰色)は OverlayForm 側で既に Hide 済みだが、
            // 画面が実際に再描画されるまで少し待ってからキャプチャする。
            // これをしないと、灰色のオーバーレイが写り込んだ状態でキャプチャされてしまう。
            await Task.Delay(PreCaptureDelayMs);

            await CaptureAndNotifyAsync(r);
        };
        overlay.Show();
        overlay.Activate();
    }

    /// <summary>
    /// 直前にキャプチャした範囲と同じ範囲を、選択操作なしで再キャプチャする(Ctrl+1)。
    /// </summary>
    private async Task RepeatLastCaptureAsync()
    {
        if (_lastCaptureRect is not Rectangle r)
        {
            _trayIcon.ShowBalloonTip(
                2500,
                "ScreenshotApp",
                "まだキャプチャ範囲が記録されていません。先に Ctrl+Shift+S で範囲を選択してください。",
                ToolTipIcon.Warning);
            return;
        }

        // オーバーレイを開かないので待機は不要、そのままキャプチャする
        await CaptureAndNotifyAsync(r);
    }

    /// <summary>
    /// 指定したスクリーン座標の矩形をキャプチャし、60%リサイズ(縦横比維持)した上で
    /// クリップボードコピー・(設定により)ファイル保存・通知バルーン表示を行う。
    /// </summary>
    private async Task CaptureAndNotifyAsync(Rectangle screenRect)
    {
        using Bitmap raw = ScreenCapture.CaptureRegion(screenRect);
        using Bitmap resized = ScreenCapture.ResizeKeepingAspectRatio(raw, ResizeScale);

        ScreenCapture.CopyToClipboard(resized);

        string message;
        if (_copyToClipboardOnly)
        {
            message = $"クリップボードにコピーしました。({resized.Width}x{resized.Height})";
        }
        else
        {
            string path = await Task.Run(() => ScreenCapture.SaveAsPng(resized));
            message = $"保存しました:\n{path}\n({resized.Width}x{resized.Height} / クリップボードにもコピー済み)";
        }

        _trayIcon.ShowBalloonTip(2500, "ScreenshotApp", message, ToolTipIcon.Info);
    }

    private static Icon CreateTrayIcon()
    {
        // 外部 .ico ファイルを使わず、コードでシンプルなアイコンを生成する
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(255, 33, 150, 243));
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var pen = new Pen(Color.White, 2.5f);
            g.DrawRectangle(pen, 8, 10, 16, 12);
            using var lensBrush = new SolidBrush(Color.White);
            g.FillEllipse(lensBrush, 13, 13, 6, 6);
        }
        IntPtr hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
        return key?.GetValue(AppName) is not null;
    }

    private static void SetAutoStart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true)
                         ?? Registry.CurrentUser.CreateSubKey(RunRegistryKey);
        if (enable)
        {
            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            if (key.GetValue(AppName) is not null)
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
    }

    private void ExitApp()
    {
        _hotkeyManager.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }
}
