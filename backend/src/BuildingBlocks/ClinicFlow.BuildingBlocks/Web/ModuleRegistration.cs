using System.Reflection;
using ClinicFlow.BuildingBlocks.Cqrs;
using ClinicFlow.BuildingBlocks.Messaging;
using ClinicFlow.BuildingBlocks.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicFlow.BuildingBlocks.Web;

public interface IModule
{
    void Register(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

public interface IModuleInitializer
{
    Task Migrate(CancellationToken ct);
    Task Seed(CancellationToken ct);
}

public static class ModuleRegistration
{
    public static IServiceCollection AddBuildingBlocks(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<Time.ClinicClock>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddSingleton<IntegrationEventRegistry>();
        services.AddSingleton<IntegrationEventDispatcher>();
        return services;
    }

    /// <summary>Registers all handlers and validators of a module, including internal ones.</summary>
    public static IServiceCollection AddModuleHandlers(this IServiceCollection services, Assembly assembly)
    {
        var openTypes = new[] { typeof(IRequestHandler<,>), typeof(IIntegrationEventHandler<>) };

        var registrations = assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces().Select(contract => (type, contract)))
            .Where(pair => pair.contract.IsGenericType && openTypes.Contains(pair.contract.GetGenericTypeDefinition()));

        foreach (var (type, contract) in registrations)
        {
            services.AddScoped(contract, type);
        }

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }

    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, IConfiguration configuration, string schema)
        where TContext : ModuleDbContext
    {
        var connection = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not set.");

        services.AddDbContext<TContext>(options =>
        {
            if (configuration["Database:Provider"] == "Sqlite")
            {
                options.UseSqlite(connection.Replace("{schema}", schema));
                return;
            }

            options.UseSqlServer(connection, sql => sql
                .MigrationsHistoryTable("__EFMigrationsHistory", schema)
                .MigrationsAssembly(typeof(TContext).Assembly));
        });

        services.AddScoped<IOutboxPublisher, OutboxPublisher<TContext>>();
        services.AddHealthChecks().AddDbContextCheck<TContext>(typeof(TContext).Name, tags: ["ready"]);
        return services;
    }
}
