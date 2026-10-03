namespace Polhem.JsonRpc.Payload.Server;

/// <summary>
/// The sliding window of one replay scope: the highest sequence number seen, and a bit for each of the
/// <see cref="WindowSize"/> numbers below it.
/// </summary>
internal sealed class ReplayWindow
{
    public const int WindowSize = 64;

    // A jump this large is refused rather than accepted, because accepting it would make every number below the new
    // highest one fall out of the window at once.
    public const long MaxForwardJump = 1_000_000;

    private readonly Lock _gate = new();
    private long _highest = -1;
    private ulong _seen;

    public bool TryAccept(long sequence)
    {
        if (sequence < 0) { return false; }

        lock (_gate)
        {
            if (_highest < 0)
            {
                // The first request of the scope sets the baseline.
                _highest = sequence;
                _seen = 1UL;
                return true;
            }

            if (sequence > _highest)
            {
                long advance = sequence - _highest;
                if (advance > MaxForwardJump) { return false; }
                _seen = advance >= WindowSize ? 0UL : _seen << (int)advance;
                _seen |= 1UL;
                _highest = sequence;
                return true;
            }

            long behind = _highest - sequence;
            if (behind >= WindowSize) { return false; }
            ulong bit = 1UL << (int)behind;
            if ((_seen & bit) != 0) { return false; }
            _seen |= bit;
            return true;
        }
    }
}
