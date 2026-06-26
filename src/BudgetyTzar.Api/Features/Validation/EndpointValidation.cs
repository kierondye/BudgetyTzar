using FluentValidation;

namespace BudgetyTzar.Api.Features;

public static class EndpointValidation
{
    public static async Task<IResult?> ValidateEndpointRequest<T>(
        this IValidator<T> validator,
        T request,
        CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        return result.IsValid
            ? null
            : Results.ValidationProblem(result.ToDictionary());
    }
}
