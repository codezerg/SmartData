namespace SmartData.Contracts;

public class SessionResult
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public bool IsAdmin { get; set; }
    public List<string> Permissions { get; set; } = [];
}
