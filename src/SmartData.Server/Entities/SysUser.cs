using System.ComponentModel.DataAnnotations;
using LinqToDB.Mapping;

namespace SmartData.Server.Entities;

[Table("_sys_users")]
internal class SysUser
{
    [PrimaryKey]
    [Column, MaxLength(36)] public string Id { get; set; } = "";
    [Column] public string Username { get; set; } = "";
    [Column] public string PasswordHash { get; set; } = "";
    [Column] public bool IsAdmin { get; set; }
    [Column] public bool IsDisabled { get; set; }
    [Column] public DateTime CreatedAt { get; set; }
    [Column] public DateTime? ModifiedAt { get; set; }
    [Column] public DateTime? LastLoginAt { get; set; }
}
