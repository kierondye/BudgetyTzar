using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed class CurrencyTests
{
    [Fact]
    public void CurrencyAcceptsThreeLetterUppercaseCode()
    {
        var currency = new Currency("GBP");

        Assert.Equal("GBP", currency.Value);
    }

    [Fact]
    public void CurrencyTrimsBoundaryWhitespace()
    {
        var currency = new Currency(" GBP ");

        Assert.Equal("GBP", currency.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("gbp")]
    [InlineData("GB")]
    [InlineData("GBPA")]
    public void CurrencyRejectsInvalidCodes(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() => new Currency(value));

        Assert.Equal("Currency must be a three-letter uppercase ISO code. (Parameter 'value')", exception.Message);
    }
}
