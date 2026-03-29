namespace FlowShellBar.App.Application.Models;

public sealed record ResourceMetricsModel(
    int MemoryUsagePercent,
    int CpuUsagePercent,
    int TemperatureCelsius);
