using ClinicFlow.BuildingBlocks.Cqrs;
using ClinicFlow.BuildingBlocks.Messaging;
using ClinicFlow.BuildingBlocks.Web;
using ClinicFlow.Modules.Doctors.Contracts;
using ClinicFlow.Modules.Doctors.Domain;
using ClinicFlow.Modules.Doctors.Features;
using ClinicFlow.Modules.Doctors.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicFlow.Modules.Doctors;

public sealed class DoctorsModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<DoctorsDbContext>(configuration, DoctorsDbContext.SchemaName);
        services.AddModuleHandlers(typeof(DoctorsModule).Assembly);
        services.AddScoped<IDoctorsModule, DoctorsModuleApi>();
        services.AddScoped<IModuleInitializer, DoctorsInitializer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var doctors = endpoints.MapGroup("/api/doctors").WithTags("Doctors");

        doctors.MapGet("/", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new ListDoctors(), ct)).ToHttp());

        doctors.MapPost("/", async (CreateDoctor command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToCreated(id => $"/api/doctors/{id}"))
            .RequireAuthorization(Policies.Admin);

        doctors.MapPut("/{id:guid}/working-hours", async (Guid id, WorkingHoursRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new SetWorkingHours(id, body.Hours), ct)).ToHttp())
            .RequireAuthorization(Policies.Admin);

        doctors.MapPost("/{id:guid}/deactivate", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new DeactivateDoctor(id), ct)).ToHttp())
            .RequireAuthorization(Policies.Admin);

        var treatments = endpoints.MapGroup("/api/treatments").WithTags("Treatments");

        treatments.MapGet("/", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new ListTreatments(), ct)).ToHttp());

        treatments.MapPost("/", async (CreateTreatment command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToCreated(id => $"/api/treatments/{id}"))
            .RequireAuthorization(Policies.Admin);
    }

    private sealed record WorkingHoursRequest(IReadOnlyList<WorkingHoursDto> Hours);
}

internal sealed class DoctorsInitializer(DoctorsDbContext db) : IModuleInitializer
{
    public Task Migrate(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public async Task Seed(CancellationToken ct)
    {
        if (await db.Doctors.AnyAsync(ct))
        {
            return;
        }

        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };

        // Sample data. These people do not exist.
        var sample = new (string Name, string Specialty, TimeOnly Start, TimeOnly End)[]
        {
            ("Dr. Maya Reed", "General Dentist", new(9, 0), new(17, 0)),
            ("Dr. Omar Hale", "Orthodontist", new(11, 0), new(19, 0)),
            ("Dr. Lina Frost", "Dental Hygienist", new(8, 0), new(14, 0)),
        };

        foreach (var (name, specialty, start, end) in sample)
        {
            var doctor = Doctor.Create(name, specialty);
            doctor.SetWorkingHours(weekdays.Select(day => new WorkingHours(day, start, end)).ToList());
            db.Doctors.Add(doctor);
        }

        db.Treatments.AddRange(
            Treatment.Create("Check-up", 30, 40),
            Treatment.Create("Teeth cleaning", 45, 70),
            Treatment.Create("Filling", 60, 120),
            Treatment.Create("Braces consultation", 30, 50));

        await db.SaveChangesAsync(ct);
    }
}
