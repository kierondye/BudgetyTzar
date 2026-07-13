namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class ApplicationUserRecord
{
    public Guid ApplicationUserId { get; set; }

    public required string UserKey { get; set; }
}
