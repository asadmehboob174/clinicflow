using ClinicFlow.BuildingBlocks.Domain;

namespace ClinicFlow.Modules.Doctors.Domain;

public sealed record WorkingHours(DayOfWeek Day, TimeOnly Start, TimeOnly End);

public sealed record DoctorDeactivatedDomainEvent(Guid DoctorId) : IDomainEvent;

public sealed class Doctor : AggregateRoot<Guid>
{
    private readonly List<WorkingHours> _workingHours = [];

    private Doctor()
    {
    }

    public string FullName { get; private set; } = string.Empty;
    public string Specialty { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public IReadOnlyList<WorkingHours> WorkingHours => _workingHours;

    public static Doctor Create(string fullName, string specialty) => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = fullName.Trim(),
        Specialty = specialty.Trim(),
        IsActive = true,
    };

    public Result SetWorkingHours(IReadOnlyCollection<WorkingHours> hours)
    {
        if (hours.Any(h => h.Start >= h.End))
        {
            return Error.Validation("doctor.hours_invalid", "Each shift must end after it starts.");
        }

        var overlaps = hours
            .GroupBy(h => h.Day)
            .SelectMany(day => day.OrderBy(h => h.Start).Zip(day.OrderBy(h => h.Start).Skip(1)))
            .Any(pair => pair.Second.Start < pair.First.End);

        if (overlaps)
        {
            return Error.Validation("doctor.hours_overlap", "Shifts on the same day cannot overlap.");
        }

        _workingHours.Clear();
        _workingHours.AddRange(hours);
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
        {
            return Error.Conflict("doctor.already_inactive", "This doctor is already inactive.");
        }

        IsActive = false;
        Raise(new DoctorDeactivatedDomainEvent(Id));
        return Result.Success();
    }
}

public sealed class Treatment : AggregateRoot<Guid>
{
    private Treatment()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public int DurationMinutes { get; private set; }
    public decimal Price { get; private set; }

    public static Treatment Create(string name, int durationMinutes, decimal price) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = name.Trim(),
        DurationMinutes = durationMinutes,
        Price = price,
    };
}
