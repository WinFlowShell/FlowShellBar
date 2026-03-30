namespace FlowShellBar.App.Integrations;

public sealed record SystemMetricsSnapshot(
    bool IsNetworkAvailable,
    bool IsAudioAvailable,
    int MemoryUsagePercent,
    ulong MemoryUsedBytes,
    ulong MemoryAvailableBytes,
    ulong MemoryTotalBytes,
    int CpuUsagePercent,
    int? GpuUsagePercent,
    int? CpuTemperatureCelsius,
    int? GpuTemperatureCelsius);
