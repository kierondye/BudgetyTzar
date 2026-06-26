namespace BudgetyTzar.Api.Features;

public static class CommandResultHttpExtensions
{
    public const string EventIdHeaderName = "X-BudgetyTzar-Event-Id";
    public const string ProjectionStatusHeaderName = "X-BudgetyTzar-Projection-Status";

    public static IResult ToHttpResult<T>(this CommandResult<T> result, HttpContext httpContext, Func<T, IResult>? created = null) =>
        result.Status switch
        {
            CommandResultStatus.Success => WithProjectionHeaders(Results.Ok(result.Value), httpContext, result.EventId, TryGetBudgetId(result.Value)),
            CommandResultStatus.Created => WithProjectionHeaders(created is not null ? created(result.Value!) : Results.Created(string.Empty, result.Value), httpContext, result.EventId, TryGetBudgetId(result.Value)),
            CommandResultStatus.NoContent => Results.NoContent(),
            CommandResultStatus.NotFound => Results.NotFound(),
            CommandResultStatus.ValidationProblem => Results.ValidationProblem(result.Errors?.ToDictionary(x => x.Key, x => x.Value) ?? []),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };

    public static IResult ToHttpResult(this CommandResult result, HttpContext httpContext, Guid? budgetId = null) =>
        result.Status switch
        {
            CommandResultStatus.Success => WithProjectionHeaders(Results.Ok(), httpContext, result.EventId, budgetId),
            CommandResultStatus.NoContent => WithProjectionHeaders(Results.NoContent(), httpContext, result.EventId, budgetId),
            CommandResultStatus.NotFound => Results.NotFound(),
            CommandResultStatus.ValidationProblem => Results.ValidationProblem(result.Errors?.ToDictionary(x => x.Key, x => x.Value) ?? []),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };

    private static IResult WithProjectionHeaders(IResult result, HttpContext httpContext, Guid? eventId, Guid? budgetId)
    {
        if (eventId.HasValue)
        {
            httpContext.Response.Headers[EventIdHeaderName] = eventId.Value.ToString();
            if (budgetId.HasValue)
            {
                httpContext.Response.Headers[ProjectionStatusHeaderName] =
                    $"/api/budgets/{budgetId.Value}/projections/status?eventId={eventId.Value}";
            }
        }

        return result;
    }

    private static Guid? TryGetBudgetId<T>(T? value) =>
        value switch
        {
            Budget budget => budget.Id,
            BudgetItem item => item.BudgetId,
            BudgetAdjustment adjustment => adjustment.BudgetId,
            BudgetReallocation reallocation => reallocation.BudgetId,
            FinancialTransaction transaction => transaction.BudgetId,
            _ => null
        };
}
