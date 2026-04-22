namespace SmartData.Contracts;

public class ScheduleStatsResult
{
    public int SchedulesTotal { get; set; }
    public int SchedulesEnabled { get; set; }
    public int CurrentlyRunning { get; set; }
    public int PendingRetries { get; set; }

    public int Last24hSucceeded { get; set; }
    public int Last24hFailed { get; set; }
    public int Last24hCancelled { get; set; }

    public List<ScheduleProcedureStat> PerProcedure { get; set; } = new();
}

public class ScheduleProcedureStat
{
    public string ProcedureName { get; set; } = "";
    public double AvgDurationMs { get; set; }
    public int Runs { get; set; }
}
