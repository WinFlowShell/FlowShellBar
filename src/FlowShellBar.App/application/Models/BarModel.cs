using FlowShellBar.App.Application;

namespace FlowShellBar.App.Application.Models;

public sealed record BarModel(
    RuntimeMode RuntimeMode,
    BarConnectionState ConnectionState,
    BarProjectionSourcesModel Sources,
    BarSurfacePlacementModel SurfacePlacement,
    IReadOnlyList<WorkspaceModel> Workspaces,
    string ActiveWindowAppName,
    string ActiveWindowTitle,
    string ActiveWorkspaceLabel,
    ResourceMetricsModel ResourceMetrics,
    string CurrentTime,
    string CurrentDate,
    StatusClusterModel StatusCluster);
