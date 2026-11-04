export interface AppEnv {
  apiUrl: string;
  auth: { mode: 'dev' } | { mode: 'keycloak'; url: string; realm: string; clientId: string };
}

export interface WorkingHours {
  day: number;
  start: string;
  end: string;
}

export interface Doctor {
  id: string;
  fullName: string;
  specialty: string;
  isActive: boolean;
  workingHours: WorkingHours[];
}

export interface Treatment {
  id: string;
  name: string;
  durationMinutes: number;
  price: number;
}

export interface Patient {
  id: string;
  fullName: string;
  email: string;
  phone: string;
  registeredAtUtc: string;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export type AppointmentStatus = 'Booked' | 'Confirmed' | 'Completed' | 'Cancelled';

export interface Appointment {
  id: string;
  patientId: string;
  patientName: string;
  doctorId: string;
  doctorName: string;
  treatmentName: string;
  startsAtUtc: string;
  endsAtUtc: string;
  status: AppointmentStatus;
  cancelReason: string | null;
}

export interface DayStats {
  total: number;
  booked: number;
  confirmed: number;
  completed: number;
  cancelled: number;
}

export interface AppNotification {
  id: string;
  appointmentId: string;
  title: string;
  message: string;
  createdAtUtc: string;
  readAtUtc: string | null;
}
