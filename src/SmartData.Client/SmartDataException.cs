using SmartData.Core.Api;

namespace SmartData.Client;

public class SmartDataException : Exception
{
    public CommandResponse? Response { get; }

    public SmartDataException(string message) : base(message) { }

    public SmartDataException(string message, CommandResponse response) : base(message)
    {
        Response = response;
    }

    public SmartDataException(string message, Exception inner) : base(message, inner) { }
}
