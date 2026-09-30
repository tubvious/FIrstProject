using Chess.Web.Contracts;
using Chess.Web.Domain;
using Chess.Web.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Chess.Web.Endpoints;

public static class GameEndpoints
{
    public const string CreateGameRateLimitPolicy = "create-game";

    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/games").WithTags("Games");

        group.MapPost("/", CreateGameAsync).RequireRateLimiting(CreateGameRateLimitPolicy);
        group.MapGet("/{code}", GetGameAsync);

        return endpoints;
    }

    private static async Task<Results<Created<CreateGameResponse>, ValidationProblem>> CreateGameAsync(
        CreateGameRequest request, IGameService games, CancellationToken cancellationToken)
    {
        TimeControl? timeControl = null;
        if (request.Minutes is { } minutes &&
            !TimeControl.TryCreate(minutes, request.IncrementSeconds ?? 0, out timeControl))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["timeControl"] =
                [
                    $"Time must be {TimeControl.MinMinutes}-{TimeControl.MaxMinutes} minutes " +
                    $"with an increment of 0-{TimeControl.MaxIncrementSeconds} seconds.",
                ],
            });
        }

        if (!Enum.IsDefined(request.Color))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["color"] = ["Color must be white, black or random."],
            });
        }

        var response = await games.CreateGameAsync(
            new CreateGameCommand(request.PlayerName, timeControl, request.Color), cancellationToken);
        return TypedResults.Created(response.Url, response);
    }

    private static async Task<Results<Ok<GameSummaryDto>, NotFound>> GetGameAsync(
        string code, IGameService games, CancellationToken cancellationToken)
    {
        if (!GameCode.TryNormalize(code, out var normalized))
        {
            return TypedResults.NotFound();
        }

        var summary = await games.GetSummaryAsync(normalized, cancellationToken);
        return summary is null ? TypedResults.NotFound() : TypedResults.Ok(summary);
    }
}
