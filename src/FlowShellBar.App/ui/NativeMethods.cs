using System.Runtime.InteropServices;

namespace FlowShellBar.App.Ui;

internal static class NativeMethods
{
    public const int GwlStyle = -16;
    public const int GwlExstyle = -20;
    public const int DwmwaWindowCornerPreference = 33;
    public const int DwmwaBorderColor = 34;

    public const uint SwpNomove = 0x0002;
    public const uint SwpNosize = 0x0001;
    public const uint SwpNoactivate = 0x0010;
    public const uint SwpNoownerzorder = 0x0200;
    public const uint SwpShowwindow = 0x0040;
    public const uint SwpFramechanged = 0x0020;

    public static readonly nint WsPopup = new(0x80000000);
    public static readonly nint WsCaption = new(0x00C00000);
    public static readonly nint WsSysmenu = new(0x00080000);
    public static readonly nint WsThickframe = new(0x00040000);
    public static readonly nint WsMinimizebox = new(0x00020000);
    public static readonly nint WsMaximizebox = new(0x00010000);
    public static readonly nint WsExToolwindow = new(0x00000080);
    public static readonly nint WsExAppwindow = new(0x00040000);
    public static readonly nint WsExNoactivate = new(0x08000000);
    public static readonly nint WsExTopmost = new(0x00000008);
    public static readonly uint DwmColorNone = 0xFFFFFFFE;

    public static readonly nint HwndTopmost = new(-1);

    public delegate bool MonitorEnumProc(
        nint hMonitor,
        nint hdc,
        nint lprcMonitor,
        nint dwData);

    public enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public MONITORINFO monitorInfo;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(
        nint hWnd,
        int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(
        nint hWnd,
        int nIndex,
        nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(
        nint hWnd,
        out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(
        nint hWnd,
        out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(
        nint hWnd,
        ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(
        nint hdc,
        nint lprcClip,
        MonitorEnumProc lpfnEnum,
        nint dwData);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(
        nint hMonitor,
        ref MONITORINFOEX lpmi);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    public static extern int DwmSetWindowAttributeInt(
        nint hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    public static extern int DwmSetWindowAttributeUInt(
        nint hwnd,
        int dwAttribute,
        ref uint pvAttribute,
        int cbAttribute);
}
