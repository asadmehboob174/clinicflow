using ClinicFlow.BuildingBlocks.Cqrs;
using ClinicFlow.BuildingBlocks.Domain;
using ClinicFlow.BuildingBlocks.Web;
using ClinicFlow.Modules.Doctors.Contracts;
using ClinicFlow.Modules.Doctors.Domain;
using ClinicFlow.Modules.Doctors.Infrastructure;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace ClinicFlow.Modules.Doctors.Features;

public sealed record DoctorDto(Guid Id, string FullName, string Specialty, bool IsActive, IReadOnlyList<WorkingHoursDto> WorkingHours);

public sealed record TreatmentDto(Guid Id, string Name, int DurationMinutes, decimal Price);

internal static class CacheKeys
{
    public const string Doctors = "doctors:all";
    public const string Treatments = "treatments:all";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
}

// Create doctor

public sealed record CreateDoctor(string FullName, string Specialty) : ICommand<Guid>;

internal sealed class CreateDoctorValidator : AbstractValidator<CreateDoctor>
{
    public CreateDoctorValidator()
    {
        RuleFor(c => c.FullName).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Specialty).NotEmpty().MaximumLength(80);
    }
}

internal sealed class CreateDoctorHandler(DoctorsDbContext db, IDistributedCache cache) : ICommandHandler<CreateDoctor, Guid>
{
    public async Task<Result<Guid>> Handle(CreateDoctor command, CancellationToken ct)
    {
        var doctor = Doctor.Create(command.FullName, command.Specialty);
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.Doctors, ct);
        return doctor.Id;
    }
}

// Set working hours

public sealed record SetWorkingHours(Guid DoctorId, IReadOnlyList<WorkingHoursDto> Hours) : ICommand;

internal sealed class SetWorkingHoursHandler(DoctorsDbContext db, IDistributedCache cache) : ICommandHandler<SetWorkingHours>
{
    public async Task<Result> Handle(SetWorkingHours command, CancellationToken ct)
    {
        var doctor = await db.Doctors.FirstOrDefaultAsync(d => d.Id == command.DoctorId, ct);
        if (doctor is null)
        {
            return Error.NotFound("doctor.not_found", "Doctor not found.");
        }

        var result = doctor.SetWorkingHours(command.Hours.Select(h => new WorkingHours(h.Day, h.Start, h.End)).ToList());
        if (!result.IsSuccess)
        {
            return result;
        }

        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.Doctors, ct);
        return Result.Success();
    }
}

// Deactivate doctor

public sealed record DeactivateDoctor(Guid DoctorId) : ICommand;

internal sealed class DeactivateDoctorHandler(DoctorsDbContext db, IDistributedCache cache) : ICommandHandler<DeactivateDoctor>
{
    public async Task<Result> Handle(DeactivateDoctor command, CancellationToken ct)
    {
        var doctor = await db.Doctors.FirstOrDefaultAsync(d => d.Id == command.DoctorId, ct);
        if (doctor is null)
        {
            return Error.NotFound("doctor.not_found", "Doctor not found.");
        }

        var result = doctor.Deactivate();
        if (!result.IsSuccess)
        {
            return result;
        }

        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.Doctors, ct);
        return Result.Success();
    }
}

// List doctors

public sealed record ListDoctors : IQuery<IReadOnlyList<DoctorDto>>;

internal sealed class ListDoctorsHandler(DoctorsDbContext db, IDistributedCache cache)
    : IQueryHandler<ListDoctors, IReadOnlyList<DoctorDto>>
{
    public async Task<Result<IReadOnlyList<DoctorDto>>> Handle(ListDoctors query, CancellationToken ct)
    {
        var doctors = await cache.GetOrCreate(CacheKeys.Doctors, CacheKeys.Lifetime, async token =>
        {
            var rows = await db.Doctors.AsNoTracking().OrderBy(d => d.FullName).ToListAsync(token);
            return rows
                .Select(d => new DoctorDto(
                    d.Id,
                    d.FullName,
                    d.Specialty,
                    d.IsActive,
                    d.WorkingHours.OrderBy(h => h.Day).ThenBy(h => h.Start)
                        .Select(h => new WorkingHoursDto(h.Day, h.Start, h.End)).ToList()))
                .ToList();
        }, ct);

        return doctors;
    }
}

// Treatments

public sealed record CreateTreatment(string Name, int DurationMinutes, decimal Price) : ICommand<Guid>;

internal sealed class CreateTreatmentValidator : AbstractValidator<CreateTreatment>
{
    public CreateTreatmentValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(120);
        RuleFor(c => c.DurationMinutes)
            .InclusiveBetween(15, 240)
            .Must(minutes => minutes % 15 == 0)
            .WithMessage("Duration must be a multiple of 15 minutes.");
        RuleFor(c => c.Price).GreaterThanOrEqualTo(0);
    }
}

internal sealed class CreateTreatmentHandler(DoctorsDbContext db, IDistributedCache cache) : ICommandHandler<CreateTreatment, Guid>
{
    public async Task<Result<Guid>> Handle(CreateTreatment command, CancellationToken ct)
    {
        var treatment = Treatment.Create(command.Name, command.DurationMinutes, command.Price);
        db.Treatments.Add(treatment);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.Treatments, ct);
        return treatment.Id;
    }
}

public sealed record ListTreatments : IQuery<IReadOnlyList<TreatmentDto>>;

internal sealed class ListTreatmentsHandler(DoctorsDbContext db, IDistributedCache cache)
    : IQueryHandler<ListTreatments, IReadOnlyList<TreatmentDto>>
{
    public async Task<Result<IReadOnlyList<TreatmentDto>>> Handle(ListTreatments query, CancellationToken ct)
    {
        var treatments = await cache.GetOrCreate(CacheKeys.Treatments, CacheKeys.Lifetime, async token =>
        {
            var rows = await db.Treatments.AsNoTracking().ToListAsync(token);
            return rows
                .OrderBy(t => t.Name)
                .Select(t => new TreatmentDto(t.Id, t.Name, t.DurationMinutes, t.Price))
                .ToList();
        }, ct);

        return treatments;
    }
}
