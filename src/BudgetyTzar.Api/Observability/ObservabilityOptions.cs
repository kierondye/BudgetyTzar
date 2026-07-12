namespace BudgetyTzar.Api.Observability;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool ConsoleExporterEnabled { get; init; }

    public string? OtlpEndpoint { get; init; }
}
