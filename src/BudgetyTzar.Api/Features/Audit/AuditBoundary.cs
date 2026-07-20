using BudgetyTzar.Api.Observability;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BudgetyTzar.Api.Features.Audit;

public static class AuditBoundary
{
    public static IServiceCollection AddAudit(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IAuditRequestContext, AuditRequestContext>();
        return services;
    }
}

public interface IAuditRequestContext
{
    string OperationName { get; }

    string CorrelationId { get; }

    DateTimeOffset PersistedAtUtc();
}

internal sealed class AuditRequestContext : IAuditRequestContext
{
    public AuditRequestContext(IHttpContextAccessor httpContextAccessor)
    {
        var httpContext = httpContextAccessor.HttpContext;
        OperationName = EndpointName(httpContext);
        CorrelationId = ReadCorrelationId(httpContext);
    }

    public string OperationName { get; }

    public string CorrelationId { get; }

    public DateTimeOffset PersistedAtUtc()
    {
        return DateTimeOffset.UtcNow;
    }

    private static string EndpointName(HttpContext? context)
    {
        var name = context?
            .GetEndpoint()?
            .Metadata
            .GetMetadata<IEndpointNameMetadata>()?
            .EndpointName;

        return string.IsNullOrWhiteSpace(name)
            ? "repository"
            : name;
    }

    private static string ReadCorrelationId(HttpContext? context)
    {
        return context?.Items[CorrelationIdMiddleware.HeaderName] as string
            ?? Guid.NewGuid().ToString("N");
    }
}
