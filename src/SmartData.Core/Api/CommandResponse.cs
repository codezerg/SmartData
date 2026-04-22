using SmartData.Core.BinarySerialization;

namespace SmartData.Core.Api;

[BinarySerializable]
public class CommandResponse
{
    [BinaryProperty("success", Order = 1)]
    public bool Success { get; set; }

    [BinaryProperty("data", Order = 2)]
    public byte[]? Data { get; set; }

    [BinaryProperty("error", Order = 3)]
    public string? Error { get; set; }

    [BinaryProperty("error_id", Order = 4)]
    public int? ErrorId { get; set; }

    [BinaryProperty("error_severity", Order = 5)]
    public int? ErrorSeverity { get; set; }

    [BinaryProperty("authenticated", Order = 6)]
    public bool? Authenticated { get; set; }

    public static CommandResponse Ok(object? data = null) => new()
    {
        Success = true,
        Data = data != null ? BinarySerializer.Serialize(data) : null
    };

    public static CommandResponse Fail(string error) => new() { Success = false, Error = error };

    public T? GetData<T>()
    {
        if (Data == null || Data.Length == 0) return default;
        return BinarySerializer.Deserialize<T>(Data);
    }

    public string? GetDataAsJson()
    {
        if (Data == null || Data.Length == 0) return null;
        return BinaryToJson.Convert(Data);
    }
}
