namespace SmartData.Contracts;

public class UserListItem
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
