namespace ClinicFlow.Modules.Appointments.Domain;

public sealed record Shift(DateTime StartUtc, DateTime EndUtc);

/// <summary>Pure logic, so it is easy to unit test. Knows nothing about the database.</summary>
public static class SlotFinder
{
    public static IReadOnlyList<DateTime> Find(
        IEnumerable<Shift> shifts, IReadOnlySet<DateTime> busyBlocks, int durationMinutes, DateTime nowUtc)
    {
        var blocksNeeded = durationMinutes / Appointment.BlockMinutes;
        var slots = new List<DateTime>();

        foreach (var shift in shifts.OrderBy(s => s.StartUtc))
        {
            for (var start = shift.StartUtc;
                 start.AddMinutes(durationMinutes) <= shift.EndUtc;
                 start = start.AddMinutes(Appointment.BlockMinutes))
            {
                var isFree = Enumerable.Range(0, blocksNeeded)
                    .All(i => !busyBlocks.Contains(start.AddMinutes(i * Appointment.BlockMinutes)));

                if (start > nowUtc && isFree)
                {
                    slots.Add(start);
                }
            }
        }

        return slots;
    }
}
