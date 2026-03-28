using FlowShellBar.App.Diagnostics;
using FlowShellBar.App.Integrations;

namespace FlowShellBar.App.Application.Actions;

public sealed class BarActionDispatcher : IBarActionDispatcher
{
    private readonly IBarModelMutator _barModelMutator;
    private readonly IAppLogger _logger;

    public BarActionDispatcher(IBarModelMutator barModelMutator, IAppLogger logger)
    {
        _barModelMutator = barModelMutator;
        _logger = logger;

        _logger.Info("Registered action dispatcher: open_launcher, toggle_overview, open_status_panel, switch_workspace, adjust_brightness, adjust_volume.");
    }

    public async Task DispatchAsync(BarActionRequest request, CancellationToken cancellationToken = default)
    {
        switch (request.Kind)
        {
            case BarActionKind.OpenLauncher:
                _logger.Info("Action invoked: open_launcher (stub).");
                break;

            case BarActionKind.ToggleOverview:
                _logger.Info("Action invoked: toggle_overview (stub).");
                break;

            case BarActionKind.OpenStatusPanel:
                _logger.Info("Action invoked: open_status_panel (stub).");
                break;

            case BarActionKind.SwitchWorkspace:
                if (request.WorkspaceId is null)
                {
                    _logger.Warning("switch_workspace was invoked without workspace id.");
                    return;
                }

                _logger.Info($"Action invoked: switch_workspace -> {request.WorkspaceId.Value}.");
                await _barModelMutator.SetActiveWorkspaceAsync(request.WorkspaceId.Value, cancellationToken);
                break;

            case BarActionKind.AdjustBrightness:
                _logger.Info($"Action invoked: adjust_brightness (delta={request.Delta ?? 0}).");
                break;

            case BarActionKind.AdjustVolume:
                _logger.Info($"Action invoked: adjust_volume (delta={request.Delta ?? 0}).");
                break;

            default:
                _logger.Warning($"Unhandled action kind: {request.Kind}.");
                break;
        }
    }
}
