using System.Runtime.InteropServices;

using FlowShellBar.App.Application.Models;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

using Windows.Foundation;
using Windows.Graphics;

using WinRT.Interop;

namespace FlowShellBar.App.Ui;

internal static class ShellSurfaceWindowing
{
    private static readonly NativeMethods.MonitorEnumProc MonitorBoundsLookupProc = OnMonitorBoundsLookup;
    private static readonly nint DarkBackgroundBrush = NativeMethods.CreateSolidBrush(ToColorRef(0x12, 0x10, 0x0F));

    public static int GetEnvironmentInt(string variableName, int fallback)
    {
        var rawValue = Environment.GetEnvironmentVariable(variableName);
        return int.TryParse(rawValue, out var value) ? value : fallback;
    }

    public static int GetBarHeight()
    {
        var configuredHeight = GetEnvironmentInt("FLOWSHELL_BAR_HEIGHT_PX", 37);
        return configuredHeight is 40 or 41 or 46 or 60 ? 37 : configuredHeight;
    }

    public static AppWindow GetAppWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    public static RectInt32 GetWindowBounds(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return new RectInt32(
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));
    }

    public static void PrepareCompanionSurface(
        Window window,
        string title,
        RectInt32 clientBounds,
        bool noActivate,
        bool showWindow = true)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var appWindow = GetAppWindow(window);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        appWindow.IsShownInSwitchers = false;
        ApplyCompanionShellSurfaceStyle(hwnd, noActivate);
        appWindow.MoveAndResize(clientBounds);
        appWindow.Title = title;

        var adjustedBounds = GetOuterBoundsForClientArea(hwnd, clientBounds);
        ApplyTopmostBounds(hwnd, adjustedBounds, showWindow);
    }

    public static void EnsureTopmost(Window window, bool noActivate)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        ApplyCompanionShellSurfaceStyle(hwnd, noActivate);
        ReassertTopmost(hwnd);
    }

    public static void ShowCompanionSurface(Window window, bool noActivate)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        ApplyCompanionShellSurfaceStyle(hwnd, noActivate);
        NativeMethods.ShowWindow(hwnd, noActivate ? NativeMethods.SwShownoactivate : NativeMethods.SwShow);
        ReassertTopmost(hwnd);
    }

    public static void HideCompanionSurface(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SwHide);
    }

    public static RectInt32 ResolveShellAnchorBounds(
        Window fallbackWindow,
        BarSurfacePlacementModel placement,
        int referenceHeight,
        out string anchorSource)
    {
        if (TryResolveFlowtileAnchorBounds(placement, referenceHeight, out var wmBounds, out anchorSource))
        {
            return wmBounds;
        }

        var hwnd = WindowNative.GetWindowHandle(fallbackWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        anchorSource = "displayarea-fallback";
        return displayArea.OuterBounds;
    }

    public static bool TryGetElementScreenBounds(
        Window window,
        FrameworkElement element,
        out RectInt32 bounds)
    {
        if (element.XamlRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            bounds = default;
            return false;
        }

        var transform = element.TransformToVisual(null);
        var elementOrigin = transform.TransformPoint(new Point(0, 0));
        var scale = element.XamlRoot.RasterizationScale;

        var screenPoint = new NativeMethods.POINT(
            (int)Math.Round(elementOrigin.X * scale),
            (int)Math.Round(elementOrigin.Y * scale));

        var hwnd = WindowNative.GetWindowHandle(window);
        if (!NativeMethods.ClientToScreen(hwnd, ref screenPoint))
        {
            bounds = default;
            return false;
        }

        bounds = new RectInt32(
            screenPoint.X,
            screenPoint.Y,
            Math.Max(1, (int)Math.Round(element.ActualWidth * scale)),
            Math.Max(1, (int)Math.Round(element.ActualHeight * scale)));
        return true;
    }

    private static bool TryResolveFlowtileAnchorBounds(
        BarSurfacePlacementModel placement,
        int referenceHeight,
        out RectInt32 bounds,
        out string anchorSource)
    {
        if (!placement.IsFlowtileBound)
        {
            bounds = default;
            anchorSource = string.Empty;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(placement.MonitorBinding)
            && TryGetMonitorBoundsByBinding(placement.MonitorBinding, out bounds))
        {
            anchorSource = $"flowtilewm-binding:{placement.MonitorBinding}";
            return true;
        }

        if (placement.WorkAreaWidth <= 0 || placement.WorkAreaHeight <= 0)
        {
            bounds = default;
            anchorSource = string.Empty;
            return false;
        }

        var reservedTop = GetEnvironmentInt("FLOWSHELL_RESERVED_TOP_PX", referenceHeight);
        var top = placement.WorkAreaY >= reservedTop
            ? placement.WorkAreaY - reservedTop
            : placement.WorkAreaY;
        var height = Math.Max(referenceHeight, placement.WorkAreaHeight + Math.Max(0, placement.WorkAreaY - top));

        bounds = new RectInt32(
            placement.WorkAreaX,
            top,
            placement.WorkAreaWidth,
            height);
        anchorSource = "flowtilewm-work-area";
        return true;
    }

    private static bool TryGetMonitorBoundsByBinding(string monitorBinding, out RectInt32 bounds)
    {
        var state = new MonitorLookupState(monitorBinding);
        var handle = GCHandle.Alloc(state);

        try
        {
            NativeMethods.EnumDisplayMonitors(0, 0, MonitorBoundsLookupProc, GCHandle.ToIntPtr(handle));
            if (state.Bounds is RectInt32 resolvedBounds)
            {
                bounds = resolvedBounds;
                return true;
            }
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        bounds = default;
        return false;
    }

    private static bool OnMonitorBoundsLookup(
        nint monitorHandle,
        nint _,
        nint __,
        nint userData)
    {
        var handle = GCHandle.FromIntPtr(userData);
        if (handle.Target is not MonitorLookupState state)
        {
            return false;
        }

        var info = new NativeMethods.MONITORINFOEX
        {
            monitorInfo = new NativeMethods.MONITORINFO
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            },
            szDevice = string.Empty,
        };

        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref info))
        {
            return true;
        }

        if (!string.Equals(info.szDevice, state.TargetBinding, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rect = info.monitorInfo.rcMonitor;
        state.Bounds = new RectInt32(
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));
        return false;
    }

    private static void ApplyCompanionShellSurfaceStyle(nint hwnd, bool noActivate)
    {
        ApplyDarkShellSurfaceBackground(hwnd);

        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle);
        style |= NativeMethods.WsPopup;
        style &= ~(NativeMethods.WsCaption
            | NativeMethods.WsSysmenu
            | NativeMethods.WsThickframe
            | NativeMethods.WsMinimizebox
            | NativeMethods.WsMaximizebox);

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlStyle, style);

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExstyle);
        exStyle |= NativeMethods.WsExToolwindow | NativeMethods.WsExTopmost;
        exStyle &= ~NativeMethods.WsExAppwindow;

        if (noActivate)
        {
            exStyle |= NativeMethods.WsExNoactivate;
        }
        else
        {
            exStyle &= ~NativeMethods.WsExNoactivate;
        }

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExstyle, exStyle);

        var cornerPreference = (int)NativeMethods.DwmWindowCornerPreference.DoNotRound;
        NativeMethods.DwmSetWindowAttributeInt(
            hwnd,
            NativeMethods.DwmwaWindowCornerPreference,
            ref cornerPreference,
            sizeof(int));

        var borderColor = NativeMethods.DwmColorNone;
        NativeMethods.DwmSetWindowAttributeUInt(
            hwnd,
            NativeMethods.DwmwaBorderColor,
            ref borderColor,
            sizeof(uint));

        var immersiveDarkMode = 1;
        NativeMethods.DwmSetWindowAttributeInt(
            hwnd,
            NativeMethods.DwmwaUseImmersiveDarkMode,
            ref immersiveDarkMode,
            sizeof(int));
    }

    private static void ApplyDarkShellSurfaceBackground(nint hwnd)
    {
        if (DarkBackgroundBrush != 0)
        {
            NativeMethods.SetClassLongPtr(hwnd, NativeMethods.GclpHbrbackground, DarkBackgroundBrush);
        }
    }

    private static RectInt32 GetOuterBoundsForClientArea(nint hwnd, RectInt32 desiredClientBounds)
    {
        NativeMethods.GetWindowRect(hwnd, out var windowRect);
        NativeMethods.GetClientRect(hwnd, out var clientRect);

        var clientTopLeft = new NativeMethods.POINT(0, 0);
        var clientBottomRight = new NativeMethods.POINT(clientRect.Right, clientRect.Bottom);

        NativeMethods.ClientToScreen(hwnd, ref clientTopLeft);
        NativeMethods.ClientToScreen(hwnd, ref clientBottomRight);

        var leftInset = clientTopLeft.X - windowRect.Left;
        var topInset = clientTopLeft.Y - windowRect.Top;
        var rightInset = windowRect.Right - clientBottomRight.X;
        var bottomInset = windowRect.Bottom - clientBottomRight.Y;

        return new RectInt32(
            desiredClientBounds.X - leftInset,
            desiredClientBounds.Y - topInset,
            desiredClientBounds.Width + leftInset + rightInset,
            desiredClientBounds.Height + topInset + bottomInset);
    }

    private static void ReassertTopmost(nint hwnd)
    {
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNomove
            | NativeMethods.SwpNosize
            | NativeMethods.SwpNoactivate
            | NativeMethods.SwpNoownerzorder
            | NativeMethods.SwpFramechanged);
    }

    private static void ApplyTopmostBounds(nint hwnd, RectInt32 bounds, bool showWindow)
    {
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpNoactivate
            | NativeMethods.SwpNoownerzorder
            | (showWindow ? NativeMethods.SwpShowwindow : 0u)
            | NativeMethods.SwpFramechanged);
    }

    private sealed class MonitorLookupState
    {
        public MonitorLookupState(string targetBinding)
        {
            TargetBinding = targetBinding;
        }

        public string TargetBinding { get; }

        public RectInt32? Bounds { get; set; }
    }

    private static uint ToColorRef(byte r, byte g, byte b)
    {
        return (uint)(r | (g << 8) | (b << 16));
    }
}
