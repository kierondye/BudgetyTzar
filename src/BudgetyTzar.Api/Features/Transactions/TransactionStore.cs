using System.Globalization;
using BudgetyTzar.Api.Features.Common;

namespace BudgetyTzar.Api.Features.Transactions;

public sealed class TransactionStore
{
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, Transaction> transactionsById = [];
    private readonly List<Guid> transactionIds = [];

    public CreateTransactionResult Create(
        string description,
        string type,
        string transactionDate,
        string amount,
        string currency)
    {
        var validation = Validate(description, type, transactionDate, amount, currency);

        if (validation is CreateTransactionValidationResult.Invalid invalid)
        {
            return new CreateTransactionResult.Invalid(invalid.Errors);
        }

        var valid = (CreateTransactionValidationResult.Valid)validation;
        var transaction = new Transaction(
            Guid.NewGuid(),
            description.Trim(),
            valid.Type,
            valid.TransactionDate,
            valid.Amount,
            valid.Currency);

        lock (syncRoot)
        {
            transactionsById[transaction.TransactionId] = transaction;
            transactionIds.Add(transaction.TransactionId);
        }

        return new CreateTransactionResult.Created(transaction);
    }

    public IReadOnlyList<Transaction> GetAll(
        TransactionFilters? filters = null,
        Func<Guid, bool>? isTransactionAllocated = null)
    {
        lock (syncRoot)
        {
            var allTransactions = transactionIds
                .Select(transactionId => transactionsById[transactionId])
                .Where(transaction => filters?.Matches(
                    transaction,
                    isTransactionAllocated?.Invoke(transaction.TransactionId) ?? false) ?? true)
                .ToList();

            return allTransactions;
        }
    }

    public Transaction? Get(Guid transactionId)
    {
        lock (syncRoot)
        {
            return transactionsById.GetValueOrDefault(transactionId);
        }
    }

    public bool Delete(Guid transactionId)
    {
        lock (syncRoot)
        {
            if (!transactionsById.Remove(transactionId))
            {
                return false;
            }

            transactionIds.Remove(transactionId);
            return true;
        }
    }

    private static CreateTransactionValidationResult Validate(
        string description,
        string type,
        string transactionDate,
        string amount,
        string currency)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(description))
        {
            errors["description"] = ["Transaction description is required."];
        }

        if (!TransactionType.TryCreate(type, out var parsedType))
        {
            errors["type"] = ["Transaction type must be Credit or Debit."];
        }

        if (!DateOnly.TryParseExact(
            transactionDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedTransactionDate))
        {
            errors["transactionDate"] = ["Transaction date must use the yyyy-MM-dd format."];
        }

        if (!PositiveMoneyAmount.TryCreate(amount, out var parsedAmount))
        {
            errors["amount"] = ["Amount must be a positive decimal string with exactly two decimal places and no more than 99999999.99."];
        }

        if (!CurrencyCode.TryCreate(currency, out var parsedCurrency))
        {
            errors["currency"] = ["Currency must be an uppercase ISO 4217 alphabetic code."];
        }

        return errors.Count > 0
            ? new CreateTransactionValidationResult.Invalid(errors)
            : new CreateTransactionValidationResult.Valid(parsedType, parsedTransactionDate, parsedAmount!, parsedCurrency);
    }

    private abstract record CreateTransactionValidationResult
    {
        public sealed record Valid(
            TransactionType Type,
            DateOnly TransactionDate,
            PositiveMoneyAmount Amount,
            CurrencyCode Currency) : CreateTransactionValidationResult;

        public sealed record Invalid(Dictionary<string, string[]> Errors) : CreateTransactionValidationResult;
    }
}

public sealed record Transaction(
    Guid TransactionId,
    string Description,
    TransactionType Type,
    DateOnly TransactionDate,
    PositiveMoneyAmount Amount,
    CurrencyCode Currency);

public sealed record TransactionFilters(
    DateOnly? From,
    DateOnly? To,
    TransactionAllocationStatus AllocationStatus)
{
    public bool Matches(Transaction transaction, bool isAllocated)
    {
        if (From is not null && transaction.TransactionDate < From.Value)
        {
            return false;
        }

        if (To is not null && transaction.TransactionDate > To.Value)
        {
            return false;
        }

        return AllocationStatus.Matches(isAllocated);
    }
}

public readonly record struct TransactionAllocationStatus
{
    public static TransactionAllocationStatus All { get; } = new("all");
    public static TransactionAllocationStatus Allocated { get; } = new("allocated");
    public static TransactionAllocationStatus Unallocated { get; } = new("unallocated");

    private TransactionAllocationStatus(string value)
    {
        Value = value;
    }

    private string Value { get; }

    public static bool TryCreate(string? value, out TransactionAllocationStatus allocationStatus)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            allocationStatus = All;
            return true;
        }

        if (string.Equals(value, Allocated.Value, StringComparison.Ordinal))
        {
            allocationStatus = Allocated;
            return true;
        }

        if (string.Equals(value, Unallocated.Value, StringComparison.Ordinal))
        {
            allocationStatus = Unallocated;
            return true;
        }

        if (string.Equals(value, All.Value, StringComparison.Ordinal))
        {
            allocationStatus = All;
            return true;
        }

        allocationStatus = All;
        return false;
    }

    public bool Matches(bool isAllocated)
    {
        if (this == All)
        {
            return true;
        }

        return this == Allocated
            ? isAllocated
            : !isAllocated;
    }
}

public abstract record CreateTransactionResult
{
    public sealed record Created(Transaction Transaction) : CreateTransactionResult;

    public sealed record Invalid(Dictionary<string, string[]> Errors) : CreateTransactionResult;
}
