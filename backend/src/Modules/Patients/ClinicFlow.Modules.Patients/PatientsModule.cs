using ClinicFlow.BuildingBlocks.Cqrs;
using ClinicFlow.BuildingBlocks.Time;
using ClinicFlow.BuildingBlocks.Web;
using ClinicFlow.Modules.Patients.Contracts;
using ClinicFlow.Modules.Patients.Domain;
using ClinicFlow.Modules.Patients.Features;
using ClinicFlow.Modules.Patients.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicFlow.Modules.Patients;

public sealed class PatientsModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PatientsDbContext>(configuration, PatientsDbContext.SchemaName);
        services.AddModuleHandlers(typeof(PatientsModule).Assembly);
        services.AddScoped<IPatientsModule, PatientsModuleApi>();
        services.AddScoped<IModuleInitializer, PatientsInitializer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var patients = endpoints.MapGroup("/api/patients").WithTags("Patients").RequireAuthorization();

        patients.MapPost("/", async (RegisterPatient command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToCreated(id => $"/api/patients/{id}"));

        patients.MapGet("/me", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new GetMyProfile(), ct)).ToHttp());

        patients.MapGet("/", async (string? search, int? page, int? pageSize, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new SearchPatients(search, page ?? 1, pageSize ?? 20), ct)).ToHttp())
            .RequireAuthorization(Policies.Staff);

        patients.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new GetPatient(id), ct)).ToHttp())
            .RequireAuthorization(Policies.Staff);
    }
}

internal sealed class PatientsInitializer(PatientsDbContext db, ClinicClock clock) : IModuleInitializer
{
    public Task Migrate(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public async Task Seed(CancellationToken ct)
    {
        if (await db.Patients.AnyAsync(ct))
        {
            return;
        }

        // Sample data. These people do not exist. example.com is reserved for documentation.
        db.Patients.AddRange(
            Patient.Register("Sara Malik", "sara@example.com", "+971 50 000 0001", clock.UtcNow),
            Patient.Register("Tom Byrne", "tom@example.com", "+353 87 000 0002", clock.UtcNow),
            Patient.Register("Noor Khan", "noor@example.com", "+974 5000 0003", clock.UtcNow));

        await db.SaveChangesAsync(ct);
    }
}
