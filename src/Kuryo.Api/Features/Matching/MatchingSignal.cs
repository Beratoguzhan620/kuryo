using System.Threading.Channels;

namespace Kuryo.Api.Features.Matching;

/// <summary>
/// Wakes the matcher when there is something to do right away (a new order, a declined offer, a courier coming
/// online), so the next offer goes out in milliseconds instead of at the next tick.
/// </summary>
public sealed class MatchingSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Poke() => _channel.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(timer.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
    }
}
