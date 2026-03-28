using FlowShellBar.App.Application.Models;

namespace FlowShellBar.App.Integrations;

public interface IBarModelProvider
{
    event EventHandler<BarModel>? ModelChanged;

    BarModel GetCurrentModel();

    Task StartAsync(CancellationToken cancellationToken = default);
}
