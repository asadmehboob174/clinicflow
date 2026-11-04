using ClinicFlow.BuildingBlocks.Domain;
using ClinicFlow.BuildingBlocks.Messaging;
using ClinicFlow.BuildingBlocks.Persistence;
using ClinicFlow.Modules.Appointments.Contracts;
using ClinicFlow.Modules.Appointments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicFlow.Modules.Appointments.Infrastructure;

public sealed class AppointmentsDbContext(DbContextOptions<AppointmentsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "appointments";

    protected override string Schema => SchemaName;

    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override IntegrationEvent? ToIntegrationEvent(IDomainEvent domainEvent) => domainEvent switch
    {
        AppointmentBookedDomainEvent { Appointment: var a } =>
            new AppointmentBooked(a.Id, a.PatientId, a.DoctorId, a.StartsAtUtc),
        AppointmentConfirmedDomainEvent { Appointment: var a } =>
            new AppointmentConfirmed(a.Id, a.PatientId, a.DoctorId, a.StartsAtUtc),
        AppointmentCancelledDomainEvent { Appointment: var a, Reason: var reason } =>
            new AppointmentCancelled(a.Id, a.PatientId, a.DoctorId, a.StartsAtUtc, reason),
        _ => null,
    };
}

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> appointment)
    {
        appointment.ToTable("Appointments");
        appointment.HasKey(a => a.Id);
        appointment.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        appointment.Property(a => a.CancelReason).HasMaxLength(300);
        appointment.HasIndex(a => new { a.DoctorId, a.StartsAtUtc });
        appointment.HasIndex(a => a.PatientId);
        appointment.Ignore(a => a.DomainEvents);

        appointment.OwnsMany(a => a.Blocks, block =>
        {
            block.ToTable("SlotBlocks");
            block.WithOwner().HasForeignKey("AppointmentId");
            block.Property<int>("Id");
            block.HasKey("Id");
            block.HasIndex(b => new { b.DoctorId, b.StartsAtUtc }).IsUnique();
        });
        appointment.Navigation(a => a.Blocks).HasField("_blocks").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
