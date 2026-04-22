using SmartData.Core.BinarySerialization;

namespace SmartData.Core.Api;

[BinarySerializable]
public class CommandRequest
{
    [BinaryProperty("command", Order = 1)]
    public string Command { get; set; } = "";

    [BinaryProperty("token", Order = 2)]
    public string? Token { get; set; }

    [BinaryProperty("args", Order = 3)]
    public byte[]? Args { get; set; }
}
