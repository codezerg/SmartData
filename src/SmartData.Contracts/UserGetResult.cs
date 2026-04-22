namespace SmartData.Contracts;

public class UserGetResult
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public List<string> Permissions { get; set; } = [];
}
