using ClinicFlow.BuildingBlocks.Cqrs;
using ClinicFlow.BuildingBlocks.Domain;
using ClinicFlow.BuildingBlocks.Messaging;
using ClinicFlow.BuildingBlocks.Persistence;
using ClinicFlow.BuildingBlocks.Time;
using ClinicFlow.BuildingBlocks.Web;
using ClinicFlow.Modules.Appointments.Contracts;
using ClinicFlow.Modules.Doctors.Contracts;
using ClinicFlow.Modules.Patients.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicFlow.Modules.Notifications;

public sealed class Notification
{
    private Notification()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The integration event this came from. Unique, so a repeated event adds no second row.</summary>
    public Guid SourceEventId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    public static Notification Create(Guid sourceEventId, Guid appointmentId, string title, string message, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        SourceEventId = sourceEventId,
        AppointmentId = appointmentId,
        Title = title,
        Message = message,
        CreatedAtUtc = nowUtc,
    };

    public void MarkRead(DateTime nowUtc) => ReadAtUtc ??= nowUtc;
}

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "notifications";

    protected override string Schema => SchemaName;

    public DbSet<Notification> Notifications => Set<Notification>();
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> notification)
    {
        notification.ToTable("Notifications");
        notification.HasKey(n => n.Id);
        notification.Property(n => n.Title).HasMaxLength(120);
        notification.Property(n => n.Message).HasMaxLength(500);
        notification.HasIndex(n => n.SourceEventId).IsUnique();
        notification.HasIndex(n => n.CreatedAtUtc);
    }
}

public sealed record NotificationDto(Guid Id, Guid AppointmentId, string Title, string Message, DateTime CreatedAtUtc, DateTime? ReadAtUtc);

[Authorize(Policy = Policies.Staff)]
public sealed class NotificationsHub : Hub
{
    public const string Path = "/hubs/notifications";
    public const string ReceiveMethod = "notification";
}

internal sealed class AppointmentNotifier(
    NotificationsDbContext db,
    IHubContext<NotificationsHub> hub,
    IPatientsModule patients,
    IDoctorsModule doctors,
    ClinicClock clock)
    : IIntegrationEventHandler<AppointmentBooked>,
      IIntegrationEventHandler<AppointmentConfirmed>,
      IIntegrationEventHandler<AppointmentCancelled>
{
    public Task Handle(AppointmentBooked e, CancellationToken ct) =>
        Notify(e.EventId, e.AppointmentId, e.PatientId, e.DoctorId, "New booking", e.StartsAtUtc, null, ct);

    public Task Handle(AppointmentConfirmed e, CancellationToken ct) =>
        Notify(e.EventId, e.AppointmentId, e.PatientId, e.DoctorId, "Booking confirmed", e.StartsAtUtc, null, ct);

    public Task Handle(AppointmentCancelled e, CancellationToken ct) =>
        Notify(e.EventId, e.AppointmentId, e.PatientId, e.DoctorId, "Booking cancelled", e.StartsAtUtc, e.Reason, ct);

    private async Task Notify(
        Guid eventId, Guid appointmentId, Guid patientId, Guid doctorId, string title, DateTime startsAtUtc, string? reason, CancellationToken ct)
    {
        if (await db.Notifications.AnyAsync(n => n.SourceEventId == eventId, ct))
        {
            return;
        }

        var patient = await patients.GetContact(patientId, ct);
        var doctorNames = await doctors.GetDoctorNames([doctorId], ct);
        var message = $"{patient?.FullName ?? "A patient"} with {doctorNames.GetValueOrDefault(doctorId, "a doctor")}, {startsAtUtc:yyyy-MM-dd HH:mm} UTC";
        if (reason is not null)
        {
            message += $". Reason: {reason}";
        }

        var notification = Notification.Create(eventId, appointmentId, title, message, clock.UtcNow);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);

        await hub.Clients.All.SendAsync(
            NotificationsHub.ReceiveMethod,
            new NotificationDto(notification.Id, appointmentId, title, message, notification.CreatedAtUtc, null),
            ct);
    }
}

public sealed record ListNotifications(bool UnreadOnly) : IQuery<IReadOnlyList<NotificationDto>>;

internal sealed class ListNotificationsHandler(NotificationsDbContext db)
    : IQueryHandler<ListNotifications, IReadOnlyList<NotificationDto>>
{
    public async Task<Result<IReadOnlyList<NotificationDto>>> Handle(ListNotifications query, CancellationToken ct)
    {
        var notifications = db.Notifications.AsNoTracking();
        if (query.UnreadOnly)
        {
            notifications = notifications.Where(n => n.ReadAtUtc == null);
        }

        var items = await notifications
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(50)
            .Select(n => new NotificationDto(n.Id, n.AppointmentId, n.Title, n.Message, n.CreatedAtUtc, n.ReadAtUtc))
            .ToListAsync(ct);

        return Result<IReadOnlyList<NotificationDto>>.From(items);
    }
}

public sealed record MarkNotificationRead(Guid NotificationId) : ICommand;

internal sealed class MarkNotificationReadHandler(NotificationsDbContext db, ClinicClock clock)
    : ICommandHandler<MarkNotificationRead>
{
    public async Task<Result> Handle(MarkNotificationRead command, CancellationToken ct)
    {
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == command.NotificationId, ct);
        if (notification is null)
        {
            return Error.NotFound("notification.not_found", "Notification not found.");
        }

        notification.MarkRead(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed class NotificationsModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<NotificationsDbContext>(configuration, NotificationsDbContext.SchemaName);
        services.AddModuleHandlers(typeof(NotificationsModule).Assembly);
        services.AddScoped<IModuleInitializer, NotificationsInitializer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization(Policies.Staff);

        notifications.MapGet("/", async (bool? unreadOnly, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new ListNotifications(unreadOnly ?? false), ct)).ToHttp());

        notifications.MapPost("/{id:guid}/read", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new MarkNotificationRead(id), ct)).ToHttp());

        endpoints.MapHub<NotificationsHub>(NotificationsHub.Path);
    }
}

internal sealed class NotificationsInitializer(NotificationsDbContext db) : IModuleInitializer
{
    public Task Migrate(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public Task Seed(CancellationToken ct) => Task.CompletedTask;
}
