namespace SmartData.Server.Procedures;

public class ProcedureException : Exception
{
    /// <summary>
    /// Error message ID. System range: 0–999. User range: 1000+.
    /// Default 0 means no specific message ID.
    /// </summary>
    public int MessageId { get; }
    public ErrorSeverity Severity { get; }

    public ProcedureException(string message) : base(message)
    {
        Severity = ErrorSeverity.Error;
    }

    public ProcedureException(int messageId, string message, ErrorSeverity severity = ErrorSeverity.Error) : base(message)
    {
        MessageId = messageId;
        Severity = severity;
    }
}
