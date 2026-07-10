using System.Diagnostics.CodeAnalysis;

namespace BudgetyTzar.Api.Authentication;

public sealed record ExternalUserIdentity
{
    private ExternalUserIdentity(string provider, string subject)
    {
        Provider = provider;
        Subject = subject;
    }

    public string Provider { get; }

    public string Subject { get; }

    public static bool TryCreate(
        string? provider,
        string? subject,
        [NotNullWhen(true)] out ExternalUserIdentity? externalIdentity)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            externalIdentity = null;
            return false;
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            externalIdentity = null;
            return false;
        }

        externalIdentity = new ExternalUserIdentity(provider.Trim(), subject.Trim());
        return true;
    }
}
