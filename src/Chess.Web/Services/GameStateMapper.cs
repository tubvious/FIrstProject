using Chess.Engine;
using Chess.Web.Contracts;
using Chess.Web.Domain;

namespace Chess.Web.Services;

/// <summary>Projects a <see cref="GameSession"/> into the snapshot sent to clients. Call while holding the session lock.</summary>
public static class GameStateMapper
{
    public static GameStateDto ToDto(GameSession session, DateTimeOffset now, TimeSpan reconnectGracePeriod)
    {
        var game = session.Game;
        var position = game.CurrentPosition;

        return new GameStateDto(
            Code: session.Code,
            Version: session.Version,
            Status: session.Status,
            Fen: position.ToFen(),
            Turn: position.SideToMove,
            Moves: game.Moves.Select(ToDto).ToList(),
            LegalMoves: session.Status == GameStatus.InProgress ? game.LegalMoves.Select(m => m.ToUci()).ToList() : [],
            CheckSquare: position.IsCheck ? position.FindKing(position.SideToMove)?.ToString() : null,
            White: ToDto(session.Seat(PieceColor.White), now),
            Black: ToDto(session.Seat(PieceColor.Black), now),
            Clock: ToDto(session.Clock, now),
            DrawOfferedBy: session.DrawOfferedBy,
            CanAbort: session.CanAbort,
            Outcome: game.Outcome is { } outcome ? new OutcomeDto(outcome.Result, outcome.Reason, outcome.Winner) : null,
            Captured: CapturedPieces(game),
            MaterialBalance: MaterialBalance(position),
            SpectatorCount: session.SpectatorCount,
            RematchCode: session.RematchCode,
            RematchOfCode: session.RematchOfCode,
            ReconnectGraceMs: (long)reconnectGracePeriod.TotalMilliseconds,
            ServerTime: now.ToUnixTimeMilliseconds());
    }

    public static GameSummaryDto ToSummary(GameSession session)
    {
        var white = session.Seat(PieceColor.White);
        var black = session.Seat(PieceColor.Black);
        return new GameSummaryDto(
            session.Code,
            session.Status,
            session.TimeControl?.ToString(),
            white.Name,
            black.Name,
            HasOpenSeat: session.Status == GameStatus.WaitingForOpponent);
    }

    private static MoveDto ToDto(PlayedMove move) => new(
        move.Ply,
        move.San,
        move.Uci,
        move.Move.From.ToString(),
        move.Move.To.ToString(),
        move.IsCapture,
        move.IsCheck,
        move.Flags.HasFlag(MoveFlags.Castle),
        move.Flags.HasFlag(MoveFlags.EnPassant),
        move.Move.Promotion,
        move.PositionAfter.ToFen());

    private static PlayerDto? ToDto(PlayerSeat seat, DateTimeOffset now) =>
        seat.IsOccupied
            ? new PlayerDto(
                seat.Name!,
                seat.IsConnected,
                seat.DisconnectedSince is { } since ? (long)(now - since).TotalMilliseconds : null,
                seat.RematchRequested)
            : null;

    private static ClockDto? ToDto(ChessClock? clock, DateTimeOffset now) =>
        clock is null
            ? null
            : new ClockDto(
                (long)clock.TimeControl.Initial.TotalMilliseconds,
                (long)clock.TimeControl.Increment.TotalMilliseconds,
                (long)clock.GetRemaining(PieceColor.White, now).TotalMilliseconds,
                (long)clock.GetRemaining(PieceColor.Black, now).TotalMilliseconds,
                clock.RunningFor);

    private static CapturedPiecesDto CapturedPieces(ChessGame game)
    {
        List<PieceType> CapturedBy(PieceColor color) => game.Moves
            .Where(m => m.Color == color && m.CapturedPiece is not null)
            .Select(m => m.CapturedPiece!.Value.Type)
            .OrderBy(type => type)
            .ToList();

        return new CapturedPiecesDto(CapturedBy(PieceColor.White), CapturedBy(PieceColor.Black));
    }

    /// <summary>Material difference in pawns; positive means White is ahead.</summary>
    private static int MaterialBalance(Position position) =>
        position.Pieces.Sum(p => p.Piece.Color == PieceColor.White ? p.Piece.Type.MaterialValue() : -p.Piece.Type.MaterialValue());
}
