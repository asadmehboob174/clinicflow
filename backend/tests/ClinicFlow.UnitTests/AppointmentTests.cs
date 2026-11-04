using ClinicFlow.BuildingBlocks.Domain;
using ClinicFlow.Modules.Appointments.Domain;

namespace ClinicFlow.UnitTests;

public class AppointmentTests
{
    private static readonly DateTime Now = new(2030, 1, 7, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = Now.AddDays(1);

    private static Appointment Booked(int minutes = 45) =>
        Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Start, minutes, Now).Value;

    [Fact]
    public void Booking_holds_one_block_per_15_minutes_and_raises_an_event()
    {
        var appointment = Booked(45);

        appointment.Blocks.Select(b => b.StartsAtUtc).ShouldBe([Start, Start.AddMinutes(15), Start.AddMinutes(30)]);
        appointment.EndsAtUtc.ShouldBe(Start.AddMinutes(45));
        appointment.DomainEvents.Single().ShouldBeOfType<AppointmentBookedDomainEvent>();
    }

    [Fact]
    public void Cannot_book_in_the_past()
    {
        var result = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(-15), 30, Now);

        result.Error!.Code.ShouldBe("appointment.in_past");
    }

    [Fact]
    public void Cannot_book_off_the_15_minute_grid()
    {
        var result = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Start.AddMinutes(7), 30, Now);

        result.Error!.Code.ShouldBe("appointment.off_grid");
    }

    [Fact]
    public void Cancel_frees_the_blocks()
    {
        var appointment = Booked();

        appointment.Cancel("Changed plans", byStaff: false, Now).IsSuccess.ShouldBeTrue();

        appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
        appointment.Blocks.ShouldBeEmpty();
    }

    [Fact]
    public void Patient_cannot_cancel_inside_two_hours_but_staff_can()
    {
        var soon = Start.AddHours(-1);

        Booked().Cancel("Late", byStaff: false, soon).Error!.Code.ShouldBe("appointment.too_late");
        Booked().Cancel("Late", byStaff: true, soon).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Only_a_booked_appointment_can_be_confirmed()
    {
        var appointment = Booked();
        appointment.Confirm().IsSuccess.ShouldBeTrue();

        var again = appointment.Confirm();

        again.Error!.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Cannot_complete_before_it_starts()
    {
        var appointment = Booked();
        appointment.Confirm();

        appointment.Complete(Now).Error!.Code.ShouldBe("appointment.not_started");
        appointment.Complete(Start).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Completed_appointment_cannot_be_cancelled()
    {
        var appointment = Booked();
        appointment.Confirm();
        appointment.Complete(Start);

        appointment.Cancel("Too late", byStaff: true, Start).Error!.Type.ShouldBe(ErrorType.Conflict);
    }
}
