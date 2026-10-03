namespace Polhem.JsonRpc.Payload.Server;

/// <summary>
/// Remembers which sequence numbers each replay scope has used.
/// </summary>
/// <remarks>
/// <see cref="MemoryPayloadReplayStore"/> keeps them in the process. A deployment with several server instances behind a
/// load balancer needs a shared store, or a call replayed to another instance is accepted there.
/// </remarks>
public interface IPayloadReplayStore
{
    /// <summary>Accepts a sequence number for a scope, unless the scope already used it or it is out of range.</summary>
    /// <param name="scope">The replay scope, usually a session.</param>
    /// <param name="sequence">The sequence number from the frame.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the number is accepted.</returns>
    ValueTask<bool> TryAcceptAsync(string scope, long sequence, CancellationToken cancellationToken = default);
}
