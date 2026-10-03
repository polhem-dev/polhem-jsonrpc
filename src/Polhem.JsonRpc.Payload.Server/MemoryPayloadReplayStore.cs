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
        long now = _clock.GetTimestamp();
        var entry = _entries.GetOrAdd(scope, static (_, timestamp) => new Entry(timestamp), now);
        Volatile.Write(ref entry.LastTouchedTimestamp, now);
        return new ValueTask<bool>(entry.Window.TryAccept(sequence));
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
                // Strictly greater, so an entry idle for exactly the lifetime is kept.
                if (_clock.GetElapsedTime(Volatile.Read(ref pair.Value.LastTouchedTimestamp), now) > _lifetime)
                {
                    _entries.TryRemove(pair);
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
        public ReplayWindow Window { get; } = new();

        public long LastTouchedTimestamp = touchedAt;
    }
}
