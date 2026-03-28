namespace FlowShellBar.App.Diagnostics;

public interface IAppLogger : IAsyncDisposable
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}
