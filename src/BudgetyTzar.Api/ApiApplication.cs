namespace BudgetyTzar.Api;

public static class ApiApplication
{
    public static WebApplication Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        return builder.Build();
    }
}
