using ClinicFlow.Modules.Appointments.Domain;

namespace ClinicFlow.UnitTests;

public class SlotFinderTests
{
    private static readonly DateTime Nine = new(2030, 1, 7, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LongAgo = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Shift OneHour = new(Nine, Nine.AddHours(1));

    [Fact]
    public void Free_shift_gives_a_slot_every_15_minutes()
    {
        var slots = SlotFinder.Find([OneHour], new HashSet<DateTime>(), 15, LongAgo);

        slots.ShouldBe([Nine, Nine.AddMinutes(15), Nine.AddMinutes(30), Nine.AddMinutes(45)]);
    }

    [Fact]
    public void Slot_must_finish_before_the_shift_ends()
    {
        var slots = SlotFinder.Find([OneHour], new HashSet<DateTime>(), 45, LongAgo);

        slots.ShouldBe([Nine, Nine.AddMinutes(15)]);
    }

    [Fact]
    public void Long_treatment_needs_every_block_to_be_free()
    {
        var busy = new HashSet<DateTime> { Nine.AddMinutes(30) };

        var slots = SlotFinder.Find([OneHour], busy, 30, LongAgo);

        slots.ShouldBe([Nine]);
    }

    [Fact]
    public void Past_times_are_not_offered()
    {
        var slots = SlotFinder.Find([OneHour], new HashSet<DateTime>(), 15, Nine.AddMinutes(20));

        slots.ShouldBe([Nine.AddMinutes(30), Nine.AddMinutes(45)]);
    }
}
