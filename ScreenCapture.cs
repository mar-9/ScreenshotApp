namespace ScreenshotApp;

public static class ScreenCapture
{
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
