using BudgetyTzar.Api.Authentication;

namespace BudgetyTzar.Tests.Support;

public sealed class FixedCurrentUser(ApplicationUserId userId) : ICurrentUser
{
    public ApplicationUserId UserId { get; } = userId;
}
