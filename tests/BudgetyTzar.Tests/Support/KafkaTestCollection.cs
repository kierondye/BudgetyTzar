namespace BudgetyTzar.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KafkaTestCollection
{
    public const string Name = "Kafka integration tests";
}
