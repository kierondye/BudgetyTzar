using FluentValidation;

namespace BudgetyTzar.Api.Features;

public static class Validation
{
    public static async Task<IResult?> Validate<T>(
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

public static class MoneyRules
{
    public static IRuleBuilderOptions<T, decimal> PositiveAmount<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0).PrecisionScale(18, 2, true);

    public static IRuleBuilderOptions<T, string> Currency<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Length(3).Must(x => x.All(char.IsUpper)).WithMessage("Currency must be a three-letter uppercase ISO code.");
}
