namespace BudgetyTzar.Api.Authentication;

public readonly record struct ExternalUserIdentity
{
    private ExternalUserIdentity(string provider, string subject)
    {
        Provider = provider;
        Subject = subject;
    }

    public string Provider { get; }

    public string Subject { get; }

    public static ExternalUserIdentity Create(string provider, string subject)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("External identity provider is required.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("External identity subject is required.", nameof(subject));
        }

        return new ExternalUserIdentity(provider.Trim(), subject.Trim());
    }
}
