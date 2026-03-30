using System.Collections.Generic;

using FlowShellBar.App.Diagnostics;

using Microsoft.UI.Xaml;

using WinRT.Interop;

namespace FlowShellBar.App.Ui;

internal sealed class LeftSidebarHotKeyHost : IDisposable
{
    private const nuint SubclassId = 0x7007;

    private readonly Window _window;
    private readonly IAppLogger _logger;
    private readonly Func<LeftSidebarCommandKind, LeftSidebarCommandSnapshot> _commandExecutor;
    private readonly NativeMethods.SubclassProc _subclassProc;
    private readonly Dictionary<int, LeftSidebarCommandKind> _hotKeys = new()
    {
        [701] = LeftSidebarCommandKind.Toggle,
        [702] = LeftSidebarCommandKind.Open,
        [703] = LeftSidebarCommandKind.Close,
        [704] = LeftSidebarCommandKind.Detach,
        [705] = LeftSidebarCommandKind.Pin,
        [706] = LeftSidebarCommandKind.Attach,
    };

    private bool _started;
    private bool _subclassInstalled;

    public LeftSidebarHotKeyHost(
        Window window,
        IAppLogger logger,
        Func<LeftSidebarCommandKind, LeftSidebarCommandSnapshot> commandExecutor)
    {
        _window = window;
        _logger = logger;
        _commandExecutor = commandExecutor;
        _subclassProc = OnSubclassMessage;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(_window);
        _subclassInstalled = NativeMethods.SetWindowSubclass(hwnd, _subclassProc, SubclassId, 0);
        if (!_subclassInstalled)
        {
            _logger.Warning("Failed to install left sidebar hotkey subclass.");
            return;
        }

        RegisterHotKey(hwnd, 701, 'L');
        RegisterHotKey(hwnd, 702, 'O');
        RegisterHotKey(hwnd, 703, 'C');
        RegisterHotKey(hwnd, 704, 'D');
        RegisterHotKey(hwnd, 705, 'P');
        RegisterHotKey(hwnd, 706, 'A');

        _started = true;
    }

    public void Dispose()
    {
        var hwnd = WindowNative.GetWindowHandle(_window);

        foreach (var id in _hotKeys.Keys)
        {
            NativeMethods.UnregisterHotKey(hwnd, id);
        }

        if (_subclassInstalled)
        {
            NativeMethods.RemoveWindowSubclass(hwnd, _subclassProc, SubclassId);
            _subclassInstalled = false;
        }
    }

    private void RegisterHotKey(nint hwnd, int id, char key)
    {
        if (NativeMethods.RegisterHotKey(
            hwnd,
            id,
            NativeMethods.ModControl | NativeMethods.ModAlt,
            char.ToUpperInvariant(key)))
        {
            _logger.Info($"Registered left sidebar hotkey: id={id}; key=Ctrl+Alt+{char.ToUpperInvariant(key)}.");
            return;
        }

        _logger.Warning($"Failed to register left sidebar hotkey: id={id}; key=Ctrl+Alt+{char.ToUpperInvariant(key)}.");
    }

    private nint OnSubclassMessage(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData)
    {
        if (message == NativeMethods.WmHotkey
            && _hotKeys.TryGetValue(wParam.ToInt32(), out var commandKind))
        {
            var snapshot = _commandExecutor(commandKind);
            _logger.Info($"Left sidebar hotkey handled: command={commandKind}; mode={snapshot.Mode}; open={snapshot.IsOpen}.");
            return 0;
        }

        return NativeMethods.DefSubclassProc(hWnd, message, wParam, lParam);
    }
}
