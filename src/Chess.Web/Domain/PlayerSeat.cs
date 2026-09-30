using Chess.Engine;

namespace Chess.Web.Domain;

/// <summary>One side of the board: who owns it and which connections currently represent them.</summary>
public sealed class PlayerSeat
{
    private readonly HashSet<string> _connections = new(StringComparer.Ordinal);

    public PlayerSeat(PieceColor color)
    {
        Color = color;
    }

    public PieceColor Color { get; }

    public string? Name { get; private set; }

    public string? TokenHash { get; private set; }

    public bool IsOccupied => TokenHash is not null;

    /// <summary>Difficulty (1-5) when this seat is played by the computer; null for a person.</summary>
    public int? BotLevel { get; private set; }

    public bool IsBot => BotLevel is not null;

    /// <summary>
    /// Whether the player is present. A person may have the game open in several tabs; any of them counts.
    /// The computer is always present.
    /// </summary>
    public bool IsConnected => IsBot || _connections.Count > 0;

    /// <summary>Whether any browser connection is attached (never true for the computer).</summary>
    public bool HasBrowserConnections => _connections.Count > 0;

    /// <summary>When the owner last lost their final connection (or took the seat without connecting yet).</summary>
    public DateTimeOffset? DisconnectedSince { get; private set; }

    public bool RematchRequested { get; internal set; }

    /// <summary>The ply count at which this player last offered a draw; limits offers to one per move.</summary>
    public int? LastDrawOfferPly { get; internal set; }

    internal void Occupy(string name, string tokenHash, DateTimeOffset now)
    {
        if (IsOccupied)
        {
            throw new InvalidOperationException($"The {Color} seat is already taken.");
        }

        Name = name;
        TokenHash = tokenHash;
        DisconnectedSince = now;
    }

    /// <summary>Seats the computer. Its token is random and never revealed, so nobody can take the seat over.</summary>
    internal void OccupyWithBot(string name, int level)
    {
        if (IsOccupied)
        {
            throw new InvalidOperationException($"The {Color} seat is already taken.");
        }

        Name = name;
        TokenHash = SeatToken.Hash(SeatToken.Generate());
        BotLevel = level;
        DisconnectedSince = null;
    }

    /// <summary>Seats the same player (person or computer) as <paramref name="previous"/>, e.g. for a rematch.</summary>
    internal void TakeOver(PlayerSeat previous, DateTimeOffset now)
    {
        if (previous.BotLevel is { } level)
        {
            OccupyWithBot(previous.Name!, level);
        }
        else
        {
            Occupy(previous.Name!, previous.TokenHash!, now);
        }
    }

    internal bool IsOwnedBy(string tokenHash) => TokenHash is not null && SeatToken.HashesEqual(TokenHash, tokenHash);

    internal bool HasConnection(string connectionId) => _connections.Contains(connectionId);

    internal void Attach(string connectionId)
    {
        _connections.Add(connectionId);
        DisconnectedSince = null;
    }

    /// <summary>Removes a connection; returns true when this was the owner's last one.</summary>
    internal bool Detach(string connectionId, DateTimeOffset now)
    {
        if (!_connections.Remove(connectionId) || IsConnected)
        {
            return false;
        }

        DisconnectedSince = now;
        return true;
    }
}
