namespace BudgetyTzar.Api.Features.Transactions;

public readonly record struct TransactionType
{
    public static TransactionType Empty { get; } = new(string.Empty);

    private TransactionType(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? value, out TransactionType type)
    {
        type = value switch
        {
            "Credit" => new TransactionType("Credit"),
            "Debit" => new TransactionType("Debit"),
            _ => Empty
        };

        return type != Empty;
    }
}
