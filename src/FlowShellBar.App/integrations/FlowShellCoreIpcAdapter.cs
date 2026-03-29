using System.Diagnostics;

using FlowShellBar.App.Diagnostics;

namespace FlowShellBar.App.Integrations;

public sealed class FlowShellCoreIpcAdapter : IFlowShellCoreAdapter
{
    private const int ProtocolVersion = 1;
    private readonly IAppLogger _logger;
    private readonly NamedPipeJsonClient _client;
    private readonly string _pipeName;
    private readonly string _appId;
    private readonly string _instanceId;
    private readonly bool _ipcDisabled;
    private bool _isConnected;
    private bool _disabledLogged;

    public FlowShellCoreIpcAdapter(IAppLogger logger)
    {
        _logger = logger;
        _client = new NamedPipeJsonClient();
        _pipeName = Environment.GetEnvironmentVariable("FLOWSHELL_BAR_FLOWSHELLCORE_PIPE")
            ?? "flowshellcore-ipc-v1";
        _appId = Environment.GetEnvironmentVariable("FLOWSHELL_CORE_APP_ID")
            ?? "flowshell-bar";
        _instanceId = $"flowshell-bar-{Environment.ProcessId}-{Guid.NewGuid():N}";
        _ipcDisabled = IsEnvironmentFlagEnabled("FLOWSHELL_BAR_DISABLE_FLOWSHELLCORE_IPC");
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
            var response = await SendCommandAsync("list_apps", new EmptyPayload(), cancellationToken);
            if (!response.Ok)
            {
                _logger.Warning($"FlowShellCore rejected list_apps probe: {FormatError(response.Error)}.");
                SetConnectionState(false, null);
                return false;
            }

            SetConnectionState(true, "FlowShellCore IPC connected.");
            return true;
        }
        catch (Exception exception)
        {
            HandleFailure("FlowShellCore IPC probe failed.", exception);
            return false;
        }
    }

    public async Task<FlowShellCoreSessionProjection?> ReadSessionProjectionAsync(CancellationToken cancellationToken = default)
    {
        if (_ipcDisabled)
        {
            LogDisabledOnce();
            return null;
        }

        try
        {
            var snapshot = await ListAppsAsync(cancellationToken);
            snapshot = await EnsureRegistrationBaselineAsync(snapshot, cancellationToken);

            var app = snapshot.Apps.FirstOrDefault(candidate => AppIdsMatch(candidate.AppId));
            if (app is not null && app.InstanceId == _instanceId)
            {
                var heartbeatAccepted = await SendHeartbeatAsync(cancellationToken);
                if (heartbeatAccepted)
                {
                    snapshot = await ListAppsAsync(cancellationToken);
                    app = snapshot.Apps.FirstOrDefault(candidate => AppIdsMatch(candidate.AppId));
                }
            }

            SetConnectionState(true, "FlowShellCore session projection available.");

            return new FlowShellCoreSessionProjection(
                SessionMode: MapSessionMode(snapshot.RuntimeMode),
                IsBarRegistered: app is not null && string.Equals(app.InstanceId, _instanceId, StringComparison.Ordinal),
                IsBarReady: IsReadyStatus(app?.Status),
                BarStatus: app?.Status ?? "missing");
        }
        catch (Exception exception)
        {
            HandleFailure("FlowShellCore session projection refresh failed.", exception);
            return null;
        }
    }

    private async Task<ListAppsResult> EnsureRegistrationBaselineAsync(
        ListAppsResult snapshot,
        CancellationToken cancellationToken)
    {
        var app = snapshot.Apps.FirstOrDefault(candidate => AppIdsMatch(candidate.AppId));
        if (app is null)
        {
            _logger.Warning($"FlowShellCore list_apps does not include '{_appId}'.");
            return snapshot;
        }

        if (!string.Equals(app.InstanceId, _instanceId, StringComparison.Ordinal))
        {
            var registerResponse = await SendCommandAsync(
                "register_app",
                new RegisterAppPayload(
                    _appId,
                    _instanceId,
                    ["bar.ui"],
                    new MetadataPayload((uint)Process.GetCurrentProcess().Id)),
                cancellationToken);

            if (!registerResponse.Ok && !IsDuplicateRegistration(registerResponse.Error))
            {
                _logger.Warning($"FlowShellCore register_app failed: {FormatError(registerResponse.Error)}.");
                return snapshot;
            }

            snapshot = await ListAppsAsync(cancellationToken);
            app = snapshot.Apps.FirstOrDefault(candidate => AppIdsMatch(candidate.AppId));
        }

        if (app is null || !string.Equals(app.InstanceId, _instanceId, StringComparison.Ordinal))
        {
            return snapshot;
        }

        if (!IsReadyStatus(app.Status))
        {
            var readinessResponse = await SendCommandAsync(
                "update_readiness",
                new UpdateReadinessPayload(_appId, _instanceId, true),
                cancellationToken);

            if (!readinessResponse.Ok && !IsDuplicateRegistration(readinessResponse.Error))
            {
                _logger.Warning($"FlowShellCore update_readiness failed: {FormatError(readinessResponse.Error)}.");
                return snapshot;
            }

            snapshot = await ListAppsAsync(cancellationToken);
        }

        return snapshot;
    }

    private async Task<bool> SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        var response = await SendCommandAsync(
            "heartbeat",
            new HeartbeatPayload(_appId, _instanceId),
            cancellationToken);

        if (response.Ok)
        {
            return true;
        }

        if (response.Error is not null)
        {
            _logger.Warning($"FlowShellCore heartbeat failed: {FormatError(response.Error)}.");
        }

        return false;
    }

    private async Task<ListAppsResult> ListAppsAsync(CancellationToken cancellationToken)
    {
        var response = await SendCommandAsync("list_apps", new EmptyPayload(), cancellationToken);
        if (!response.Ok)
        {
            throw new InvalidOperationException($"FlowShellCore list_apps failed: {FormatError(response.Error)}.");
        }

        return _client.Deserialize<ListAppsResult>(response.Result);
    }

    private async Task<JsonPipeResponseEnvelope> SendCommandAsync(
        string messageType,
        object payload,
        CancellationToken cancellationToken)
    {
        return await _client.SendAsync(
            _pipeName,
            new FlowShellCoreRequestEnvelope(
                ProtocolVersion,
                $"core-{messageType}-{Guid.NewGuid():N}",
                messageType,
                payload),
            cancellationToken);
    }

    private bool AppIdsMatch(string? candidate)
    {
        return string.Equals(candidate, _appId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReadyStatus(string? status)
    {
        return string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "degraded", StringComparison.OrdinalIgnoreCase);
    }

    private static FlowShellCoreSessionMode MapSessionMode(string? runtimeMode)
    {
        return runtimeMode?.ToLowerInvariant() switch
        {
            "normal" => FlowShellCoreSessionMode.Normal,
            "degraded" => FlowShellCoreSessionMode.Degraded,
            "safe_mode" => FlowShellCoreSessionMode.SafeMode,
            "shutting_down" => FlowShellCoreSessionMode.ShuttingDown,
            _ => FlowShellCoreSessionMode.Unknown,
        };
    }

    private static bool IsDuplicateRegistration(JsonPipeErrorEnvelope? error)
    {
        return string.Equals(error?.Code, "duplicate_registration", StringComparison.OrdinalIgnoreCase);
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
        _logger.Info("FlowShellCore IPC disabled by FLOWSHELL_BAR_DISABLE_FLOWSHELLCORE_IPC.");
    }

    private static string FormatError(JsonPipeErrorEnvelope? error)
    {
        return error is null
            ? "unknown error"
            : $"{error.Code}: {error.Message}";
    }

    private void HandleFailure(string message, Exception exception)
    {
        SetConnectionState(false, "FlowShellCore IPC unavailable.");
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

    private sealed record MetadataPayload(uint ProcessId);

    private sealed record RegisterAppPayload(
        string AppId,
        string InstanceId,
        string[] Capabilities,
        MetadataPayload Metadata);

    private sealed record UpdateReadinessPayload(
        string AppId,
        string InstanceId,
        bool Ready);

    private sealed record HeartbeatPayload(
        string AppId,
        string InstanceId);

    private sealed record FlowShellCoreRequestEnvelope(
        int ProtocolVersion,
        string RequestId,
        string MessageType,
        object Payload);

    private sealed record ListAppsResult(
        string RuntimeMode,
        IReadOnlyList<FlowShellAppDto> Apps);

    private sealed record FlowShellAppDto(
        string AppId,
        string DisplayName,
        string Status,
        string HealthState,
        string? InstanceId,
        uint? ProcessId);
}
