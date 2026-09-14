namespace ScreenshotApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 二重起動防止
        using var mutex = new Mutex(true, "ScreenshotApp_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "ScreenshotApp はすでに起動しています（タスクトレイを確認してください）。",
                "ScreenshotApp",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
