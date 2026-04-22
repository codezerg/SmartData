using LinqToDB.Mapping;

namespace SmartData.Server.Entities;

[Table("_sys_metrics")]
internal class SysMetric
{
    [PrimaryKey, Identity]
    [Column] public int Id { get; set; }
    [Column] public string Name { get; set; } = "";
    [Column] public string Type { get; set; } = "";
    [Column, Nullable] public string? Tags { get; set; }
    [Column] public double Value { get; set; }
    [Column, Nullable] public long? Count { get; set; }
    [Column, Nullable] public double? Sum { get; set; }
    [Column, Nullable] public double? Min { get; set; }
    [Column, Nullable] public double? Max { get; set; }
    [Column, Nullable] public double? P50 { get; set; }
    [Column, Nullable] public double? P95 { get; set; }
    [Column, Nullable] public double? P99 { get; set; }
    [Column] public DateTime CreatedAt { get; set; }
}
