namespace SmartData.Server;

/// <summary>
/// Exception thrown by SmartData when a configuration, schema, or runtime error is detected.
/// </summary>
public class SmartDataException : Exception
{
    public SmartDataException(string message) : base(message) { }
    public SmartDataException(string message, Exception innerException) : base(message, innerException) { }
}
