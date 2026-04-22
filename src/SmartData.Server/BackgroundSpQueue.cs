using System.Threading.Channels;

namespace SmartData.Server;

internal record BackgroundSpWork(
    string SpName,
    Dictionary<string, object> Parameters,
    string? Token,
    bool Trusted = false,
    string? TrustedUser = null);

internal class BackgroundSpQueue
{
    private readonly Channel<BackgroundSpWork> _channel = Channel.CreateBounded<BackgroundSpWork>(1000);

    public void Enqueue(BackgroundSpWork work) =>
        _channel.Writer.TryWrite(work);

    public async Task<BackgroundSpWork> DequeueAsync(CancellationToken ct) =>
        await _channel.Reader.ReadAsync(ct);
}
