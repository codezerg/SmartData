using SmartData.Server;

namespace SmartData.Console.Models;

public class SettingsViewModel
{
    public List<SettingsGroupViewModel> Groups { get; set; } = [];
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SettingsGroupViewModel
{
    public string Section { get; set; } = "";
    public List<SettingEntry> Items { get; set; } = [];
}
