namespace FlowShellBar.App.Application.Actions;

public interface IBarActionDispatcher
{
    Task DispatchAsync(BarActionRequest request, CancellationToken cancellationToken = default);
}
