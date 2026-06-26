namespace BudgetyTzar.Api.Features;

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
    IReadOnlyDictionary<string, string[]>? Errors = null,
    Guid? EventId = null)
{
    public static CommandResult<T> Success(T value, Guid? eventId = null) => new(CommandResultStatus.Success, value, EventId: eventId);
    public static CommandResult<T> Created(T value, Guid? eventId = null) => new(CommandResultStatus.Created, value, EventId: eventId);
    public static CommandResult<T> NoContent() => new(CommandResultStatus.NoContent);
    public static CommandResult<T> NotFound() => new(CommandResultStatus.NotFound);
    public static CommandResult<T> ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        new(CommandResultStatus.ValidationProblem, default, errors);
}

public sealed record CommandResult(
    CommandResultStatus Status,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    Guid? EventId = null)
{
    public static CommandResult Success(Guid? eventId = null) => new(CommandResultStatus.Success, EventId: eventId);
    public static CommandResult NoContent(Guid? eventId = null) => new(CommandResultStatus.NoContent, EventId: eventId);
    public static CommandResult NotFound() => new(CommandResultStatus.NotFound);
    public static CommandResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        new(CommandResultStatus.ValidationProblem, errors);
}
