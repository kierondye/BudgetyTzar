namespace BudgetyTzar.Api.Features.Identity;

public interface ICurrentUser
{
    ApplicationUserId UserId { get; }
}

public sealed record CurrentUser(ApplicationUserId UserId) : ICurrentUser;

