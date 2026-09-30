using System.Diagnostics.CodeAnalysis;

namespace Chess.Engine;

/// <summary>
/// A full game of chess: the move history plus the rules that end a game (checkmate, stalemate,
/// insufficient material, threefold repetition, the fifty-move rule, resignation, timeout, ...).
/// Not thread-safe; callers must synchronize access.
/// </summary>
public sealed class ChessGame
{
    private readonly List<PlayedMove> _moves = [];
    private readonly Dictionary<string, int> _positionCounts = new(StringComparer.Ordinal);

    public ChessGame()
        : this(Position.Initial)
    {
    }

    public ChessGame(Position initialPosition)
    {
        InitialPosition = initialPosition;
        CurrentPosition = initialPosition;
        RecordPosition(initialPosition);
        Outcome = EvaluateRules();
    }

    public Position InitialPosition { get; }

    public Position CurrentPosition { get; private set; }

    public IReadOnlyList<PlayedMove> Moves => _moves;

    public PlayedMove? LastMove => _moves.Count > 0 ? _moves[^1] : null;

    public GameOutcome? Outcome { get; private set; }

    [MemberNotNullWhen(true, nameof(Outcome))]
    public bool IsOver => Outcome is not null;

    public PieceColor SideToMove => CurrentPosition.SideToMove;

    /// <summary>Legal moves for the side to move; empty once the game is over.</summary>
    public IReadOnlyList<Move> LegalMoves => IsOver ? [] : CurrentPosition.LegalMoves;

    public bool TryMakeMove(Move move, [NotNullWhen(true)] out PlayedMove? played)
    {
        played = null;
        if (IsOver || !CurrentPosition.IsLegal(move))
        {
            return false;
        }

        var before = CurrentPosition;
        var after = before.Apply(move);
        played = new PlayedMove(
            Ply: _moves.Count + 1,
            Move: move,
            San: San.Format(before, move, after),
            Piece: before[move.From]!.Value,
            CapturedPiece: before.GetCapturedPiece(move),
            Flags: before.GetMoveFlags(move),
            PositionAfter: after);

        _moves.Add(played);
        CurrentPosition = after;
        RecordPosition(after);
        Outcome = EvaluateRules();
        return true;
    }

    public PlayedMove MakeMove(Move move) =>
        TryMakeMove(move, out var played)
            ? played
            : throw new InvalidOperationException($"Move {move} cannot be played in position {CurrentPosition.ToFen()}.");

    /// <summary>Plays a move written in SAN (e.g. "Nf3") or UCI (e.g. "g1f3").</summary>
    public PlayedMove MakeMove(string notation)
    {
        if (San.TryParse(CurrentPosition, notation, out var move) || Move.TryParseUci(notation, out move))
        {
            return MakeMove(move);
        }

        throw new InvalidOperationException($"'{notation}' is not a legal move in position {CurrentPosition.ToFen()}.");
    }

    public void Resign(PieceColor color) => End(GameOutcome.Win(color.Opposite(), GameEndReason.Resignation));

    public void AgreeDraw() => End(GameOutcome.Draw(GameEndReason.Agreement));

    /// <summary>
    /// Ends the game because <paramref name="flaggedColor"/> ran out of time. The opponent wins unless
    /// they lack the material to ever deliver mate, in which case the game is drawn.
    /// </summary>
    public void Timeout(PieceColor flaggedColor)
    {
        var opponent = flaggedColor.Opposite();
        End(CurrentPosition.HasMatingMaterial(opponent)
            ? GameOutcome.Win(opponent, GameEndReason.Timeout)
            : GameOutcome.Draw(GameEndReason.TimeoutVsInsufficientMaterial));
    }

    /// <summary>Ends the game because <paramref name="absentColor"/> left and did not come back.</summary>
    public void Abandon(PieceColor absentColor, bool scoreAsDraw) =>
        End(scoreAsDraw
            ? GameOutcome.Draw(GameEndReason.Abandonment)
            : GameOutcome.Win(absentColor.Opposite(), GameEndReason.Abandonment));

    public void Abort() => End(GameOutcome.Abort());

    /// <summary>
    /// Re-applies an outcome that was decided outside the move rules (e.g. when rehydrating a
    /// persisted game that ended by resignation). Rule-based outcomes are recomputed from the moves.
    /// </summary>
    public void RestoreOutcome(GameOutcome outcome)
    {
        if (!IsOver)
        {
            End(outcome);
        }
    }

    private void End(GameOutcome outcome)
    {
        if (IsOver)
        {
            throw new InvalidOperationException("The game is already over.");
        }

        Outcome = outcome;
    }

    private void RecordPosition(Position position)
    {
        var key = position.RepetitionKey;
        _positionCounts[key] = _positionCounts.GetValueOrDefault(key) + 1;
    }

    private GameOutcome? EvaluateRules()
    {
        var position = CurrentPosition;
        if (position.LegalMoves.Count == 0)
        {
            return position.IsCheck
                ? GameOutcome.Win(position.SideToMove.Opposite(), GameEndReason.Checkmate)
                : GameOutcome.Draw(GameEndReason.Stalemate);
        }

        if (position.HasInsufficientMaterial())
        {
            return GameOutcome.Draw(GameEndReason.InsufficientMaterial);
        }

        if (_positionCounts[position.RepetitionKey] >= 3)
        {
            return GameOutcome.Draw(GameEndReason.ThreefoldRepetition);
        }

        return position.HalfmoveClock >= 100 ? GameOutcome.Draw(GameEndReason.FiftyMoveRule) : null;
    }
}
