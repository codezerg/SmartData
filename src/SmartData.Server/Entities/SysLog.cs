using LinqToDB.Mapping;

namespace SmartData.Server.Entities;

[Table("_sys_logs")]
internal class SysLog
{
    [PrimaryKey, Identity]
    [Column] public int Id { get; set; }
    [Column] public string Type { get; set; } = "";
    [Column, Nullable] public string? ProcedureName { get; set; }
    [Column] public string Message { get; set; } = "";
    [Column] public DateTime CreatedAt { get; set; }
}
