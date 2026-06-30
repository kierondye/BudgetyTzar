using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed class MoneyAmountTests
{
    [Fact]
    public void PositiveMoneyAmountCreateReturnsValidationFailureForNonPositiveAmounts()
    {
        var result = PositiveMoneyAmount.Create(0m);

        var validationProblem = Assert.IsType<PositiveMoneyAmountResult.ValidationFailed>(result);
        Assert.Equal(MoneyAmount.PositiveAmountRequiredMessage, validationProblem.Error);
    }

    [Fact]
    public void PositiveMoneyAmountCreateReturnsValidationFailureForAmountsWithTooManyDecimalPlaces()
    {
        var result = PositiveMoneyAmount.Create(10.001m);

        var validationProblem = Assert.IsType<PositiveMoneyAmountResult.ValidationFailed>(result);
        Assert.Equal(MoneyAmount.MoneyScaleExceededMessage, validationProblem.Error);
    }

    [Fact]
    public void PositiveMoneyAmountCreateReturnsPositiveMoneyAmountForValidPositiveAmounts()
    {
        var result = PositiveMoneyAmount.Create(10.01m);

        var success = Assert.IsType<PositiveMoneyAmountResult.Success>(result);
        Assert.Equal(10.01m, success.Amount.Value);
    }
}
