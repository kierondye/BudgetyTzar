namespace BudgetyTzar.Api.Features;

public sealed class InMemoryDataStoreLock
{
    public object SyncRoot { get; } = new();
}
