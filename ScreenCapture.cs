using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ScreenshotApp;

public static class ScreenCapture
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    /// <summary>
    /// アクティブ(最前面)ウィンドウのスクリーン座標の矩形を返す。
    /// Windows 10/11 では GetWindowRect に透明なリサイズ枠(影)が含まれてしまうため、
    /// 可能なら DWM の「実際に見えている枠」を優先して使う。
    /// 取得できない場合や最小化されている場合は null。
    /// </summary>
    public static Rectangle? GetActiveWindowRect()
    {
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero || IsIconic(hWnd)) return null;

        RECT rect;
        int hr = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>());
        if (hr != 0 && !GetWindowRect(hWnd, out rect)) return null;

        Rectangle r = rect.ToRectangle();
        return (r.Width > 0 && r.Height > 0) ? r : null;
    }

    /// <summary>
    /// 指定したスクリーン座標の矩形領域をキャプチャして Bitmap を返す。
    /// </summary>
    public static Bitmap CaptureRegion(Rectangle screenRect)
    {
        var bitmap = new Bitmap(screenRect.Width, screenRect.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(screenRect.Location, Point.Empty, screenRect.Size, CopyPixelOperation.SourceCopy);
        }
        return bitmap;
    }

    /// <summary>
    /// 縦横比を維持したまま指定倍率(例: 0.6 = 60%)にリサイズした新しい Bitmap を返す。
    /// 高品質な補間(HighQualityBicubic)でリサイズする。
    /// </summary>
    public static Bitmap ResizeKeepingAspectRatio(Bitmap source, double scale)
    {
        if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));

        int newWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        int newHeight = Math.Max(1, (int)Math.Round(source.Height * scale));

        var resized = new Bitmap(newWidth, newHeight, source.PixelFormat);
        resized.SetResolution(source.HorizontalResolution, source.VerticalResolution);

        using var g = Graphics.FromImage(resized);
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, newWidth, newHeight));

        return resized;
    }

    /// <summary>
    /// 保存先フォルダを返す。無ければ作成する。
    /// 既定: %USERPROFILE%\Pictures\Screenshots
    /// </summary>
    public static string GetSaveDirectory()
    {
        string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string dir = Path.Combine(picturesDir, "Screenshots");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Bitmap を PNG として保存し、保存先フルパスを返す。
    /// </summary>
    public static string SaveAsPng(Bitmap bitmap)
    {
        string dir = GetSaveDirectory();
        string fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        string fullPath = Path.Combine(dir, fileName);
        bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        return fullPath;
    }

    /// <summary>
    /// Bitmap をクリップボードにコピーする。
    /// </summary>
    public static void CopyToClipboard(Bitmap bitmap)
    {
        // STA スレッドから呼び出すこと(WinForms の UI スレッドであれば問題なし)
        Clipboard.SetImage(bitmap);
    }
}
