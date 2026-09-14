using System.Runtime.InteropServices;

namespace ScreenshotApp;

/// <summary>
/// Win32 の RegisterHotKey / UnregisterHotKey を使い、
/// アプリがフォアグラウンドでなくてもグローバルにホットキーを検知するためのクラス。
/// メッセージ受信用に非表示の NativeWindow を1つ内部に持つ。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    // 修飾キー
    [Flags]
    public enum Modifiers : uint
    {
        None = 0x0000,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
    }

    private class MessageWindow : NativeWindow
    {
        public event Action<int>? HotkeyPressed;

        public MessageWindow()
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                HotkeyPressed?.Invoke(id);
            }
            base.WndProc(ref m);
        }
    }

    private readonly MessageWindow _window;
    private readonly HashSet<int> _registeredIds = new();
    private int _nextId = 1;

    /// <summary>
    /// ホットキーが押されたときに発火。引数は Register が返した ID なので、
    /// 複数ホットキーを登録している場合はこの ID で判別する。
    /// </summary>
    public event Action<int>? HotkeyPressed;

    public HotkeyManager()
    {
        _window = new MessageWindow();
        _window.HotkeyPressed += id => HotkeyPressed?.Invoke(id);
    }

    /// <summary>
    /// ホットキーを登録する。成功した場合は識別用の ID(1以上)、失敗した場合は -1 を返す。
    /// 例: int id = Register(Modifiers.Control | Modifiers.Shift, Keys.S)
    /// </summary>
    public int Register(Modifiers modifiers, Keys key)
    {
        int id = _nextId++;
        bool ok = RegisterHotKey(_window.Handle, id, (uint)modifiers, (uint)key);
        if (!ok)
        {
            return -1;
        }
        _registeredIds.Add(id);
        return id;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registeredIds)
        {
            UnregisterHotKey(_window.Handle, id);
        }
        _registeredIds.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.DestroyHandle();
    }
}
