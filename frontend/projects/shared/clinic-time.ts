/** The sample clinic is in Dubai. Times show in clinic time, wherever the viewer is. */
export const CLINIC_OFFSET = '+0400';
const OFFSET_HOURS = 4;

export function clinicDate(daysFromToday = 0): string {
  const now = new Date(Date.now() + OFFSET_HOURS * 3_600_000 + daysFromToday * 86_400_000);
  return now.toISOString().slice(0, 10);
}
