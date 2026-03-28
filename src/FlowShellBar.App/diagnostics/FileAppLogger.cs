using System.Diagnostics;

namespace FlowShellBar.App.Diagnostics;

public sealed class FileAppLogger : IAppLogger
{
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileAppLogger()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowShell",
            "FlowShellBar",
            "logs");

        Directory.CreateDirectory(logDirectory);
        _logFilePath = Path.Combine(logDirectory, "latest.log");
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Warning(string message)
    {
        Write("WARN", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        var text = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Write("ERROR", text);
    }

    public ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";

        Trace.Write(line);
        _ = PersistAsync(line);
    }

    private async Task PersistAsync(string line)
    {
        await _writeLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_logFilePath, line);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
