import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { env } from './env';
import { Appointment, AppNotification, DayStats, Doctor, Paged, Patient, Treatment, WorkingHours } from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly url = `${env.apiUrl}/api`;

  doctors(): Observable<Doctor[]> {
    return this.http.get<Doctor[]>(`${this.url}/doctors`);
  }

  createDoctor(fullName: string, specialty: string): Observable<string> {
    return this.http.post<string>(`${this.url}/doctors`, { fullName, specialty });
  }

  setWorkingHours(doctorId: string, hours: WorkingHours[]): Observable<void> {
    return this.http.put<void>(`${this.url}/doctors/${doctorId}/working-hours`, { hours });
  }

  deactivateDoctor(doctorId: string): Observable<void> {
    return this.http.post<void>(`${this.url}/doctors/${doctorId}/deactivate`, null);
  }

  treatments(): Observable<Treatment[]> {
    return this.http.get<Treatment[]>(`${this.url}/treatments`);
  }

  patients(search: string, page: number): Observable<Paged<Patient>> {
    const params = new HttpParams().set('search', search).set('page', page);
    return this.http.get<Paged<Patient>>(`${this.url}/patients`, { params });
  }

  myProfile(): Observable<Patient> {
    return this.http.get<Patient>(`${this.url}/patients/me`);
  }

  registerPatient(fullName: string, email: string, phone: string): Observable<string> {
    return this.http.post<string>(`${this.url}/patients`, { fullName, email, phone });
  }

  slots(doctorId: string, treatmentId: string, date: string): Observable<string[]> {
    const params = new HttpParams().set('doctorId', doctorId).set('treatmentId', treatmentId).set('date', date);
    return this.http.get<string[]>(`${this.url}/appointments/slots`, { params });
  }

  book(patientId: string, doctorId: string, treatmentId: string, startsAtUtc: string): Observable<string> {
    return this.http.post<string>(`${this.url}/appointments`, { patientId, doctorId, treatmentId, startsAtUtc });
  }

  daySchedule(date: string): Observable<Appointment[]> {
    return this.http.get<Appointment[]>(`${this.url}/appointments/day`, { params: { date } });
  }

  dayStats(date: string): Observable<DayStats> {
    return this.http.get<DayStats>(`${this.url}/appointments/stats`, { params: { date } });
  }

  myAppointments(): Observable<Appointment[]> {
    return this.http.get<Appointment[]>(`${this.url}/appointments/mine`);
  }

  confirm(id: string): Observable<void> {
    return this.http.post<void>(`${this.url}/appointments/${id}/confirm`, null);
  }

  complete(id: string): Observable<void> {
    return this.http.post<void>(`${this.url}/appointments/${id}/complete`, null);
  }

  cancel(id: string, reason: string): Observable<void> {
    return this.http.post<void>(`${this.url}/appointments/${id}/cancel`, { reason });
  }

  notifications(): Observable<AppNotification[]> {
    return this.http.get<AppNotification[]>(`${this.url}/notifications`);
  }

  markRead(id: string): Observable<void> {
    return this.http.post<void>(`${this.url}/notifications/${id}/read`, null);
  }
}

/** Turns an API problem response into one short line for the screen. */
export function errorText(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return 'Cannot reach the server. Please try again.';
    }
    return error.error?.detail ?? 'Something went wrong. Please try again.';
  }
  return 'Something went wrong. Please try again.';
}
