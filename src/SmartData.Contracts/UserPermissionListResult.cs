namespace SmartData.Contracts;

public class UserPermissionListResult
{
    public string UserId { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
}
