using LinqToDB.Mapping;

namespace SmartData.Server.Entities;

[Table("_sys_exceptions")]
internal class SysException
{
    [PrimaryKey, Identity]
    [Column] public int Id { get; set; }
    [Column] public string ExceptionType { get; set; } = "";
    [Column] public string Message { get; set; } = "";
    [Column] public string StackTrace { get; set; } = "";
    [Column, Nullable] public string? Procedure { get; set; }
    [Column, Nullable] public string? Database { get; set; }
    [Column, Nullable] public string? User { get; set; }
    [Column, Nullable] public string? TraceId { get; set; }
    [Column, Nullable] public string? SpanId { get; set; }
    [Column, Nullable] public string? Properties { get; set; }
    [Column] public DateTime Timestamp { get; set; }
}
