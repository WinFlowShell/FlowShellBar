using FlowShellBar.App.Diagnostics;

namespace FlowShellBar.App.Integrations;

public sealed class FlowtileWmIpcAdapter : IFlowtileWmAdapter
{
    private const int ProtocolVersion = 1;
    private readonly IAppLogger _logger;
    private readonly NamedPipeJsonClient _client;
    private readonly string _pipeName;
    private readonly bool _ipcDisabled;
    private bool _isConnected;
    private bool _disabledLogged;
    private FlowtileBarProjection? _lastProjection;

    public FlowtileWmIpcAdapter(IAppLogger logger)
    {
        _logger = logger;
        _client = new NamedPipeJsonClient();
        _pipeName = Environment.GetEnvironmentVariable("FLOWSHELL_BAR_FLOWTILEWM_PIPE")
            ?? "flowtilewm-ipc-v1";
        _ipcDisabled = IsEnvironmentFlagEnabled("FLOWSHELL_BAR_DISABLE_FLOWTILEWM_IPC");
    }

    public async Task<bool> TryConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_ipcDisabled)
        {
            LogDisabledOnce();
            SetConnectionState(false, null);
            return false;
        }

        try
        {
            var response = await SendCommandAsync("get_focus", new EmptyPayload(), cancellationToken);
            if (!response.Ok)
            {
                _logger.Warning($"FlowtileWM rejected get_focus probe: {FormatError(response.Error)}.");
                SetConnectionState(false, null);
                return false;
            }

            SetConnectionState(true, "FlowtileWM IPC connected.");
            return true;
        }
        catch (Exception exception)
        {
            HandleFailure("FlowtileWM IPC probe failed.", exception);
            return false;
        }
    }

    public async Task<FlowtileBarProjection?> ReadProjectionAsync(CancellationToken cancellationToken = default)
    {
        if (_ipcDisabled)
        {
            LogDisabledOnce();
            return null;
        }

        try
        {
            var outputs = await ReadResultAsync<FlowtileOutputsResult>("get_outputs", cancellationToken);
            var workspaces = await ReadResultAsync<FlowtileWorkspacesResult>("get_workspaces", cancellationToken);
            var windows = await ReadResultAsync<FlowtileWindowsResult>("get_windows", cancellationToken);
            var focus = await ReadResultAsync<FlowtileFocusResult>("get_focus", cancellationToken);

            var projection = MapProjection(outputs.Outputs, workspaces.Workspaces, windows.Windows, focus.Focus, focus.Overview);
            if (projection is null)
            {
                _logger.Warning("FlowtileWM projection was received but could not be mapped for the bar.");
                SetConnectionState(false, null);
                return null;
            }

            _lastProjection = projection;
            SetConnectionState(true, "FlowtileWM live projection available.");
            return projection;
        }
        catch (Exception exception)
        {
            HandleFailure("FlowtileWM live projection refresh failed.", exception);
            return null;
        }
    }

    public async Task<bool> SwitchWorkspaceAsync(int workspaceId, CancellationToken cancellationToken = default)
    {
        if (_ipcDisabled)
        {
            LogDisabledOnce();
            return false;
        }

        var projection = _lastProjection ?? await ReadProjectionAsync(cancellationToken);
        if (projection is null)
        {
            return false;
        }

        var activeWorkspace = projection.Workspaces.FirstOrDefault(workspace => workspace.IsActive)
            ?? projection.Workspaces.FirstOrDefault();
        var targetWorkspace = projection.Workspaces.FirstOrDefault(workspace => workspace.WorkspaceId == workspaceId);

        if (activeWorkspace is null || targetWorkspace is null)
        {
            _logger.Warning($"FlowtileWM workspace switch target is unavailable: {workspaceId}.");
            return false;
        }

        var delta = targetWorkspace.VerticalIndex - activeWorkspace.VerticalIndex;
        if (delta == 0)
        {
            return true;
        }

        var command = delta > 0 ? "focus_workspace_down" : "focus_workspace_up";
        var steps = Math.Abs(delta);

        for (var index = 0; index < steps; index++)
        {
            var response = await SendCommandAsync(
                command,
                new MonitorScopedPayload(projection.MonitorId),
                cancellationToken);

            if (!response.Ok)
            {
                _logger.Warning($"FlowtileWM rejected {command}: {FormatError(response.Error)}.");
                return false;
            }
        }

        await ReadProjectionAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ToggleOverviewAsync(CancellationToken cancellationToken = default)
    {
        if (_ipcDisabled)
        {
            LogDisabledOnce();
            return false;
        }

        try
        {
            var projection = _lastProjection ?? await ReadProjectionAsync(cancellationToken);
            var payload = projection is null
                ? new MonitorScopedPayload(null)
                : new MonitorScopedPayload(projection.MonitorId);

            var response = await SendCommandAsync("toggle_overview", payload, cancellationToken);
            if (!response.Ok)
            {
                _logger.Warning($"FlowtileWM rejected toggle_overview: {FormatError(response.Error)}.");
                return false;
            }

            await ReadProjectionAsync(cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            HandleFailure("FlowtileWM toggle_overview failed.", exception);
            return false;
        }
    }

    private static bool IsEnvironmentFlagEnabled(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private void LogDisabledOnce()
    {
        if (_disabledLogged)
        {
            return;
        }

        _disabledLogged = true;
        _logger.Info("FlowtileWM IPC disabled by FLOWSHELL_BAR_DISABLE_FLOWTILEWM_IPC.");
    }

    private async Task<T> ReadResultAsync<T>(string command, CancellationToken cancellationToken)
    {
        var response = await SendCommandAsync(command, new EmptyPayload(), cancellationToken);
        if (!response.Ok)
        {
            throw new InvalidOperationException($"FlowtileWM command '{command}' failed: {FormatError(response.Error)}.");
        }

        return _client.Deserialize<T>(response.Result);
    }

    private async Task<JsonPipeResponseEnvelope> SendCommandAsync(
        string command,
        object payload,
        CancellationToken cancellationToken)
    {
        return await _client.SendAsync(
            _pipeName,
            new FlowtileWmRequestEnvelope(
                ProtocolVersion,
                $"wm-{command}-{Guid.NewGuid():N}",
                command,
                payload),
            cancellationToken);
    }

    private FlowtileBarProjection? MapProjection(
        IReadOnlyList<FlowtileOutputDto>? outputs,
        IReadOnlyList<FlowtileWorkspaceDto>? workspaces,
        IReadOnlyList<FlowtileWindowDto>? windows,
        FlowtileFocusDto? focus,
        FlowtileOverviewDto? overview)
    {
        if (workspaces is null || workspaces.Count == 0)
        {
            return null;
        }

        var monitorId = focus?.MonitorId
            ?? workspaces.FirstOrDefault(workspace => workspace.IsActive)?.MonitorId
            ?? workspaces[0].MonitorId;

        var orderedMonitorWorkspaces = workspaces
            .Where(workspace => workspace.MonitorId == monitorId)
            .OrderBy(workspace => workspace.VerticalIndex)
            .ToArray();
        var lastVisibleWorkspaceIndex = Array.FindLastIndex(
            orderedMonitorWorkspaces,
            workspace => !workspace.IsTail || workspace.IsActive || !workspace.IsEmpty);
        var visibleMonitorWorkspaces = lastVisibleWorkspaceIndex >= 0
            ? orderedMonitorWorkspaces.Take(lastVisibleWorkspaceIndex + 1)
            : orderedMonitorWorkspaces.Take(1);

        var monitorWorkspaces = visibleMonitorWorkspaces
            .Select(workspace => new FlowtileWorkspaceProjection(
                WorkspaceId: checked((int)workspace.WorkspaceId),
                VerticalIndex: workspace.VerticalIndex,
                Label: string.IsNullOrWhiteSpace(workspace.Name)
                    ? (workspace.VerticalIndex + 1).ToString()
                    : workspace.Name,
                IsActive: workspace.IsActive,
                IsOccupied: !workspace.IsEmpty))
            .ToArray();

        if (monitorWorkspaces.Length == 0)
        {
            return null;
        }

        var activeWorkspaceId = focus?.WorkspaceId is ulong focusedWorkspaceId
            ? checked((int)focusedWorkspaceId)
            : monitorWorkspaces.FirstOrDefault(workspace => workspace.IsActive)?.WorkspaceId
                ?? monitorWorkspaces[0].WorkspaceId;
        var activeOutput = outputs?.FirstOrDefault(output => output.MonitorId == monitorId)
            ?? outputs?.FirstOrDefault(output => output.ActiveWorkspaceId == (ulong)activeWorkspaceId)
            ?? outputs?.FirstOrDefault(output => output.IsPrimary);
        var monitorBinding = string.IsNullOrWhiteSpace(activeOutput?.Binding)
            ? null
            : activeOutput!.Binding;
        var workArea = activeOutput is null
            ? null
            : new FlowtileRectProjection(
                X: activeOutput.WorkArea.X,
                Y: activeOutput.WorkArea.Y,
                Width: checked((int)activeOutput.WorkArea.Width),
                Height: checked((int)activeOutput.WorkArea.Height));

        var activeWindow = windows?
            .FirstOrDefault(window => focus?.WindowId is ulong focusWindowId && window.WindowId == focusWindowId)
            ?? windows?.FirstOrDefault(window => window.IsFocused && window.WorkspaceId == (ulong)activeWorkspaceId)
            ?? windows?.FirstOrDefault(window => window.WorkspaceId == (ulong)activeWorkspaceId);

        return new FlowtileBarProjection(
            MonitorId: monitorId,
            MonitorBinding: monitorBinding,
            WorkArea: workArea,
            Workspaces: monitorWorkspaces,
            ActiveWorkspaceId: activeWorkspaceId,
            ActiveWindowAppName: activeWindow?.ProcessName
                ?? activeWindow?.ClassName
                ?? "FlowtileWM",
            ActiveWindowTitle: string.IsNullOrWhiteSpace(activeWindow?.Title)
                ? $"Workspace {monitorWorkspaces.First(workspace => workspace.WorkspaceId == activeWorkspaceId).Label}"
                : activeWindow.Title,
            IsOverviewOpen: overview?.IsOpen ?? false);
    }

    private static string FormatError(JsonPipeErrorEnvelope? error)
    {
        return error is null
            ? "unknown error"
            : $"{error.Code}: {error.Message}";
    }

    private void HandleFailure(string message, Exception exception)
    {
        SetConnectionState(false, "FlowtileWM IPC unavailable.");
        _logger.Warning($"{message} {exception.Message}");
    }

    private void SetConnectionState(bool connected, string? transitionMessage)
    {
        if (_isConnected == connected)
        {
            return;
        }

        _isConnected = connected;
        if (!string.IsNullOrWhiteSpace(transitionMessage))
        {
            _logger.Info(transitionMessage);
        }
    }

    private sealed record EmptyPayload;

    private sealed record MonitorScopedPayload(ulong? MonitorId);

    private sealed record FlowtileWmRequestEnvelope(
        int ProtocolVersion,
        string RequestId,
        string Command,
        object Payload);

    private sealed record FlowtileOutputsResult(
        ulong StateVersion,
        IReadOnlyList<FlowtileOutputDto> Outputs);

    private sealed record FlowtileWorkspacesResult(
        ulong StateVersion,
        IReadOnlyList<FlowtileWorkspaceDto> Workspaces);

    private sealed record FlowtileWindowsResult(
        ulong StateVersion,
        IReadOnlyList<FlowtileWindowDto> Windows);

    private sealed record FlowtileFocusResult(
        ulong StateVersion,
        FlowtileFocusDto Focus,
        FlowtileOverviewDto Overview);

    private sealed record FlowtileWorkspaceDto(
        ulong WorkspaceId,
        ulong MonitorId,
        int VerticalIndex,
        string? Name,
        bool IsActive,
        bool IsEmpty,
        bool IsTail);

    private sealed record FlowtileWindowDto(
        ulong WindowId,
        ulong MonitorId,
        ulong WorkspaceId,
        string Title,
        string ClassName,
        string? ProcessName,
        bool IsFocused);

    private sealed record FlowtileFocusDto(
        ulong? MonitorId,
        ulong? WorkspaceId,
        ulong? WindowId,
        string Origin);

    private sealed record FlowtileOverviewDto(
        bool IsOpen,
        ulong? MonitorId);

    private sealed record FlowtileOutputDto(
        ulong MonitorId,
        string? Binding,
        uint Dpi,
        bool IsPrimary,
        FlowtileRectDto WorkArea,
        int WorkspaceCount,
        ulong? ActiveWorkspaceId);

    private sealed record FlowtileRectDto(
        int X,
        int Y,
        uint Width,
        uint Height);
}
