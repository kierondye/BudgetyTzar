namespace BudgetyTzar.Api.Features.Transactions;

public readonly record struct TransactionType
{
    public static TransactionType Empty { get; } = new(string.Empty);
    public static TransactionType Credit { get; } = new(nameof(Credit));
    public static TransactionType Debit { get; } = new(nameof(Debit));

    private TransactionType(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? value, out TransactionType type)
    {
        type = value switch
        {
            nameof(Credit) => Credit,
            nameof(Debit) => Debit,
            _ => Empty
        };

        return type != Empty;
    }
}
