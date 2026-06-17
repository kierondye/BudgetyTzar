using BudgetyTzar.Api.Application.Common;

namespace BudgetyTzar.Api.Features;

public static class CommandResultHttpExtensions
{
    public static IResult ToHttpResult<T>(this CommandResult<T> result, Func<T, IResult>? created = null) =>
        result.Status switch
        {
            CommandResultStatus.Success => Results.Ok(result.Value),
            CommandResultStatus.Created => created is not null ? created(result.Value!) : Results.Created(string.Empty, result.Value),
            CommandResultStatus.NoContent => Results.NoContent(),
            CommandResultStatus.NotFound => Results.NotFound(),
            CommandResultStatus.ValidationProblem => Results.ValidationProblem(result.Errors?.ToDictionary(x => x.Key, x => x.Value) ?? []),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };

    public static IResult ToHttpResult(this CommandResult result) =>
        result.Status switch
        {
            CommandResultStatus.Success => Results.Ok(),
            CommandResultStatus.NoContent => Results.NoContent(),
            CommandResultStatus.NotFound => Results.NotFound(),
            CommandResultStatus.ValidationProblem => Results.ValidationProblem(result.Errors?.ToDictionary(x => x.Key, x => x.Value) ?? []),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
}
