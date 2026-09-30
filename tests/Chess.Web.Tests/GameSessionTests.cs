using Chess.Engine;
using Chess.Web.Domain;

namespace Chess.Web.Tests;

public class GameSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private const string WhiteToken = "WHITE-HASH";
    private const string BlackToken = "BLACK-HASH";

    private static GameSession NewGame(TimeControl? timeControl = null)
    {
        var session = GameSession.Create("TESTGAME", timeControl, ColorPreference.White, PieceColor.White, "Alice", WhiteToken, Start);
        session.Join("white-conn", WhiteToken, [], "ignored", "unused", Start);
        return session;
    }

    private static GameSession StartedGame(TimeControl? timeControl = null)
    {
        var session = NewGame(timeControl);
        session.Join("black-conn", null, [], "Bob", BlackToken, Start);
        return session;
    }

    private static GameActionResult Move(GameSession session, string connection, string uci, DateTimeOffset? at = null) =>
        session.MakeMove(connection, Engine.Move.ParseUci(uci), at ?? Start);

    [Fact]
    public void Creator_WaitsForOpponent_UntilSomeoneJoins()
    {
        var session = NewGame();

        Assert.Equal(GameStatus.WaitingForOpponent, session.Status);
        Assert.Equal(GameError.GameNotStarted, Move(session, "white-conn", "e2e4").Error);
    }

    [Fact]
    public void SecondVisitor_TakesTheFreeSeat_AndStartsTheGame()
    {
        var session = NewGame();

        var outcome = session.Join("black-conn", null, [], "Bob", BlackToken, Start);

        Assert.Equal(ParticipantRole.Black, outcome.Role);
        Assert.True(outcome.ClaimedNewSeat);
        Assert.Equal(GameStatus.InProgress, session.Status);
        Assert.Equal("Bob", session.Seat(PieceColor.Black).Name);
        Assert.Contains(outcome.Result.Events, e => e.Type == GameEventType.PlayerJoined && e.Color == PieceColor.Black);
    }

    [Fact]
    public void ThirdVisitor_BecomesSpectator_AndCannotMove()
    {
        var session = StartedGame();

        var outcome = session.Join("third-conn", "SOMEONE-ELSE", ["ANOTHER"], "Eve", "EVE-HASH", Start);

        Assert.Equal(ParticipantRole.Spectator, outcome.Role);
        Assert.Equal(1, session.SpectatorCount);
        Assert.Equal(GameError.NotAPlayer, Move(session, "third-conn", "e2e4").Error);
        Assert.Equal("Bob", session.Seat(PieceColor.Black).Name);
    }

    [Fact]
    public void RememberedToken_ResumesSeat_OnlyWhenNobodyIsUsingIt()
    {
        var session = StartedGame();

        // Another tab of Black's browser while Black is connected: must not hijack the seat.
        var whileConnected = session.Join("black-tab-2", null, [BlackToken], "Bob", "NEW", Start);
        Assert.Equal(ParticipantRole.Spectator, whileConnected.Role);

        session.Leave("black-conn", Start);
        var afterLeaving = session.Join("black-tab-3", null, [BlackToken], "Bob", "NEW", Start);
        Assert.Equal(ParticipantRole.Black, afterLeaving.Role);
        Assert.Equal(BlackToken, afterLeaving.MatchedTokenHash);
        Assert.Contains(afterLeaving.Result.Events, e => e.Type == GameEventType.PlayerReconnected);
    }

    [Fact]
    public void OwnTabToken_ResumesSeat_EvenWhenAlreadyConnected()
    {
        var session = StartedGame();

        var duplicateTab = session.Join("white-tab-2", WhiteToken, [], "Alice", "NEW", Start);

        Assert.Equal(ParticipantRole.White, duplicateTab.Role);
        Assert.Equal(GameError.None, Move(session, "white-tab-2", "e2e4").Error);
    }

    [Fact]
    public void JoiningAgainOnTheSameConnection_IsNotReportedAsAReconnect()
    {
        var session = StartedGame();

        var again = session.Join("black-conn", BlackToken, [], "Bob", "NEW", Start);

        Assert.Equal(ParticipantRole.Black, again.Role);
        Assert.Empty(again.Result.Events);
        Assert.True(session.Seat(PieceColor.Black).IsConnected);
    }

    [Fact]
    public void Moves_AreOnlyAccepted_FromThePlayerWhoseTurnItIs()
    {
        var session = StartedGame();

        Assert.Equal(GameError.NotYourTurn, Move(session, "black-conn", "e7e5").Error);
        Assert.True(Move(session, "white-conn", "e2e4").Succeeded);
        Assert.Equal(GameError.NotYourTurn, Move(session, "white-conn", "d2d4").Error);
        Assert.True(Move(session, "black-conn", "e7e5").Succeeded);
        Assert.Equal(2, session.Game.Moves.Count);
    }

    [Fact]
    public void IllegalMoves_AreRejected_WithoutChangingState()
    {
        var session = StartedGame();
        var version = session.Version;

        var result = Move(session, "white-conn", "e2e5");

        Assert.Equal(GameError.IllegalMove, result.Error);
        Assert.Equal(ChangeKind.None, result.Change);
        Assert.Equal(version, session.Version);
        Assert.Empty(session.Game.Moves);
    }

    [Fact]
    public void Checkmate_FinishesTheGame()
    {
        var session = StartedGame();
        Move(session, "white-conn", "f2f3");
        Move(session, "black-conn", "e7e5");
        Move(session, "white-conn", "g2g4");
        Move(session, "black-conn", "d8h4");

        Assert.Equal(GameStatus.Finished, session.Status);
        Assert.Equal(GameOutcome.Win(PieceColor.Black, GameEndReason.Checkmate), session.Game.Outcome);
        Assert.Equal(GameError.GameFinished, Move(session, "white-conn", "a2a3").Error);
    }

    [Fact]
    public void Clock_DoesNotRun_UntilBothSidesHaveMoved_ThenAddsIncrement()
    {
        var session = StartedGame(new TimeControl(TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(2)));
        var clock = session.Clock!;

        Move(session, "white-conn", "e2e4", Start.AddSeconds(30));
        Move(session, "black-conn", "e7e5", Start.AddSeconds(60));
        Assert.Equal(TimeSpan.FromMinutes(3), clock.GetRemaining(PieceColor.White, Start.AddSeconds(60)));
        Assert.Equal(TimeSpan.FromMinutes(3), clock.GetRemaining(PieceColor.Black, Start.AddSeconds(60)));
        Assert.Equal(PieceColor.White, clock.RunningFor);

        Move(session, "white-conn", "g1f3", Start.AddSeconds(70));

        // 10 seconds used, 2 seconds increment.
        Assert.Equal(TimeSpan.FromSeconds(172), clock.GetRemaining(PieceColor.White, Start.AddSeconds(70)));
        Assert.Equal(PieceColor.Black, clock.RunningFor);
    }

    [Fact]
    public void Clock_FlagFall_EndsTheGameOnTime()
    {
        var session = StartedGame(new TimeControl(TimeSpan.FromMinutes(1), TimeSpan.Zero));
        Move(session, "white-conn", "e2e4");
        Move(session, "black-conn", "e7e5");

        Assert.Equal(ChangeKind.None, session.CheckFlag(Start.AddSeconds(59)).Change);
        var result = session.CheckFlag(Start.AddSeconds(60));

        Assert.Equal(ChangeKind.Game, result.Change);
        Assert.Equal(GameOutcome.Win(PieceColor.Black, GameEndReason.Timeout), session.Game.Outcome);
        Assert.Null(session.Clock!.RunningFor);
    }

    [Fact]
    public void Clock_MoveArrivingAfterFlagFall_LosesOnTime()
    {
        var session = StartedGame(new TimeControl(TimeSpan.FromMinutes(1), TimeSpan.Zero));
        Move(session, "white-conn", "e2e4");
        Move(session, "black-conn", "e7e5");

        var late = Move(session, "white-conn", "g1f3", Start.AddSeconds(61));

        Assert.Equal(GameError.GameFinished, late.Error);
        Assert.Equal(ChangeKind.Game, late.Change);
        Assert.Equal(GameEndReason.Timeout, session.Game.Outcome?.Reason);
        Assert.Equal(2, session.Game.Moves.Count);
    }

    [Fact]
    public void FlagFallTicks_TrackTheRunningClock()
    {
        var session = StartedGame(new TimeControl(TimeSpan.FromMinutes(1), TimeSpan.Zero));
        Assert.Equal(long.MaxValue, session.FlagFallUtcTicks);

        Move(session, "white-conn", "e2e4");
        Move(session, "black-conn", "e7e5");

        Assert.Equal(Start.AddMinutes(1).UtcTicks, session.FlagFallUtcTicks);
    }

    [Fact]
    public void DrawOffer_CanBeDeclined_AndIsLimitedToOnePerMove()
    {
        var session = StartedGame();

        Assert.True(session.OfferDraw("white-conn", Start).Succeeded);
        Assert.Equal(PieceColor.White, session.DrawOfferedBy);
        Assert.Equal(GameError.DrawAlreadyOffered, session.OfferDraw("white-conn", Start).Error);
        Assert.Equal(GameError.NoDrawOffer, session.RespondToDrawOffer("white-conn", true, Start).Error);

        var declined = session.RespondToDrawOffer("black-conn", false, Start);
        Assert.Contains(declined.Events, e => e.Type == GameEventType.DrawDeclined);
        Assert.Null(session.DrawOfferedBy);
        Assert.Equal(GameError.DrawOfferLimitReached, session.OfferDraw("white-conn", Start).Error);

        Move(session, "white-conn", "e2e4");
        Assert.True(session.OfferDraw("white-conn", Start).Succeeded);
    }

    [Fact]
    public void DrawOffer_IsDeclinedByMoving_AndAcceptingEndsTheGame()
    {
        var session = StartedGame();
        session.OfferDraw("black-conn", Start);

        Move(session, "white-conn", "e2e4");
        Assert.Null(session.DrawOfferedBy);

        session.OfferDraw("white-conn", Start);
        Assert.True(session.RespondToDrawOffer("black-conn", true, Start).Succeeded);
        Assert.Equal(GameOutcome.Draw(GameEndReason.Agreement), session.Game.Outcome);
        Assert.Equal(GameStatus.Finished, session.Status);
    }

    [Fact]
    public void MutualDrawOffers_AgreeTheDraw()
    {
        var session = StartedGame();
        session.OfferDraw("white-conn", Start);

        session.OfferDraw("black-conn", Start);

        Assert.Equal(GameOutcome.Draw(GameEndReason.Agreement), session.Game.Outcome);
    }

    [Fact]
    public void Resigning_BeforeBothSidesMoved_AbortsTheGame()
    {
        var session = StartedGame();
        Move(session, "white-conn", "e2e4");

        session.Resign("black-conn", Start);

        Assert.Equal(GameOutcome.Abort(), session.Game.Outcome);
    }

    [Fact]
    public void Resigning_AfterTheOpening_AwardsTheOpponent()
    {
        var session = StartedGame();
        Move(session, "white-conn", "e2e4");
        Move(session, "black-conn", "e7e5");

        session.Resign("white-conn", Start);

        Assert.Equal(GameOutcome.Win(PieceColor.Black, GameEndReason.Resignation), session.Game.Outcome);
        Assert.Equal(GameError.NotAPlayer, session.Resign("spectator", Start).Error);
    }

    [Fact]
    public void Rematch_RequiresBothPlayers_AndSwapsColours()
    {
        var session = StartedGame(new TimeControl(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(3)));
        Assert.Equal(GameError.RematchUnavailable, session.RequestRematch("white-conn", Start).Error);
        session.Resign("white-conn", Start);

        session.RequestRematch("white-conn", Start);
        Assert.False(session.IsRematchAgreed);
        session.RequestRematch("black-conn", Start);
        Assert.True(session.IsRematchAgreed);

        var rematch = session.CreateRematch("REMATCH1", Start);

        Assert.Equal("REMATCH1", session.RematchCode);
        Assert.Equal("TESTGAME", rematch.RematchOfCode);
        Assert.Equal(GameStatus.InProgress, rematch.Status);
        Assert.Equal("Bob", rematch.Seat(PieceColor.White).Name);
        Assert.Equal(BlackToken, rematch.Seat(PieceColor.White).TokenHash);
        Assert.Equal(WhiteToken, rematch.Seat(PieceColor.Black).TokenHash);
        Assert.Equal(session.TimeControl, rematch.TimeControl);
    }

    [Fact]
    public void Rematch_CanBeDeclined()
    {
        var session = StartedGame();
        session.Resign("white-conn", Start);
        session.RequestRematch("black-conn", Start);

        var declined = session.DeclineRematch("white-conn", Start);

        Assert.Contains(declined.Events, e => e.Type == GameEventType.RematchDeclined);
        Assert.False(session.Seat(PieceColor.Black).RematchRequested);
    }

    [Fact]
    public void AbandonedGame_CanBeClaimed_OnlyAfterTheGracePeriod()
    {
        var session = StartedGame();
        var grace = TimeSpan.FromSeconds(60);
        Assert.Equal(GameError.OpponentConnected, session.ClaimAbandonedGame("white-conn", false, grace, Start).Error);

        var left = session.Leave("black-conn", Start);
        Assert.Contains(left.Events, e => e.Type == GameEventType.PlayerDisconnected && e.Color == PieceColor.Black);
        Assert.Equal(GameError.ReconnectGracePending, session.ClaimAbandonedGame("white-conn", false, grace, Start.AddSeconds(59)).Error);

        Assert.True(session.ClaimAbandonedGame("white-conn", false, grace, Start.AddSeconds(60)).Succeeded);
        Assert.Equal(GameOutcome.Win(PieceColor.White, GameEndReason.Abandonment), session.Game.Outcome);
    }

    [Fact]
    public void ComputerSeat_IsAlwaysPresent_AndOnlyTheServerCanMoveForIt()
    {
        var session = GameSession.CreateAgainstBot("BOTGAME1", null, ColorPreference.White, PieceColor.White, "Alice", WhiteToken, 3, Start);
        session.Join("white-conn", WhiteToken, [], "Alice", "unused", Start);

        Assert.Equal(GameStatus.InProgress, session.Status);
        Assert.Equal(PieceColor.Black, session.BotColor);
        Assert.True(session.Seat(PieceColor.Black).IsConnected);
        Assert.False(session.IsBotToMove);

        Move(session, "white-conn", "e2e4");
        Assert.True(session.IsBotToMove);
        Assert.Equal(GameError.NotYourTurn, Move(session, "white-conn", "e7e5").Error);

        // A stale computer move (computed for an earlier position) is ignored.
        Assert.Equal(ChangeKind.None, session.PlayBotMove(Engine.Move.ParseUci("e7e5"), expectedPly: 0, Start).Change);
        Assert.True(session.PlayBotMove(Engine.Move.ParseUci("e7e5"), expectedPly: 1, Start).Succeeded);
        Assert.Equal(2, session.Game.Moves.Count);
    }

    [Fact]
    public void ComputerSeat_CannotBeClaimedOrAbandoned()
    {
        var session = GameSession.CreateAgainstBot("BOTGAME2", null, ColorPreference.White, PieceColor.White, "Alice", WhiteToken, 1, Start);
        session.Join("white-conn", WhiteToken, [], "Alice", "unused", Start);

        Assert.Equal(ParticipantRole.Spectator, session.Join("intruder", null, [], "Eve", "EVE", Start).Role);
        Assert.Equal(GameError.OpponentConnected,
            session.ClaimAbandonedGame("white-conn", false, TimeSpan.Zero, Start.AddHours(1)).Error);
        Assert.False(session.HasConnections && session.Seat(PieceColor.Black).HasBrowserConnections);
    }

    [Fact]
    public void Version_Increases_WithEveryObservableChange()
    {
        var session = StartedGame();
        var before = session.Version;

        Move(session, "white-conn", "e2e4");
        var afterMove = session.Version;
        session.Join("spectator", null, [], "Eve", "EVE", Start);

        Assert.True(afterMove > before);
        Assert.True(session.Version > afterMove);
    }

    [Fact]
    public void Restore_ReplaysMoves_AndReappliesOutcomeAndClock()
    {
        var data = new RestoredGame(
            Code: "RESTORED",
            Status: GameStatus.Finished,
            InitialFen: Fen.StartingPosition,
            TimeControl: new TimeControl(TimeSpan.FromMinutes(5), TimeSpan.Zero),
            ColorPreference: ColorPreference.Random,
            WhiteRemaining: TimeSpan.FromSeconds(250),
            BlackRemaining: TimeSpan.FromSeconds(240),
            Outcome: GameOutcome.Win(PieceColor.White, GameEndReason.Resignation),
            RematchOfCode: null,
            RematchCode: null,
            CreatedAt: Start,
            StartedAt: Start,
            FinishedAt: Start.AddMinutes(1),
            Players: [new RestoredPlayer(PieceColor.White, "Alice", WhiteToken), new RestoredPlayer(PieceColor.Black, "Bob", BlackToken)],
            Moves: [new RestoredMove(1, "e2e4", Start, null, null), new RestoredMove(2, "e7e5", Start, null, null)]);

        var session = GameSession.Restore(data, Start.AddHours(1));

        Assert.Equal(2, session.Game.Moves.Count);
        Assert.Equal(GameOutcome.Win(PieceColor.White, GameEndReason.Resignation), session.Game.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(250), session.Clock!.GetRemaining(PieceColor.White, Start.AddHours(2)));
        Assert.Null(session.Clock.RunningFor);
        Assert.Equal(ParticipantRole.Black, session.Join("c", BlackToken, [], "Bob", "x", Start).Role);
    }

    [Fact]
    public void Restore_RejectsCorruptMoveHistory()
    {
        var data = new RestoredGame(
            "CORRUPT1", GameStatus.InProgress, Fen.StartingPosition, null, ColorPreference.White, null, null, null, null, null,
            Start, Start, null, [], [new RestoredMove(1, "e2e5", Start, null, null)]);

        Assert.Throws<InvalidDataException>(() => GameSession.Restore(data, Start));
    }
}
