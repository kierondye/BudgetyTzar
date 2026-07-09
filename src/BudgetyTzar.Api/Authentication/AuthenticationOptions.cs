namespace BudgetyTzar.Api.Authentication;

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string Scheme { get; set; } = "Bearer";

    public string ProviderClaimType { get; set; } = "iss";

    public string SubjectClaimType { get; set; } = "sub";
}
