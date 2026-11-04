using ClinicFlow.BuildingBlocks.Domain;
using ClinicFlow.BuildingBlocks.Messaging;
using ClinicFlow.BuildingBlocks.Persistence;
using ClinicFlow.Modules.Doctors.Contracts;
using ClinicFlow.Modules.Doctors.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicFlow.Modules.Doctors.Infrastructure;

public sealed class DoctorsDbContext(DbContextOptions<DoctorsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "doctors";

    protected override string Schema => SchemaName;

    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<Treatment> Treatments => Set<Treatment>();

    protected override IntegrationEvent? ToIntegrationEvent(IDomainEvent domainEvent) => domainEvent switch
    {
        DoctorDeactivatedDomainEvent e => new DoctorDeactivated(e.DoctorId),
        _ => null,
    };
}

internal sealed class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> doctor)
    {
        doctor.ToTable("Doctors");
        doctor.HasKey(d => d.Id);
        doctor.Property(d => d.FullName).HasMaxLength(120);
        doctor.Property(d => d.Specialty).HasMaxLength(80);
        doctor.Ignore(d => d.DomainEvents);
        doctor.OwnsMany(d => d.WorkingHours, hours =>
        {
            hours.ToTable("WorkingHours");
            hours.WithOwner().HasForeignKey("DoctorId");
            hours.Property<int>("Id");
            hours.HasKey("Id");
        });
        doctor.Navigation(d => d.WorkingHours).HasField("_workingHours").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TreatmentConfiguration : IEntityTypeConfiguration<Treatment>
{
    public void Configure(EntityTypeBuilder<Treatment> treatment)
    {
        treatment.ToTable("Treatments");
        treatment.HasKey(t => t.Id);
        treatment.Property(t => t.Name).HasMaxLength(120);
        treatment.Property(t => t.Price).HasPrecision(10, 2);
        treatment.Ignore(t => t.DomainEvents);
    }
}
