using GameControlMapper.Win32;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public sealed class DpiAwarenessDiagnostics
{
    private readonly ILogger<DpiAwarenessDiagnostics> _logger;

    public DpiAwarenessDiagnostics(ILogger<DpiAwarenessDiagnostics> logger)
    {
        _logger = logger;
    }

    public bool LogCurrentContext()
    {
        var context = NativeMethods.GetThreadDpiAwarenessContext();
        var isPerMonitorV2 = NativeMethods.AreDpiAwarenessContextsEqual(context, NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        _logger.LogInformation("Thread DPI awareness context: 0x{Context:X}; PerMonitorV2={IsPerMonitorV2}", context.ToInt64(), isPerMonitorV2);
        if (!isPerMonitorV2) _logger.LogWarning("Expected a PerMonitorV2 DPI awareness context for physical window geometry.");
        return isPerMonitorV2;
    }
}
