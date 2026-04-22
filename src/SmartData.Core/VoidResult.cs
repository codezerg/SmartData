namespace SmartData.Core;

/// <summary>
/// Shared return type for procedures that have no payload. Callers read outcome
/// from the relevant entity (e.g. <c>ScheduleRun.Outcome</c>) rather than the return value.
/// </summary>
public sealed class VoidResult
{
    public static readonly VoidResult Instance = new();
}
