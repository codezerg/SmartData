using System.ComponentModel.DataAnnotations;
using LinqToDB.Mapping;
using SmartData.Server.Attributes;

namespace SmartData.Server.Entities;

[Table("_sys_sessions")]
[Index("IX_Session_User", nameof(UserId))]
[Index("IX_Session_Expiry", nameof(ExpiresAt))]
internal class SysSession
{
    [PrimaryKey]
    [Column, MaxLength(128)] public string Token { get; set; } = "";

    [Column, MaxLength(36)] public string UserId { get; set; } = "";

    [Column] public DateTime CreatedAt { get; set; }
    [Column] public DateTime LastActivityAt { get; set; }
    [Column] public DateTime ExpiresAt { get; set; }
}
