namespace SmartData.Server.Metrics;

/// <summary>
/// AsyncLocal context for passing the current procedure name to child components
/// (e.g., SqlTrackingInterceptor) without coupling them to the procedure framework.
/// </summary>
internal static class MetricsContext
{
    private static readonly AsyncLocal<string?> _currentProcedure = new();

    /// <summary>
    /// The name of the stored procedure currently executing on this async flow.
    /// Set by ProcedureExecutor, read by SqlTrackingInterceptor.
    /// </summary>
    public static string? CurrentProcedure
    {
        get => _currentProcedure.Value;
        set => _currentProcedure.Value = value;
    }
}
