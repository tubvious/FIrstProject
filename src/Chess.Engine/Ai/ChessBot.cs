using System.Diagnostics;

namespace Chess.Engine.Ai;

/// <summary>
/// A computer opponent: iterative-deepening alpha-beta (negamax) search with quiescence search on
/// captures, simple move ordering and a time limit. Stateless and thread-safe; each call searches
/// independently.
/// </summary>
public static class ChessBot
{
    /// <summary>
    /// Picks a move for the side to move, or null if there is none (the game is over).
    /// </summary>
    /// <param name="timeBudget">Optional extra cap on thinking time, e.g. derived from the bot's clock.</param>
    public static Move? ChooseMove(Position position, BotLevel level, TimeSpan? timeBudget = null, Random? random = null)
    {
        var moves = position.LegalMoves;
        if (moves.Count == 0)
        {
            return null;
        }

        random ??= Random.Shared;
        if (moves.Count == 1)
        {
            return moves[0];
        }

        if (random.NextDouble() < level.RandomMoveChance)
        {
            return moves[random.Next(moves.Count)];
        }

        var limit = timeBudget is { } budget && budget < level.ThinkTime ? budget : level.ThinkTime;
        var scored = new Search(limit).ScoreRootMoves(position, level.MaxDepth, exactScores: level.NoiseCentipawns > 0);

        var best = scored[0].Move;
        var bestScore = int.MinValue;
        foreach (var (move, score) in scored)
        {
            var noisy = score + (level.NoiseCentipawns > 0 ? random.Next(-level.NoiseCentipawns, level.NoiseCentipawns + 1) : 0);
            if (noisy > bestScore)
            {
                bestScore = noisy;
                best = move;
            }
        }

        return best;
    }

    private sealed class Search(TimeSpan limit)
    {
        private const int Infinity = 1_000_000;
        private const int MateScore = 100_000;
        private const int MaxQuiescenceDepth = 8;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _nodes;
        private bool _stopped;
        private bool _mayStop;

        /// <summary>
        /// Returns every root move with its score from the deepest fully completed iteration, best first.
        /// With exactScores every move gets a true score (needed for the noise of weaker levels);
        /// otherwise only the best move's score is exact, which is much faster.
        /// </summary>
        public List<(Move Move, int Score)> ScoreRootMoves(Position root, int maxDepth, bool exactScores)
        {
            var results = root.LegalMoves.Select(m => (Move: m, Score: 0)).ToList();
            results = [.. results.OrderByDescending(r => OrderingScore(root, r.Move))];

            for (var depth = 1; depth <= maxDepth; depth++)
            {
                var iteration = new List<(Move Move, int Score)>(results.Count);
                var alpha = -Infinity;
                foreach (var (move, _) in results)
                {
                    var window = exactScores ? -Infinity : alpha;
                    var score = -Negamax(root.Apply(move), depth - 1, -Infinity, -window, ply: 1);
                    if (_stopped)
                    {
                        break;
                    }

                    iteration.Add((move, score));
                    alpha = Math.Max(alpha, score);
                }

                if (_stopped)
                {
                    break;
                }

                results = [.. iteration.OrderByDescending(r => r.Score)];
                _mayStop = true; // at least one full iteration is available from here on

                if (results[0].Score >= MateScore - 100)
                {
                    break; // found a forced mate; searching deeper cannot improve on it
                }
            }

            return results;
        }

        private int Negamax(Position position, int depth, int alpha, int beta, int ply)
        {
            if (ShouldStop())
            {
                return 0;
            }

            var moves = position.LegalMoves;
            if (moves.Count == 0)
            {
                return position.IsCheck ? -MateScore + ply : 0;
            }

            if (position.HalfmoveClock >= 100 || position.HasInsufficientMaterial())
            {
                return 0;
            }

            if (depth <= 0)
            {
                return Quiescence(position, alpha, beta, ply, 0);
            }

            foreach (var move in Ordered(position, moves, capturesOnly: false))
            {
                var score = -Negamax(position.Apply(move), depth - 1, -beta, -alpha, ply + 1);
                if (_stopped)
                {
                    return 0;
                }

                if (score >= beta)
                {
                    return beta;
                }

                alpha = Math.Max(alpha, score);
            }

            return alpha;
        }

        /// <summary>Keeps searching captures until the position is quiet, so exchanges are judged correctly.</summary>
        private int Quiescence(Position position, int alpha, int beta, int ply, int qDepth)
        {
            if (ShouldStop())
            {
                return 0;
            }

            var moves = position.LegalMoves;
            if (moves.Count == 0)
            {
                return position.IsCheck ? -MateScore + ply : 0;
            }

            var standPat = Evaluator.Evaluate(position);
            if (standPat >= beta || qDepth >= MaxQuiescenceDepth)
            {
                return Math.Min(standPat, beta);
            }

            alpha = Math.Max(alpha, standPat);
            foreach (var move in Ordered(position, moves, capturesOnly: true))
            {
                var score = -Quiescence(position.Apply(move), -beta, -alpha, ply + 1, qDepth + 1);
                if (_stopped)
                {
                    return 0;
                }

                if (score >= beta)
                {
                    return beta;
                }

                alpha = Math.Max(alpha, score);
            }

            return alpha;
        }

        private bool ShouldStop()
        {
            if (!_stopped && _mayStop && (++_nodes & 511) == 0 && _clock.Elapsed > limit)
            {
                _stopped = true;
            }

            return _stopped;
        }

        private static IEnumerable<Move> Ordered(Position position, IReadOnlyList<Move> moves, bool capturesOnly)
        {
            var candidates = capturesOnly
                ? moves.Where(m => m.Promotion is not null || position.GetMoveFlags(m).HasFlag(MoveFlags.Capture))
                : moves;
            return candidates.OrderByDescending(m => OrderingScore(position, m));
        }

        /// <summary>Most valuable victim / least valuable attacker first, then promotions.</summary>
        private static int OrderingScore(Position position, Move move)
        {
            var score = 0;
            if (position.GetCapturedPiece(move) is { } victim)
            {
                score += 10 * Evaluator.PieceValue(victim.Type) - Evaluator.PieceValue(position[move.From]!.Value.Type) / 10;
            }

            if (move.Promotion is { } promotion)
            {
                score += Evaluator.PieceValue(promotion);
            }

            return score;
        }
    }
}
