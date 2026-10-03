using System.Collections.Concurrent;

namespace Polhem.JsonRpc.Payload.Server;

/// <summary>
/// Keeps the sequence numbers of each replay scope in memory: a sliding window of the last 64 numbers below the highest
/// one seen. A scope that stays idle longer than its lifetime is forgotten.
/// </summary>
/// <remarks>
/// A forgotten scope starts over at whatever number arrives next. That is safe only while a frame that old is rejected
/// by its timestamp anyway, so the lifetime must be at least twice <see cref="PayloadOptions.FrameTimestampTolerance"/>.
/// </remarks>
public sealed class MemoryPayloadReplayStore : IPayloadReplayStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private long _lastSweepAtTimestamp;
    private int _sweeping;

    /// <summary>Initializes a new instance whose scopes live for twice the default frame timestamp tolerance.</summary>
    /// <remarks>
    /// A host that changes <see cref="PayloadOptions.FrameTimestampTolerance"/> uses the other constructor, as
    /// <see cref="PayloadFilter"/> does when it creates its own store.
    /// </remarks>
    public MemoryPayloadReplayStore() : this(new PayloadOptions().FrameTimestampTolerance * 2) { }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="lifetime">How long an idle scope is kept; at least twice the frame timestamp tolerance.</param>
    /// <param name="clock">The clock; the system clock when <see langword="null"/>.</param>
    public MemoryPayloadReplayStore(TimeSpan lifetime, TimeProvider? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        _lifetime = lifetime;
        _clock = clock ?? TimeProvider.System;
        // Start the throttle as though a sweep had just run: a sweep of an empty store finds nothing.
        _lastSweepAtTimestamp = _clock.GetTimestamp();
    }

    /// <summary>Gets the number of scopes currently remembered.</summary>
    public int Count => _entries.Count;

    /// <inheritdoc/>
    public ValueTask<bool> TryAcceptAsync(string scope, long sequence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();

        SweepIfDue();
        while (true)
        {
            long now = _clock.GetTimestamp();
            var entry = _entries.GetOrAdd(scope, static (_, timestamp) => new Entry(timestamp), now);
            lock (entry.Gate)
            {
                // A sweep removed the entry between the lookup and the lock. Accepting into it would record the number
                // in a window about to be forgotten, so the scope's current entry is looked up again.
                if (entry.IsRemoved) { continue; }
                entry.LastTouchedTimestamp = now;
                return new ValueTask<bool>(entry.Window.TryAccept(sequence));
            }
        }
    }

    private void SweepIfDue()
    {
        long now = _clock.GetTimestamp();
        if (_clock.GetElapsedTime(Volatile.Read(ref _lastSweepAtTimestamp), now) < _lifetime) { return; }

        // One sweeper at a time; everyone else carries on rather than queueing behind it.
        if (Interlocked.Exchange(ref _sweeping, 1) == 1) { return; }
        try
        {
            Volatile.Write(ref _lastSweepAtTimestamp, now);
            foreach (var pair in _entries)
            {
                var entry = pair.Value;
                lock (entry.Gate)
                {
                    // Strictly greater, so an entry idle for exactly the lifetime is kept. Decided and marked under the
                    // entry's lock, so a request that touches the entry either lands before and keeps it, or after and
                    // sees it removed.
                    if (_clock.GetElapsedTime(entry.LastTouchedTimestamp, now) > _lifetime)
                    {
                        entry.IsRemoved = true;
                        _entries.TryRemove(pair);
                    }
                }
            }
        }
        finally
        {
            Volatile.Write(ref _sweeping, 0);
        }
    }

    private sealed class Entry(long touchedAt)
    {
        public Lock Gate { get; } = new();

        public ReplayWindow Window { get; } = new();

        public long LastTouchedTimestamp { get; set; } = touchedAt;

        public bool IsRemoved { get; set; }
    }
}
