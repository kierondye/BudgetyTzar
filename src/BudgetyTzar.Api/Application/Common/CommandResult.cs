namespace BudgetyTzar.Api.Application.Common;

public enum CommandResultStatus
{
    Success,
    Created,
    NoContent,
    NotFound,
    ValidationProblem
}

public sealed record CommandResult<T>(
    CommandResultStatus Status,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static CommandResult<T> Success(T value) => new(CommandResultStatus.Success, value);
    public static CommandResult<T> Created(T value) => new(CommandResultStatus.Created, value);
    public static CommandResult<T> NoContent() => new(CommandResultStatus.NoContent);
    public static CommandResult<T> NotFound() => new(CommandResultStatus.NotFound);
    public static CommandResult<T> ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        new(CommandResultStatus.ValidationProblem, default, errors);
}

public sealed record CommandResult(
    CommandResultStatus Status,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static CommandResult Success() => new(CommandResultStatus.Success);
    public static CommandResult NoContent() => new(CommandResultStatus.NoContent);
    public static CommandResult NotFound() => new(CommandResultStatus.NotFound);
    public static CommandResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        new(CommandResultStatus.ValidationProblem, errors);
}
