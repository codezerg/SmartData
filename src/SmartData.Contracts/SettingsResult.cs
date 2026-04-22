namespace SmartData.Contracts;

public class SettingsResult
{
    public List<SettingsResultItem> Items { get; set; } = [];
}

public class SettingsResultItem
{
    public string Key { get; set; } = "";
    public string Section { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Value { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsReadOnly { get; set; }
    public bool RestartRequired { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
