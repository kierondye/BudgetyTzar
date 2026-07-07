namespace BudgetyTzar.Api.Domain.ValueTypes;

public readonly record struct BudgetItemKind
{
    public static BudgetItemKind Empty { get; } = new(string.Empty);
    public static BudgetItemKind Funding { get; } = new(nameof(Funding));
    public static BudgetItemKind Consumption { get; } = new(nameof(Consumption));

    private BudgetItemKind(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? value, out BudgetItemKind kind)
    {
        kind = value switch
        {
            nameof(Funding) => Funding,
            nameof(Consumption) => Consumption,
            _ => Empty
        };

        return kind != Empty;
    }
}
