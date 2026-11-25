// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace Soncle.Windows.Web;

/// <summary>
/// The notification-area (tray) icon, straight on Shell_NotifyIcon: a message-only window gets the
/// icon's clicks, left click shows Soncle, right click opens a native menu. No library, nothing
/// running unless "Keep running in the tray" is on. Created and used on the UI thread.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    public enum Command { Show = 1, PlayPause, Next, Previous, Quit }

    private const int WM_APP_TRAY = 0x8000 + 1;   // WM_APP + 1
    private const int WM_LBUTTONUP = 0x0202, WM_CONTEXTMENU = 0x007B, WM_NULL = 0;
    private const int NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;
    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_SHOWTIP = 0x80;
    private const int NOTIFYICON_VERSION_4 = 4;
    private const uint MF_STRING = 0, MF_SEPARATOR = 0x800, MF_GRAYED = 1;
    private const uint TPM_RIGHTBUTTON = 2, TPM_RETURNCMD = 0x100, TPM_BOTTOMALIGN = 0x20;

    private readonly Action<Command> _onCommand;
    private readonly WndProc _proc;          // kept alive: the window calls it
    private readonly IntPtr _hwnd, _icon;
    private readonly uint _taskbarCreated;
    private string _tip = "Soncle", _title = "";
    private bool _playing, _added;

    public TrayIcon(string iconPath, Action<Command> onCommand)
    {
        _onCommand = onCommand;
        _proc = Proc;
        var cls = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = GetModuleHandle(null),
            lpszClassName = "SoncleTray"
        };
        RegisterClassEx(ref cls);   // fails harmlessly if already registered
        _hwnd = CreateWindowEx(0, "SoncleTray", "Soncle tray", 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        _icon = LoadImage(IntPtr.Zero, iconPath, 1 /* IMAGE_ICON */, GetSystemMetrics(49), GetSystemMetrics(50), 0x10 /* LR_LOADFROMFILE */);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");   // Explorer restarted: add it again
        try { SetPreferredAppMode(1 /* AllowDark */); FlushMenuThemes(); } catch { /* menus stay light */ }
        Add();
    }

    public void Update(string title, string artist, bool playing)
    {
        _playing = playing;
        _title = title;
        var tip = title.Length == 0 ? "Soncle" : artist.Length > 0 ? $"{title} — {artist}" : title;
        if (tip.Length > 120) tip = tip[..120];
        if (tip == _tip) return;
        _tip = tip;
        var d = Data(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_MODIFY, ref d);
    }

    private void Add()
    {
        var d = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIcon(NIM_ADD, ref d);
        d.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref d);
    }

    private NOTIFYICONDATA Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = _icon,
        szTip = _tip
    };

    private IntPtr Proc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_APP_TRAY)
        {
            switch ((int)((long)lParam & 0xFFFF))
            {
                case WM_LBUTTONUP:
                case NIN_SELECT:
                case NIN_KEYSELECT:
                    _onCommand(Command.Show);
                    break;
                case WM_CONTEXTMENU:
                    ShowMenu();
                    break;
            }
            return IntPtr.Zero;
        }
        if (msg == _taskbarCreated && _taskbarCreated != 0) { Add(); return IntPtr.Zero; }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            if (_title.Length > 0)
            {
                AppendMenu(menu, MF_STRING | MF_GRAYED, 0, _title.Length > 60 ? _title[..60] : _title);
                AppendMenu(menu, MF_SEPARATOR, 0, null);
            }
            AppendMenu(menu, MF_STRING, (uint)Command.PlayPause, _playing ? "Pause" : "Play");
            AppendMenu(menu, MF_STRING, (uint)Command.Next, "Next");
            AppendMenu(menu, MF_STRING, (uint)Command.Previous, "Previous");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, (uint)Command.Show, "Show Soncle");
            AppendMenu(menu, MF_STRING, (uint)Command.Quit, "Quit");
            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd);   // so the menu closes when you click elsewhere
            var cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_BOTTOMALIGN, pt.X, pt.Y, _hwnd, IntPtr.Zero);
            PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            if (cmd > 0) _onCommand((Command)cmd);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var d = Data(0);
            Shell_NotifyIcon(NIM_DELETE, ref d);
            _added = false;
        }
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
    }

    // ---------- Win32 ----------
    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr inst, string name, uint type, int cx, int cy, uint load);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, uint id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    // dark context menus when Windows is in dark mode (uxtheme ordinals, as Chromium and Explorer use)
    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemes();
}
