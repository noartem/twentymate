using System;
using System.Runtime.InteropServices;

namespace TwentyMate.Platform;

/// <summary>
/// A tray icon built directly on Shell_NotifyIcon, replacing <c>System.Windows.Forms.NotifyIcon</c>.
/// Neither WinForms' control nor <c>Avalonia.Controls.TrayIcon</c> fits: this app opens its own
/// menu on both the left AND right click, and needs balloon notifications, which
/// <c>Avalonia.Controls.TrayIcon</c> doesn't support — per its Win32 backend, <c>Clicked</c> only
/// fires on the left button, a right click always shows (or, if empty, silently swallows) the
/// native menu, and Shell_NotifyIcon is called without NIF_INFO at all.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint NinBalloonUserClick = 0x0400 + 5;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;

    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const int IconId = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int CbSize;
        public IntPtr HWnd;
        public uint UId;
        public uint UFlags;
        public uint UCallbackMessage;
        public IntPtr HIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string SzTip;

        public uint DwState;
        public uint DwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string SzInfo;

        public uint UTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string SzInfoTitle;

        public uint DwInfoFlags;
        public Guid GuidItem;
        public IntPtr HBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint CbSize;
        public uint Style;
        public IntPtr LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public IntPtr HInstance;
        public IntPtr HIcon;
        public IntPtr HCursor;
        public IntPtr HbrBackground;
        public string? LpszMenuName;
        public string LpszClassName;
        public IntPtr HIconSm;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    // A message-only window's WndProc, called back by the same Win32 message loop Avalonia
    // already pumps on the UI thread. UnmanagedCallersOnly (rather than a delegate handed to
    // Marshal.GetFunctionPointerForDelegate) is the NativeAOT-supported way to receive a
    // native callback; it can't be an instance method, so state is routed through _current —
    // this app only ever creates one tray icon.
    private static TrayIcon? _current;

    [UnmanagedCallersOnly]
    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            return _current?.HandleMessage(hWnd, msg, wParam, lParam) ?? DefWindowProc(hWnd, msg, wParam, lParam);
        }
        catch
        {
            // Must never throw across the native boundary.
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private readonly IntPtr _hwnd;
    private readonly uint _taskbarCreatedMessage;

    private IntPtr _icon;
    private string _tooltip = "";
    private bool _added;

    public event Action? Clicked;
    public event Action? BalloonClicked;

    public unsafe TrayIcon()
    {
        _current = this;

        var className = $"TwentyMate.TrayIcon.{Guid.NewGuid():N}";
        var instance = GetModuleHandle(null);

        var wndClass = new WndClassEx
        {
            CbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            LpfnWndProc = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&StaticWndProc,
            HInstance = instance,
            LpszClassName = className,
        };
        RegisterClassEx(ref wndClass);

        _hwnd = CreateWindowEx(0, className, "TwentyMate.TrayIcon", 0, 0, 0, 0, 0,
            new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, instance, IntPtr.Zero);

        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    }

    /// <summary>
    /// Sets the icon shown in the tray. The handle is <b>not</b> owned by this instance —
    /// <see cref="TrayIconFactory"/> caches and reuses handles across calls (an unchanged
    /// state can hand back the same HICON many ticks in a row), so destroying whatever was
    /// set previously here would tear down an icon that's still live.
    /// </summary>
    public void SetIcon(IntPtr icon)
    {
        _icon = icon;
        Update();
    }

    public void SetTooltip(string text)
    {
        // Shell_NotifyIcon truncates szTip at 127 characters (128 including the terminator).
        _tooltip = text.Length > 127 ? text[..127] : text;
        Update();
    }

    public void ShowBalloon(string title, string body)
    {
        if (!_added) return;

        var data = new NotifyIconData
        {
            CbSize = Marshal.SizeOf<NotifyIconData>(),
            HWnd = _hwnd,
            UId = IconId,
            UFlags = NifInfo,
            SzInfo = body.Length > 255 ? body[..255] : body,
            SzInfoTitle = title.Length > 63 ? title[..63] : title,
        };
        Shell_NotifyIcon(NimModify, ref data);
    }

    /// <summary>
    /// Resends the icon and tooltip together. The first call is NIM_ADD; every call after that
    /// is a NIM_MODIFY that always carries every field, not just the one that changed — a
    /// MODIFY that omitted NIF_ICON, for instance, would be free to leave the old icon in place.
    /// </summary>
    private void Update()
    {
        var data = new NotifyIconData
        {
            CbSize = Marshal.SizeOf<NotifyIconData>(),
            HWnd = _hwnd,
            UId = IconId,
            UFlags = NifMessage | NifIcon | NifTip,
            UCallbackMessage = CallbackMessage,
            HIcon = _icon,
            SzTip = _tooltip,
        };

        Shell_NotifyIcon(_added ? NimModify : NimAdd, ref data);
        _added = true;
    }

    private IntPtr HandleMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreatedMessage)
        {
            // Explorer restarted and dropped every tray icon — re-add ours from scratch.
            _added = false;
            if (_icon != IntPtr.Zero || _tooltip.Length > 0) Update();
            return IntPtr.Zero;
        }

        if (msg == CallbackMessage)
        {
            var notification = (uint)lParam.ToInt64();
            switch (notification)
            {
                case WmLButtonUp:
                case WmRButtonUp:
                    Clicked?.Invoke();
                    break;
                case NinBalloonUserClick:
                    BalloonClicked?.Invoke();
                    break;
            }

            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = new NotifyIconData
            {
                CbSize = Marshal.SizeOf<NotifyIconData>(),
                HWnd = _hwnd,
                UId = IconId,
            };
            Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }

        // _icon is owned by whoever generates icon handles (TrayIconFactory) — not this class.
        _icon = IntPtr.Zero;

        DestroyWindow(_hwnd);
        if (ReferenceEquals(_current, this)) _current = null;
    }
}
