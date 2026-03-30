using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

using FlowShellBar.App.Diagnostics;

using LibreHardwareMonitor.Hardware;

namespace FlowShellBar.App.Integrations;

public sealed class WindowsSystemMetricsAdapter : ISystemMetricsAdapter
{
    private static readonly TimeSpan SensorRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private readonly Computer _hardwareMonitorComputer;

    private SystemMetricsSnapshot _lastSnapshot = new(
        IsNetworkAvailable: false,
        IsAudioAvailable: true,
        MemoryUsagePercent: 0,
        MemoryUsedBytes: 0,
        MemoryAvailableBytes: 0,
        MemoryTotalBytes: 0,
        CpuUsagePercent: 0,
        GpuUsagePercent: null,
        CpuTemperatureCelsius: null,
        GpuTemperatureCelsius: null);

    private ulong _previousIdleTime;
    private ulong _previousKernelTime;
    private ulong _previousUserTime;
    private bool _hasCpuSample;
    private int _lastCpuUsagePercent;
    private DateTimeOffset _lastSensorReadUtc = DateTimeOffset.MinValue;
    private HardwareSensorSnapshot _cachedHardwareSensors = HardwareSensorSnapshot.Empty;
    private bool _hardwareMonitorInitialized;
    private bool _loggedUnavailableSensors;
    private bool _loggedDirectHardwareMonitorFailure;
    private string? _loggedSensorSource;

    public WindowsSystemMetricsAdapter(IAppLogger logger)
    {
        _logger = logger;
        _hardwareMonitorComputer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
        };

        TryInitializeHardwareMonitor();
        PrimeCpuSampling();
        _logger.Info("Windows system metrics adapter initialized.");
    }

    public SystemMetricsSnapshot ReadSnapshot()
    {
        try
        {
            var memory = ReadMemoryStatus();
            var isNetworkAvailable = NetworkInterface.GetIsNetworkAvailable();
            int cpuUsagePercent;
            HardwareSensorSnapshot hardwareSensors;

            lock (_sync)
            {
                cpuUsagePercent = ReadCpuUsagePercentLocked();
                hardwareSensors = ReadHardwareSensorsLocked();
            }

            _lastSnapshot = new SystemMetricsSnapshot(
                IsNetworkAvailable: isNetworkAvailable,
                IsAudioAvailable: true,
                MemoryUsagePercent: memory.MemoryLoadPercent,
                MemoryUsedBytes: memory.UsedBytes,
                MemoryAvailableBytes: memory.AvailableBytes,
                MemoryTotalBytes: memory.TotalBytes,
                CpuUsagePercent: cpuUsagePercent,
                GpuUsagePercent: hardwareSensors.GpuUsagePercent,
                CpuTemperatureCelsius: hardwareSensors.CpuTemperatureCelsius,
                GpuTemperatureCelsius: hardwareSensors.GpuTemperatureCelsius);

            return _lastSnapshot;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to sample live system metrics. Falling back to last snapshot. {ex.Message}");
            return _lastSnapshot;
        }
    }

    private void TryInitializeHardwareMonitor()
    {
        try
        {
            _hardwareMonitorComputer.Open();
            _hardwareMonitorInitialized = true;
            _logger.Info("LibreHardwareMonitorLib direct provider initialized.");
        }
        catch (Exception ex)
        {
            _hardwareMonitorInitialized = false;
            _logger.Warning($"LibreHardwareMonitorLib direct provider initialization failed. {ex.Message}");
        }
    }

    private void PrimeCpuSampling()
    {
        lock (_sync)
        {
            if (!TryReadSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            {
                _logger.Warning("Unable to prime CPU metrics sampling. CPU usage will remain at the last known value.");
                return;
            }

            _previousIdleTime = idleTime;
            _previousKernelTime = kernelTime;
            _previousUserTime = userTime;
            _hasCpuSample = true;
        }
    }

    private int ReadCpuUsagePercentLocked()
    {
        if (!TryReadSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            return _lastCpuUsagePercent;
        }

        if (!_hasCpuSample)
        {
            _previousIdleTime = idleTime;
            _previousKernelTime = kernelTime;
            _previousUserTime = userTime;
            _hasCpuSample = true;
            return _lastCpuUsagePercent;
        }

        var idleDelta = idleTime - _previousIdleTime;
        var kernelDelta = kernelTime - _previousKernelTime;
        var userDelta = userTime - _previousUserTime;
        var totalDelta = kernelDelta + userDelta;

        if (totalDelta > 0)
        {
            var usage = 1d - (idleDelta / (double)totalDelta);
            _lastCpuUsagePercent = (int)Math.Clamp(Math.Round(usage * 100d), 0d, 100d);
        }

        _previousIdleTime = idleTime;
        _previousKernelTime = kernelTime;
        _previousUserTime = userTime;

        return _lastCpuUsagePercent;
    }

    private HardwareSensorSnapshot ReadHardwareSensorsLocked()
    {
        var now = DateTimeOffset.UtcNow;
        if ((now - _lastSensorReadUtc) < SensorRefreshInterval)
        {
            return _cachedHardwareSensors;
        }

        _lastSensorReadUtc = now;

        var snapshot = HardwareSensorSnapshot.Empty;

        if (TryReadDirectHardwareMonitorSnapshot(out var directSnapshot))
        {
            snapshot = MergeSnapshots(snapshot, directSnapshot);
        }

        if (TryReadHardwareMonitorSensorSnapshot("root\\LibreHardwareMonitor", out var libreWmiSnapshot)
            || TryReadHardwareMonitorSensorSnapshot("root\\OpenHardwareMonitor", out libreWmiSnapshot))
        {
            snapshot = MergeSnapshots(snapshot, libreWmiSnapshot);
        }

        if (snapshot.CpuTemperatureCelsius is null && TryReadAcpiTemperature(out var cpuTemperatureCelsius, out var acpiSource))
        {
            snapshot = MergeSnapshots(snapshot, new HardwareSensorSnapshot(
                CpuTemperatureCelsius: cpuTemperatureCelsius,
                GpuTemperatureCelsius: null,
                GpuUsagePercent: null,
                Source: acpiSource));
        }

        if (snapshot.CpuTemperatureCelsius is null && TryReadThermalZoneTemperature(out cpuTemperatureCelsius, out var thermalZoneSource))
        {
            snapshot = MergeSnapshots(snapshot, new HardwareSensorSnapshot(
                CpuTemperatureCelsius: cpuTemperatureCelsius,
                GpuTemperatureCelsius: null,
                GpuUsagePercent: null,
                Source: thermalZoneSource));
        }

        if (snapshot.GpuUsagePercent is null && TryReadGpuEngineUsage(out var gpuUsagePercent))
        {
            snapshot = MergeSnapshots(snapshot, new HardwareSensorSnapshot(
                CpuTemperatureCelsius: null,
                GpuTemperatureCelsius: null,
                GpuUsagePercent: gpuUsagePercent,
                Source: "GPU Engine WMI"));
        }

        if (snapshot.HasAnyData)
        {
            _cachedHardwareSensors = snapshot;
            LogResolvedSensorSourceIfChanged(snapshot.Source);
            _loggedUnavailableSensors = false;
            return _cachedHardwareSensors;
        }

        _cachedHardwareSensors = HardwareSensorSnapshot.Empty;

        if (!_loggedUnavailableSensors)
        {
            _logger.Info("No local hardware sensor provider is available. GPU usage and temperatures will be reported as unavailable.");
            _loggedUnavailableSensors = true;
        }

        return _cachedHardwareSensors;
    }

    private bool TryReadDirectHardwareMonitorSnapshot(out HardwareSensorSnapshot snapshot)
    {
        if (!_hardwareMonitorInitialized)
        {
            snapshot = HardwareSensorSnapshot.Empty;
            return false;
        }

        try
        {
            SensorReading? cpuTemperature = null;
            var gpuAdapters = new Dictionary<string, GpuAdapterBuilder>(StringComparer.Ordinal);

            foreach (var hardware in _hardwareMonitorComputer.Hardware)
            {
                UpdateHardwareSnapshot(hardware, ref cpuTemperature, gpuAdapters, currentGpuAdapter: null);
            }

            var selectedGpuAdapter = SelectPreferredGpuAdapter(gpuAdapters.Values);

            if (cpuTemperature is null
                && selectedGpuAdapter?.Temperature is null
                && selectedGpuAdapter?.Usage is null)
            {
                snapshot = HardwareSensorSnapshot.Empty;
                return false;
            }

            snapshot = new HardwareSensorSnapshot(
                CpuTemperatureCelsius: cpuTemperature?.Value,
                GpuTemperatureCelsius: selectedGpuAdapter?.Temperature?.Value,
                GpuUsagePercent: selectedGpuAdapter?.Usage?.Value,
                Source: selectedGpuAdapter is null
                    ? "LibreHardwareMonitorLib"
                    : $"LibreHardwareMonitorLib:{selectedGpuAdapter.Name}");
            return true;
        }
        catch (Exception ex)
        {
            if (!_loggedDirectHardwareMonitorFailure)
            {
                _logger.Warning($"LibreHardwareMonitorLib direct sampling failed. {ex.Message}");
                _loggedDirectHardwareMonitorFailure = true;
            }

            snapshot = HardwareSensorSnapshot.Empty;
            return false;
        }
    }

    private static void UpdateHardwareSnapshot(
        IHardware hardware,
        ref SensorReading? cpuTemperature,
        IDictionary<string, GpuAdapterBuilder> gpuAdapters,
        GpuAdapterBuilder? currentGpuAdapter)
    {
        hardware.Update();

        var effectiveGpuAdapter = currentGpuAdapter;
        if (IsGpuHardware(hardware.HardwareType))
        {
            var adapterKey = hardware.Identifier.ToString();
            if (!gpuAdapters.TryGetValue(adapterKey, out var adapter))
            {
                adapter = new GpuAdapterBuilder(adapterKey, hardware.Name, hardware.HardwareType);
                gpuAdapters.Add(adapterKey, adapter);
            }

            effectiveGpuAdapter = adapter;
        }

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is not float value)
            {
                continue;
            }

            switch (sensor.SensorType)
            {
                case SensorType.Temperature when TryClassifyDirectTemperatureSensor(hardware, sensor, out var sensorKind, out var priority):
                {
                    var reading = new SensorReading((int)Math.Round(value), sensor.Name, priority);
                    if (sensorKind == HardwareSensorKind.Cpu)
                    {
                        cpuTemperature = SelectPreferredReading(cpuTemperature, reading);
                    }
                    else if (effectiveGpuAdapter is not null)
                    {
                        effectiveGpuAdapter.Temperature = SelectPreferredReading(effectiveGpuAdapter.Temperature, reading);
                    }

                    break;
                }

                case SensorType.Load when effectiveGpuAdapter is not null && TryClassifyDirectGpuLoadSensor(hardware, sensor, out var loadPriority):
                {
                    var reading = new SensorReading(
                        Value: (int)Math.Clamp(Math.Round(value), 0d, 100d),
                        Name: sensor.Name,
                        Priority: loadPriority);
                    effectiveGpuAdapter.Usage = SelectPreferredLoadReading(effectiveGpuAdapter.Usage, reading);
                    break;
                }
            }
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            UpdateHardwareSnapshot(subHardware, ref cpuTemperature, gpuAdapters, effectiveGpuAdapter);
        }
    }

    private static bool TryClassifyDirectTemperatureSensor(
        IHardware hardware,
        ISensor sensor,
        out HardwareSensorKind kind,
        out int priority)
    {
        if (IsCpuHardware(hardware.HardwareType))
        {
            kind = HardwareSensorKind.Cpu;
            priority = GetCpuTemperaturePriority(sensor.Name.ToLowerInvariant());
            return true;
        }

        if (IsGpuHardware(hardware.HardwareType))
        {
            kind = HardwareSensorKind.Gpu;
            priority = GetGpuTemperaturePriority(sensor.Name.ToLowerInvariant());
            return true;
        }

        return TryClassifyTemperatureSensor(sensor.Name, out kind, out priority);
    }

    private static bool TryClassifyDirectGpuLoadSensor(IHardware hardware, ISensor sensor, out int priority)
    {
        if (!IsGpuHardware(hardware.HardwareType))
        {
            priority = 0;
            return false;
        }

        priority = GetGpuLoadPriority(sensor.Name.ToLowerInvariant());
        return priority > 0;
    }

    private static bool IsCpuHardware(HardwareType hardwareType)
    {
        return hardwareType == HardwareType.Cpu;
    }

    private static bool IsGpuHardware(HardwareType hardwareType)
    {
        return hardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia;
    }

    private static GpuAdapterBuilder? SelectPreferredGpuAdapter(IEnumerable<GpuAdapterBuilder> gpuAdapters)
    {
        var candidates = gpuAdapters
            .Where(adapter => adapter.Temperature is not null || adapter.Usage is not null)
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        var discreteCandidates = candidates
            .Where(adapter => adapter.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)
            .ToArray();

        var selectionPool = discreteCandidates.Length > 0
            ? discreteCandidates
            : candidates;

        return selectionPool
            .OrderByDescending(adapter => adapter.Usage?.Value ?? -1)
            .ThenByDescending(adapter => adapter.Temperature?.Value ?? -1)
            .ThenByDescending(adapter => GetGpuHardwarePriority(adapter.HardwareType))
            .First();
    }

    private static HardwareSensorSnapshot MergeSnapshots(HardwareSensorSnapshot current, HardwareSensorSnapshot incoming)
    {
        if (!incoming.HasAnyData)
        {
            return current;
        }

        return current with
        {
            CpuTemperatureCelsius = current.CpuTemperatureCelsius ?? incoming.CpuTemperatureCelsius,
            GpuTemperatureCelsius = current.GpuTemperatureCelsius ?? incoming.GpuTemperatureCelsius,
            GpuUsagePercent = current.GpuUsagePercent ?? incoming.GpuUsagePercent,
            Source = AppendSource(current.Source, incoming.Source),
        };
    }

    private static string AppendSource(string currentSource, string incomingSource)
    {
        if (string.IsNullOrWhiteSpace(incomingSource))
        {
            return currentSource;
        }

        if (string.IsNullOrWhiteSpace(currentSource))
        {
            return incomingSource;
        }

        return currentSource.Contains(incomingSource, StringComparison.Ordinal)
            ? currentSource
            : $"{currentSource}; {incomingSource}";
    }

    private void LogResolvedSensorSourceIfChanged(string source)
    {
        if (string.Equals(_loggedSensorSource, source, StringComparison.Ordinal))
        {
            return;
        }

        _logger.Info($"Hardware sensor source resolved: {source}.");
        _loggedSensorSource = source;
    }

    private static MemoryStatusSnapshot ReadMemoryStatus()
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>(),
        };

        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException("GlobalMemoryStatusEx returned an error.");
        }

        return new MemoryStatusSnapshot(
            MemoryLoadPercent: (int)Math.Clamp(status.MemoryLoad, 0u, 100u),
            UsedBytes: status.TotalPhys - status.AvailPhys,
            AvailableBytes: status.AvailPhys,
            TotalBytes: status.TotalPhys);
    }

    private static bool TryReadSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime)
    {
        if (!GetSystemTimes(out var idleFileTime, out var kernelFileTime, out var userFileTime))
        {
            idleTime = 0;
            kernelTime = 0;
            userTime = 0;
            return false;
        }

        idleTime = ToUInt64(idleFileTime);
        kernelTime = ToUInt64(kernelFileTime);
        userTime = ToUInt64(userFileTime);
        return true;
    }

    private static ulong ToUInt64(FileTime fileTime)
    {
        return ((ulong)fileTime.HighDateTime << 32) | fileTime.LowDateTime;
    }

    private static bool TryReadHardwareMonitorSensorSnapshot(
        string managementNamespace,
        out HardwareSensorSnapshot snapshot)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                managementNamespace,
                "SELECT Name, Value, SensorType FROM Sensor WHERE SensorType = 'Temperature' OR SensorType = 'Load'");
            using var results = searcher.Get();

            SensorReading? cpuTemperature = null;
            SensorReading? gpuTemperature = null;
            SensorReading? gpuUsage = null;

            foreach (var sensor in results.Cast<ManagementObject>())
            {
                var sensorName = sensor["Name"]?.ToString();
                var sensorType = sensor["SensorType"]?.ToString();

                if (string.IsNullOrWhiteSpace(sensorName)
                    || string.IsNullOrWhiteSpace(sensorType)
                    || !TryConvertToDouble(sensor["Value"], out var rawValue))
                {
                    continue;
                }

                if (string.Equals(sensorType, "Temperature", StringComparison.OrdinalIgnoreCase))
                {
                    var temperatureCelsius = (int)Math.Round(rawValue);
                    if (temperatureCelsius is < 0 or > 150
                        || !TryClassifyTemperatureSensor(sensorName, out var kind, out var priority))
                    {
                        continue;
                    }

                    var reading = new SensorReading(temperatureCelsius, sensorName, priority);
                    if (kind == HardwareSensorKind.Cpu)
                    {
                        cpuTemperature = SelectPreferredReading(cpuTemperature, reading);
                    }
                    else
                    {
                        gpuTemperature = SelectPreferredReading(gpuTemperature, reading);
                    }

                    continue;
                }

                if (!string.Equals(sensorType, "Load", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var usagePercent = (int)Math.Clamp(Math.Round(rawValue), 0d, 100d);
                if (!TryClassifyGpuLoadSensor(sensorName, out var usagePriority))
                {
                    continue;
                }

                gpuUsage = SelectPreferredLoadReading(gpuUsage, new SensorReading(usagePercent, sensorName, usagePriority));
            }

            if (cpuTemperature is null && gpuTemperature is null && gpuUsage is null)
            {
                snapshot = HardwareSensorSnapshot.Empty;
                return false;
            }

            snapshot = new HardwareSensorSnapshot(
                CpuTemperatureCelsius: cpuTemperature?.Value,
                GpuTemperatureCelsius: gpuTemperature?.Value,
                GpuUsagePercent: gpuUsage?.Value,
                Source: SimplifySensorSource(managementNamespace));
            return true;
        }
        catch (ManagementException)
        {
        }
        catch (COMException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        snapshot = HardwareSensorSnapshot.Empty;
        return false;
    }

    private static SensorReading? SelectPreferredReading(SensorReading? current, SensorReading candidate)
    {
        if (current is null)
        {
            return candidate;
        }

        if (candidate.Priority > current.Value.Priority)
        {
            return candidate;
        }

        if (candidate.Priority == current.Value.Priority && candidate.Value > current.Value.Value)
        {
            return candidate;
        }

        return current;
    }

    private static SensorReading? SelectPreferredLoadReading(SensorReading? current, SensorReading candidate)
    {
        if (current is null)
        {
            return candidate;
        }

        if (candidate.Value > current.Value.Value)
        {
            return candidate;
        }

        if (candidate.Value == current.Value.Value && candidate.Priority > current.Value.Priority)
        {
            return candidate;
        }

        return current;
    }

    private static bool TryClassifyTemperatureSensor(
        string sensorName,
        out HardwareSensorKind kind,
        out int priority)
    {
        var normalized = sensorName.ToLowerInvariant();

        if (normalized.Contains("gpu", StringComparison.Ordinal)
            || normalized.Contains("gfx", StringComparison.Ordinal)
            || normalized.Contains("graphics", StringComparison.Ordinal)
            || normalized.Contains("amdgpu", StringComparison.Ordinal)
            || normalized.Contains("radeon", StringComparison.Ordinal)
            || normalized.Contains("nvidia", StringComparison.Ordinal)
            || normalized.Contains("hot spot", StringComparison.Ordinal)
            || normalized.Contains("hotspot", StringComparison.Ordinal)
            || normalized.Contains("memory junction", StringComparison.Ordinal))
        {
            kind = HardwareSensorKind.Gpu;
            priority = GetGpuTemperaturePriority(normalized);
            return true;
        }

        if (normalized.Contains("cpu package", StringComparison.Ordinal)
            || normalized.Contains("tctl", StringComparison.Ordinal)
            || normalized.Contains("tdie", StringComparison.Ordinal)
            || normalized.Contains("ccd", StringComparison.Ordinal)
            || normalized.Contains("cpu", StringComparison.Ordinal)
            || normalized.Contains("core", StringComparison.Ordinal)
            || normalized.Contains("package", StringComparison.Ordinal)
            || normalized.Contains("processor", StringComparison.Ordinal)
            || normalized.Contains("soc", StringComparison.Ordinal))
        {
            kind = HardwareSensorKind.Cpu;
            priority = GetCpuTemperaturePriority(normalized);
            return true;
        }

        kind = default;
        priority = 0;
        return false;
    }

    private static int GetCpuTemperaturePriority(string normalizedSource)
    {
        if (normalizedSource.Contains("cpu package", StringComparison.Ordinal))
        {
            return 500;
        }

        if (normalizedSource.Contains("tctl", StringComparison.Ordinal)
            || normalizedSource.Contains("tdie", StringComparison.Ordinal))
        {
            return 450;
        }

        if (normalizedSource.Contains("ccd", StringComparison.Ordinal))
        {
            return 425;
        }

        if (normalizedSource.Contains("cpu", StringComparison.Ordinal))
        {
            return 400;
        }

        if (normalizedSource.Contains("core", StringComparison.Ordinal))
        {
            return 350;
        }

        if (normalizedSource.Contains("package", StringComparison.Ordinal))
        {
            return 300;
        }

        return 200;
    }

    private static int GetGpuTemperaturePriority(string normalizedSource)
    {
        if (normalizedSource.Contains("gpu core", StringComparison.Ordinal))
        {
            return 400;
        }

        if (normalizedSource.Contains("gpu", StringComparison.Ordinal)
            || normalizedSource.Contains("gfx", StringComparison.Ordinal))
        {
            return 350;
        }

        if (normalizedSource.Contains("graphics", StringComparison.Ordinal))
        {
            return 300;
        }

        if (normalizedSource.Contains("hot spot", StringComparison.Ordinal)
            || normalizedSource.Contains("hotspot", StringComparison.Ordinal))
        {
            return 250;
        }

        if (normalizedSource.Contains("memory junction", StringComparison.Ordinal))
        {
            return 225;
        }

        return 200;
    }

    private static bool TryClassifyGpuLoadSensor(string sensorName, out int priority)
    {
        priority = GetGpuLoadPriority(sensorName.ToLowerInvariant());
        return priority > 0;
    }

    private static int GetGpuLoadPriority(string normalizedSource)
    {
        if (normalizedSource.Contains("gpu core", StringComparison.Ordinal))
        {
            return 450;
        }

        if (normalizedSource.Contains("d3d 3d", StringComparison.Ordinal)
            || normalizedSource.Contains("gpu", StringComparison.Ordinal)
            || normalizedSource.Contains("gfx", StringComparison.Ordinal))
        {
            return 400;
        }

        if (normalizedSource.Contains("graphics", StringComparison.Ordinal)
            || normalizedSource.Contains("3d", StringComparison.Ordinal)
            || normalizedSource.Contains("video engine", StringComparison.Ordinal))
        {
            return 300;
        }

        return 0;
    }

    private static int GetGpuHardwarePriority(HardwareType hardwareType)
    {
        return hardwareType switch
        {
            HardwareType.GpuNvidia => 300,
            HardwareType.GpuAmd => 200,
            HardwareType.GpuIntel => 100,
            _ => 0,
        };
    }

    private static string SimplifySensorSource(string managementNamespace)
    {
        var lastSlashIndex = managementNamespace.LastIndexOf('\\');
        return lastSlashIndex >= 0 && lastSlashIndex < managementNamespace.Length - 1
            ? managementNamespace[(lastSlashIndex + 1)..]
            : managementNamespace;
    }

    private static bool TryReadGpuEngineUsage(out int usagePercent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            using var results = searcher.Get();

            var maxUtilization = results
                .Cast<ManagementObject>()
                .Select(obj => new
                {
                    Name = obj["Name"]?.ToString(),
                    Utilization = TryReadInt(obj["UtilizationPercentage"], out var value) ? value : -1,
                })
                .Where(sample =>
                    sample.Utilization >= 0
                    && !string.IsNullOrWhiteSpace(sample.Name)
                    && sample.Name.Contains("phys_", StringComparison.OrdinalIgnoreCase))
                .Select(sample => sample.Utilization)
                .DefaultIfEmpty(-1)
                .Max();

            if (maxUtilization >= 0)
            {
                usagePercent = maxUtilization;
                return true;
            }
        }
        catch (ManagementException)
        {
        }
        catch (COMException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        usagePercent = 0;
        return false;
    }

    private static bool TryReadAcpiTemperature(out int temperatureCelsius, out string source)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\WMI",
                "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            using var results = searcher.Get();

            var bestReading = results
                .Cast<ManagementObject>()
                .Select(CreateAcpiTemperatureReading)
                .Where(reading => reading is not null)
                .Select(reading => reading!.Value)
                .OrderByDescending(reading => reading.Priority)
                .ThenByDescending(reading => reading.Value)
                .FirstOrDefault();

            if (bestReading.Name is not null)
            {
                temperatureCelsius = bestReading.Value;
                source = "MSAcpi_ThermalZoneTemperature";
                return true;
            }
        }
        catch (ManagementException)
        {
        }
        catch (COMException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        temperatureCelsius = 0;
        source = string.Empty;
        return false;
    }

    private static bool TryReadThermalZoneTemperature(out int temperatureCelsius, out string source)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT Name, Temperature, HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
            using var results = searcher.Get();

            var bestReading = results
                .Cast<ManagementObject>()
                .Select(CreateThermalZoneTemperatureReading)
                .Where(reading => reading is not null)
                .Select(reading => reading!.Value)
                .OrderByDescending(reading => reading.Value)
                .FirstOrDefault();

            if (bestReading.Name is not null)
            {
                temperatureCelsius = bestReading.Value;
                source = $"ThermalZoneInformation:{bestReading.Name}";
                return true;
            }
        }
        catch (ManagementException)
        {
        }
        catch (COMException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        temperatureCelsius = 0;
        source = string.Empty;
        return false;
    }

    private static SensorReading? CreateAcpiTemperatureReading(ManagementObject zone)
    {
        if (!TryConvertToDouble(zone["CurrentTemperature"], out var rawTemperature))
        {
            return null;
        }

        var temperatureCelsius = (int)Math.Round((rawTemperature / 10d) - 273.15d);
        if (temperatureCelsius is < 0 or > 150)
        {
            return null;
        }

        var source = zone["InstanceName"]?.ToString();
        if (string.IsNullOrWhiteSpace(source))
        {
            source = "ACPI thermal zone";
        }

        return new SensorReading(temperatureCelsius, source, Priority: 10);
    }

    private static SensorReading? CreateThermalZoneTemperatureReading(ManagementObject zone)
    {
        var zoneName = zone["Name"]?.ToString();
        if (string.IsNullOrWhiteSpace(zoneName))
        {
            zoneName = "thermal-zone";
        }

        if (TryConvertToDouble(zone["HighPrecisionTemperature"], out var highPrecisionTemperature) && highPrecisionTemperature > 0)
        {
            var roundedTemperature = (int)Math.Round(highPrecisionTemperature / 100d);
            if (roundedTemperature is >= 0 and <= 150)
            {
                return new SensorReading(roundedTemperature, zoneName, Priority: 5);
            }
        }

        if (TryConvertToDouble(zone["Temperature"], out var temperature) && temperature > 0)
        {
            var roundedTemperature = (int)Math.Round(temperature / 10d);
            if (roundedTemperature is >= 0 and <= 150)
            {
                return new SensorReading(roundedTemperature, zoneName, Priority: 4);
            }
        }

        return null;
    }

    private static bool TryReadInt(object? value, out int result)
    {
        try
        {
            if (value is null)
            {
                result = 0;
                return false;
            }

            result = Convert.ToInt32(value);
            return true;
        }
        catch (FormatException)
        {
        }
        catch (InvalidCastException)
        {
        }
        catch (OverflowException)
        {
        }

        result = 0;
        return false;
    }

    private static bool TryConvertToDouble(object? value, out double result)
    {
        try
        {
            if (value is null)
            {
                result = 0;
                return false;
            }

            result = Convert.ToDouble(value);
            return true;
        }
        catch (FormatException)
        {
        }
        catch (InvalidCastException)
        {
        }
        catch (OverflowException)
        {
        }

        result = 0;
        return false;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    private enum HardwareSensorKind
    {
        Cpu,
        Gpu,
    }

    private readonly record struct MemoryStatusSnapshot(
        int MemoryLoadPercent,
        ulong UsedBytes,
        ulong AvailableBytes,
        ulong TotalBytes);

    private readonly record struct HardwareSensorSnapshot(
        int? CpuTemperatureCelsius,
        int? GpuTemperatureCelsius,
        int? GpuUsagePercent,
        string Source)
    {
        public bool HasAnyData =>
            CpuTemperatureCelsius is not null
            || GpuTemperatureCelsius is not null
            || GpuUsagePercent is not null;

        public static HardwareSensorSnapshot Empty => new(
            CpuTemperatureCelsius: null,
            GpuTemperatureCelsius: null,
            GpuUsagePercent: null,
            Source: string.Empty);
    }

    private readonly record struct SensorReading(
        int Value,
        string Name,
        int Priority);

    private sealed class GpuAdapterBuilder
    {
        public GpuAdapterBuilder(string key, string name, HardwareType hardwareType)
        {
            Key = key;
            Name = name;
            HardwareType = hardwareType;
        }

        public string Key { get; }

        public string Name { get; }

        public HardwareType HardwareType { get; }

        public SensorReading? Temperature { get; set; }

        public SensorReading? Usage { get; set; }
    }
}
