using SmartData.Contracts;

namespace SmartData.Console.Models;

public class UserEditViewModel
{
    public bool IsNew { get; set; }
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsDisabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public List<string> Permissions { get; set; } = [];
    public List<PermissionGroup> AllPermissions { get; set; } = [];
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PermissionGroup
{
    public string Category { get; set; } = "";
    public List<PermissionEntry> Entries { get; set; } = [];
}

public class PermissionEntry
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Granted { get; set; }
}
