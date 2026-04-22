using System.ComponentModel.DataAnnotations;
using LinqToDB.Mapping;

namespace SmartData.Server.Entities;

[Table("_sys_user_permissions")]
internal class SysUserPermission
{
    [PrimaryKey(1)]
    [Column, MaxLength(36)] public string UserId { get; set; } = "";
    [PrimaryKey(2)]
    [Column, MaxLength(64)] public string PermissionKey { get; set; } = "";
    [Column] public DateTime GrantedAt { get; set; }
}
