using System.ComponentModel.DataAnnotations;
using LinqToDB.Mapping;

namespace SmartData.Server.Entities;

[Table("_sys_settings")]
internal class SysSetting
{
    [PrimaryKey]
    [Column, MaxLength(128)] public string Key { get; set; } = "";
    [Column] public string Value { get; set; } = "";
    [Column] public DateTime ModifiedAt { get; set; }
}
