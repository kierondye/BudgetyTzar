using System.Globalization;

namespace BudgetyTzar.Api.Features.Identity;

public sealed record ApplicationUserKey
{
    private ApplicationUserKey(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ApplicationUserKey FromExternalIdentity(ExternalIdentity externalIdentity)
    {
        var provider = externalIdentity.Provider;
        var subject = externalIdentity.Subject;

        return new ApplicationUserKey(string.Concat(
            provider.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            provider,
            subject.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            subject));
    }
}
