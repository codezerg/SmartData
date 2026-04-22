using System.Text.Json;

namespace SmartData.Cli;

public class SdConfig
{
    public string? Server { get; set; }
    public string? Token { get; set; }
    public string? Database { get; set; }

    private static readonly string ConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sd");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public static SdConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new SdConfig();

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<SdConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new SdConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
