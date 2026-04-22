using SmartData.Contracts;

namespace SmartData.Console.Models;

public class LayoutViewModel
{
    public string CurrentPath { get; set; } = "/";
    public string? CurrentDb { get; set; }
    public List<DatabaseListItem> Databases { get; set; } = [];
    public string? Username { get; set; }
}
