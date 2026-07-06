namespace BudgetyTzar.Api.Features.Transactions;

public sealed record CreateTransactionRequest(
    string Description,
    string Type,
    string TransactionDate,
    string Amount,
    string Currency);

public sealed record TransactionResponse(
    Guid TransactionId,
    string Description,
    string Type,
    string TransactionDate,
    string Amount,
    string Currency)
{
    public static TransactionResponse FromTransaction(Transaction transaction)
    {
        return new TransactionResponse(
            transaction.TransactionId,
            transaction.Description,
            transaction.Type.Value,
            transaction.TransactionDate.ToString("yyyy-MM-dd"),
            transaction.Amount.Value,
            transaction.Currency.Value);
    }
}

public sealed record TransactionListItemResponse(
    Guid TransactionId,
    string Description,
    string Type,
    string TransactionDate,
    string Amount,
    string Currency)
{
    public static TransactionListItemResponse FromTransaction(Transaction transaction)
    {
        return new TransactionListItemResponse(
            transaction.TransactionId,
            transaction.Description,
            transaction.Type.Value,
            transaction.TransactionDate.ToString("yyyy-MM-dd"),
            transaction.Amount.Value,
            transaction.Currency.Value);
    }
}
