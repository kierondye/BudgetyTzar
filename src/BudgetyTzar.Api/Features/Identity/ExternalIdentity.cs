namespace BudgetyTzar.Api.Features.Identity;

public sealed record ExternalIdentity
{
    private ExternalIdentity(string provider, string subject)
    {
        Provider = provider;
        Subject = subject;
    }

    public string Provider { get; }

    public string Subject { get; }

    public static bool TryCreate(string? provider, string? subject, out ExternalIdentity? externalIdentity)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(subject))
        {
            externalIdentity = null;
            return false;
        }

        externalIdentity = new ExternalIdentity(provider.Trim(), subject.Trim());
        return true;
    }
}

