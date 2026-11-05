using Microsoft.Win32;

namespace ScreenshotApp;

public sealed class TrayApplicationContext : ApplicationContext
{
    private const string AppName = "ScreenshotApp";
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string SettingsRegistryKey = @"Software\ScreenshotApp";
    private const string ResizePercentValueName = "ResizePercent";

    // トレイメニューに表示するリサイズ倍率(%)の選択肢。増減したい場合はここを編集する。
    private static readonly int[] ResizePercentChoices = { 100, 80, 60, 50, 40, 30, 20 };
    private const int DefaultResizePercent = 60;

    // キャプチャ画像はアスペクト比を維持したままこの倍率(%)でリサイズする。100 ならリサイズしない。
    private int _resizePercent = DefaultResizePercent;

    // オーバーレイを閉じてから実際に画面をキャプチャするまでの待機時間(ms)。
    // 短すぎるとオーバーレイの色が画面に残った状態でキャプチャされてしまう。
    private const int PreCaptureDelayMs = 150;

    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyManager _hotkeyManager;
    private readonly ToolStripMenuItem _autoStartMenuItem;
    private readonly ToolStripMenuItem _copyOnlyMenuItem;

    private readonly int _captureHotkeyId;
    private readonly int _repeatHotkeyId;
    private readonly int _activeWindowHotkeyId;

    private bool _copyToClipboardOnly; // true: クリップボードのみ / false: 保存もする
    private Rectangle? _lastCaptureRect; // Ctrl+1 での再撮影用に、直前の選択範囲を保持

    public TrayApplicationContext()
    {
        _resizePercent = LoadResizePercent();

        var menu = new ContextMenuStrip();

        var captureItem = new ToolStripMenuItem("範囲選択して撮影 (Ctrl+Shift+S)");
        captureItem.Click += (_, _) => StartCapture();
        menu.Items.Add(captureItem);

        var repeatItem = new ToolStripMenuItem("前回と同じ範囲を再撮影 (Ctrl+1)");
        repeatItem.Click += async (_, _) => await RepeatLastCaptureAsync();
        menu.Items.Add(repeatItem);

        var activeWindowItem = new ToolStripMenuItem("アクティブウィンドウを撮影 (Ctrl+2)");
        activeWindowItem.Click += async (_, _) => await CaptureActiveWindowAsync();
        menu.Items.Add(activeWindowItem);

        menu.Items.Add(new ToolStripSeparator());

        var openFolderItem = new ToolStripMenuItem("保存フォルダを開く");
        openFolderItem.Click += (_, _) =>
        {
            string dir = ScreenCapture.GetSaveDirectory();
            System.Diagnostics.Process.Start("explorer.exe", dir);
        };
        menu.Items.Add(openFolderItem);

        var resizeMenu = new ToolStripMenuItem("リサイズ倍率");
        foreach (int percent in ResizePercentChoices)
        {
            string label = percent == 100 ? "100% (リサイズなし)" : $"{percent}%";
            var item = new ToolStripMenuItem(label)
            {
                Tag = percent,
                Checked = percent == _resizePercent,
            };
            item.Click += (_, _) =>
            {
                _resizePercent = percent;
                SaveResizePercent(percent);
                // ラジオボタンのように、選んだ倍率だけにチェックを付ける
                foreach (ToolStripMenuItem other in resizeMenu.DropDownItems)
                {
                    other.Checked = (int)other.Tag! == percent;
                }
            };
            resizeMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(resizeMenu);

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

        _activeWindowHotkeyId = _hotkeyManager.Register(
            HotkeyManager.Modifiers.Control,
            Keys.D2);

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
            else if (id == _activeWindowHotkeyId)
            {
                await CaptureActiveWindowAsync();
            }
        };
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

            await CaptureAsync(r);
        };
        overlay.Show();
        overlay.Activate();
    }

    /// <summary>
    /// 直前にキャプチャした範囲と同じ範囲を、選択操作なしで再キャプチャする(Ctrl+1)。
    /// </summary>
    private async Task RepeatLastCaptureAsync()
    {
        // まだ一度も範囲選択していない場合は何もしない
        if (_lastCaptureRect is not Rectangle r) return;

        // オーバーレイを開かないので待機は不要、そのままキャプチャする
        await CaptureAsync(r);
    }

    /// <summary>
    /// アクティブ(最前面)ウィンドウをキャプチャする(Ctrl+2)。
    /// </summary>
    private async Task CaptureActiveWindowAsync()
    {
        if (ScreenCapture.GetActiveWindowRect() is not Rectangle r) return;
        await CaptureAsync(r);
    }

    /// <summary>
    /// 指定したスクリーン座標の矩形をキャプチャし、60%リサイズ(縦横比維持)した上で
    /// クリップボードコピー・(設定により)ファイル保存を行う。通知は出さない。
    /// </summary>
    private async Task CaptureAsync(Rectangle screenRect)
    {
        using Bitmap raw = ScreenCapture.CaptureRegion(screenRect);
        // 100% のときはリサイズせず、キャプチャした画像をそのまま使う
        using Bitmap? scaled = _resizePercent >= 100
            ? null
            : ScreenCapture.ResizeKeepingAspectRatio(raw, _resizePercent / 100.0);
        Bitmap resized = scaled ?? raw;

        ScreenCapture.CopyToClipboard(resized);

        if (!_copyToClipboardOnly)
        {
            await Task.Run(() => ScreenCapture.SaveAsPng(resized));
        }
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

    private static int LoadResizePercent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryKey, writable: false);
            if (key?.GetValue(ResizePercentValueName) is int saved && ResizePercentChoices.Contains(saved))
            {
                return saved;
            }
        }
        catch
        {
            // 読み込みに失敗した場合は既定値を使う
        }
        return DefaultResizePercent;
    }

    private static void SaveResizePercent(int percent)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsRegistryKey);
            key.SetValue(ResizePercentValueName, percent, RegistryValueKind.DWord);
        }
        catch
        {
            // 保存に失敗しても動作は継続する(次回起動時に既定値に戻るだけ)
        }
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
