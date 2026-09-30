using Chess.Engine;

namespace Chess.Web.Domain;

/// <summary>
/// Server-side chess clock. Time is derived from timestamps supplied by the caller, so the clock
/// never depends on client input and is deterministic under test.
/// </summary>
public sealed class ChessClock
{
    private TimeSpan _whiteRemaining;
    private TimeSpan _blackRemaining;
    private DateTimeOffset _runningSince;

    public ChessClock(TimeControl timeControl)
        : this(timeControl, timeControl.Initial, timeControl.Initial)
    {
    }

    public ChessClock(TimeControl timeControl, TimeSpan whiteRemaining, TimeSpan blackRemaining)
    {
        TimeControl = timeControl;
        _whiteRemaining = whiteRemaining;
        _blackRemaining = blackRemaining;
    }

    public TimeControl TimeControl { get; }

    /// <summary>The side whose time is currently running, or null when the clock is stopped.</summary>
    public PieceColor? RunningFor { get; private set; }

    public TimeSpan GetRemaining(PieceColor color, DateTimeOffset now)
    {
        var remaining = color == PieceColor.White ? _whiteRemaining : _blackRemaining;
        if (RunningFor == color)
        {
            remaining -= now - _runningSince;
        }

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>The instant the running side's flag falls, or null when the clock is stopped.</summary>
    public DateTimeOffset? GetFlagFallTime() =>
        RunningFor is { } color ? _runningSince + (color == PieceColor.White ? _whiteRemaining : _blackRemaining) : null;

    public bool HasFlagged(PieceColor color, DateTimeOffset now) => GetRemaining(color, now) <= TimeSpan.Zero;

    public void Start(PieceColor color, DateTimeOffset now)
    {
        Stop(now);
        RunningFor = color;
        _runningSince = now;
    }

    public void Stop(DateTimeOffset now)
    {
        if (RunningFor is { } color)
        {
            SetRemaining(color, GetRemaining(color, now));
            RunningFor = null;
        }
    }

    /// <summary>Ends the running side's turn: charges the elapsed time, adds the increment and starts the opponent.</summary>
    public void SwitchTurn(DateTimeOffset now)
    {
        var mover = RunningFor ?? throw new InvalidOperationException("The clock is not running.");
        SetRemaining(mover, GetRemaining(mover, now) + TimeControl.Increment);
        RunningFor = mover.Opposite();
        _runningSince = now;
    }

    private void SetRemaining(PieceColor color, TimeSpan value)
    {
        if (color == PieceColor.White)
        {
            _whiteRemaining = value;
        }
        else
        {
            _blackRemaining = value;
        }
    }
}
