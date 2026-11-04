using ClinicFlow.BuildingBlocks.Cqrs;
using ClinicFlow.BuildingBlocks.Web;
using ClinicFlow.Modules.Appointments.Features;
using ClinicFlow.Modules.Appointments.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicFlow.Modules.Appointments;

public sealed class AppointmentsModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AppointmentsDbContext>(configuration, AppointmentsDbContext.SchemaName);
        services.AddModuleHandlers(typeof(AppointmentsModule).Assembly);
        services.AddScoped<AppointmentReader>();
        services.AddScoped<IModuleInitializer, AppointmentsInitializer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var appointments = endpoints.MapGroup("/api/appointments").WithTags("Appointments");

        appointments.MapGet("/slots", async (Guid doctorId, Guid treatmentId, DateOnly date, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new GetAvailableSlots(doctorId, treatmentId, date), ct)).ToHttp());

        appointments.MapPost("/", async (BookAppointment command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToCreated(id => $"/api/appointments/{id}"))
            .RequireAuthorization();

        appointments.MapGet("/mine", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new GetMyAppointments(), ct)).ToHttp())
            .RequireAuthorization();

        appointments.MapGet("/day", async (DateOnly date, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new GetDaySchedule(date), ct)).ToHttp())
            .RequireAuthorization(Policies.Staff);

        appointments.MapGet("/stats", async (DateOnly date, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new GetDayStats(date), ct)).ToHttp())
            .RequireAuthorization(Policies.Staff);

        appointments.MapPost("/{id:guid}/confirm", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new ConfirmAppointment(id), ct)).ToHttp())
            .RequireAuthorization(Policies.Staff);

        appointments.MapPost("/{id:guid}/complete", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new CompleteAppointment(id), ct)).ToHttp())
            .RequireAuthorization(Policies.Staff);

        appointments.MapPost("/{id:guid}/cancel", async (Guid id, CancelRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new CancelAppointment(id, body.Reason), ct)).ToHttp())
            .RequireAuthorization();
    }

    private sealed record CancelRequest(string Reason);
}

internal sealed class AppointmentsInitializer(AppointmentsDbContext db) : IModuleInitializer
{
    public Task Migrate(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public Task Seed(CancellationToken ct) => Task.CompletedTask;
}
