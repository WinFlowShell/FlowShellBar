namespace FlowShellBar.App.Application.Actions;

public sealed record BarActionRequest(
    BarActionKind Kind,
    int? WorkspaceId = null,
    double? Delta = null);
