using LinqToDB.Mapping;

namespace SmartData.Server.Entities;

[Table("_sys_spans")]
internal class SysSpan
{
    [PrimaryKey, Identity]
    [Column] public int Id { get; set; }
    [Column] public string TraceId { get; set; } = "";
    [Column] public string SpanId { get; set; } = "";
    [Column, Nullable] public string? ParentSpanId { get; set; }
    [Column] public string Name { get; set; } = "";
    [Column, Nullable] public string? Tags { get; set; }
    [Column, Nullable] public string? Attributes { get; set; }
    [Column] public DateTime StartTime { get; set; }
    [Column] public DateTime EndTime { get; set; }
    [Column] public double DurationMs { get; set; }
    [Column] public string Status { get; set; } = "";
    [Column, Nullable] public string? ErrorMessage { get; set; }
    [Column, Nullable] public string? ErrorType { get; set; }
    [Column] public DateTime CreatedAt { get; set; }
}
