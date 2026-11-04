using ClinicFlow.Modules.Doctors.Domain;

namespace ClinicFlow.UnitTests;

public class DoctorTests
{
    private static readonly TimeOnly Nine = new(9, 0);
    private static readonly TimeOnly Noon = new(12, 0);
    private static readonly TimeOnly Five = new(17, 0);

    [Fact]
    public void Two_shifts_on_one_day_are_fine_when_they_do_not_overlap()
    {
        var doctor = Doctor.Create("Dr. Test", "General");

        var result = doctor.SetWorkingHours(
        [
            new(DayOfWeek.Monday, Nine, Noon),
            new(DayOfWeek.Monday, Noon, Five),
        ]);

        result.IsSuccess.ShouldBeTrue();
        doctor.WorkingHours.Count.ShouldBe(2);
    }

    [Fact]
    public void Overlapping_shifts_are_refused()
    {
        var doctor = Doctor.Create("Dr. Test", "General");

        var result = doctor.SetWorkingHours(
        [
            new(DayOfWeek.Monday, Nine, Five),
            new(DayOfWeek.Monday, Noon, Five),
        ]);

        result.Error!.Code.ShouldBe("doctor.hours_overlap");
        doctor.WorkingHours.ShouldBeEmpty();
    }

    [Fact]
    public void Shift_must_end_after_it_starts()
    {
        var doctor = Doctor.Create("Dr. Test", "General");

        doctor.SetWorkingHours([new(DayOfWeek.Monday, Five, Nine)]).Error!.Code.ShouldBe("doctor.hours_invalid");
    }

    [Fact]
    public void Deactivating_twice_is_a_conflict_and_raises_one_event()
    {
        var doctor = Doctor.Create("Dr. Test", "General");

        doctor.Deactivate().IsSuccess.ShouldBeTrue();
        doctor.Deactivate().IsSuccess.ShouldBeFalse();

        doctor.DomainEvents.Count.ShouldBe(1);
    }
}
