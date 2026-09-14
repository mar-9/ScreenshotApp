using Microsoft.Win32;

namespace ScreenshotApp;

public sealed class TrayApplicationContext : ApplicationContext
{
    private const string AppName = "ScreenshotApp";
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyManager _hotkeyManager;
    private readonly ToolStripMenuItem _autoStartMenuItem;
    private readonly ToolStripMenuItem _copyOnlyMenuItem;

    private bool _copyToClipboardOnly; // true: クリップボードのみ / false: 保存もする

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();

        var captureItem = new ToolStripMenuItem("今すぐ範囲選択して撮影 (Ctrl+Shift+S)");
        captureItem.Click += (_, _) => StartCapture();
        menu.Items.Add(captureItem);

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
        bool registered = _hotkeyManager.Register(
            HotkeyManager.Modifiers.Control | HotkeyManager.Modifiers.Shift,
            Keys.S);

        _hotkeyManager.HotkeyTriggered += () =>
        {
            // ホットキーのコールバックは UI スレッドで呼ばれるが、
            // 念のため安全にディスパッチする
            StartCapture();
        };

        if (!registered)
        {
            _trayIcon.ShowBalloonTip(
                4000,
                "ScreenshotApp",
                "ホットキー (Ctrl+Shift+S) の登録に失敗しました。他のアプリと競合している可能性があります。トレイアイコンのメニューから手動でキャプチャできます。",
                ToolTipIcon.Warning);
        }
        else
        {
            _trayIcon.ShowBalloonTip(
                2500,
                "ScreenshotApp",
                "常駐を開始しました。Ctrl+Shift+S で範囲選択キャプチャができます。",
                ToolTipIcon.Info);
        }
    }

    private bool _isCapturing;

    private void StartCapture()
    {
        // 選択中の多重起動を防止
        if (_isCapturing) return;
        _isCapturing = true;

        // トレイのバルーン等が残っていると見た目に干渉するので少し待ってから起動
        var overlay = new OverlayForm();
        overlay.SelectionCompleted += rect =>
        {
            _isCapturing = false;
            if (rect is not Rectangle r) return; // キャンセル

            using var bitmap = ScreenCapture.CaptureRegion(r);
            ScreenCapture.CopyToClipboard(bitmap);

            string message;
            if (_copyToClipboardOnly)
            {
                message = "クリップボードにコピーしました。";
            }
            else
            {
                string path = ScreenCapture.SaveAsPng(bitmap);
                message = $"保存しました:\n{path}\n(クリップボードにもコピー済み)";
            }

            _trayIcon.ShowBalloonTip(2500, "ScreenshotApp", message, ToolTipIcon.Info);
        };
        overlay.Show();
        overlay.Activate();
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
