using FlowShellBar.App.Application;

namespace FlowShellBar.App.Application.Models;

public sealed record BarModel(
    RuntimeMode RuntimeMode,
    IReadOnlyList<WorkspaceModel> Workspaces,
    string ActiveWindowAppName,
    string ActiveWindowTitle,
    string CurrentTime,
    string CurrentDate,
    StatusClusterModel StatusCluster);
