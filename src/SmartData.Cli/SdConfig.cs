using System.Text.Json;
using System.Text.Json.Serialization;
using SmartData.Client;

namespace SmartData.Cli;

public class SdConfig
{
    public string? ConnectionString { get; set; }
    public string? Database { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    private static readonly string ConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sd");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public static SdConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new SdConfig();

        var json = File.ReadAllText(ConfigPath);
        var config = JsonSerializer.Deserialize<SdConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new SdConfig();

        if (string.IsNullOrEmpty(config.ConnectionString) && config.Extra != null)
        {
            var builder = new SmartDataConnectionStringBuilder();
            if (config.Extra.TryGetValue("Server", out var server) && server.ValueKind == JsonValueKind.String)
                builder.Server = server.GetString();
            if (config.Extra.TryGetValue("Token", out var token) && token.ValueKind == JsonValueKind.String)
                builder.Token = token.GetString();
            if (config.Extra.TryGetValue("Database", out var db) && db.ValueKind == JsonValueKind.String)
                config.Database = db.GetString();

            if (!string.IsNullOrEmpty(builder.Server) || !string.IsNullOrEmpty(builder.Token))
            {
                config.ConnectionString = builder.ConnectionString;
                config.Extra = null;
                config.Save();
            }
        }

        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        File.WriteAllText(ConfigPath, json);
    }
}
