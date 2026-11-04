import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Store } from '@ngrx/store';
import { CLINIC_OFFSET } from '../../../../shared/clinic-time';
import { Appointment, AppointmentStatus } from '../../../../shared/models';
import { scheduleActions, scheduleFeature } from './schedule.state';

@Component({
  selector: 'app-today',
  imports: [DatePipe, FormsModule],
  templateUrl: './today.page.html',
})
export class TodayPage implements OnInit {
  private readonly store = inject(Store);

  protected readonly offset = CLINIC_OFFSET;
  protected readonly date = this.store.selectSignal(scheduleFeature.selectDate);
  protected readonly appointments = this.store.selectSignal(scheduleFeature.selectAppointments);
  protected readonly stats = this.store.selectSignal(scheduleFeature.selectStats);
  protected readonly loading = this.store.selectSignal(scheduleFeature.selectLoading);
  protected readonly error = this.store.selectSignal(scheduleFeature.selectError);

  protected readonly cancelling = signal<string | null>(null);
  protected reason = '';

  protected readonly statusStyle: Record<AppointmentStatus, string> = {
    Booked: 'bg-amber-400/15 text-amber-300',
    Confirmed: 'bg-teal-400/15 text-teal-300',
    Completed: 'bg-slate-500/20 text-slate-300',
    Cancelled: 'bg-red-400/15 text-red-300',
  };

  ngOnInit(): void {
    this.store.dispatch(scheduleActions.refresh());
  }

  protected pickDate(date: string): void {
    if (date) {
      this.store.dispatch(scheduleActions.datePicked({ date }));
    }
  }

  protected confirm(appointment: Appointment): void {
    this.store.dispatch(scheduleActions.changeRequested({ change: { kind: 'confirm', id: appointment.id } }));
  }

  protected complete(appointment: Appointment): void {
    this.store.dispatch(scheduleActions.changeRequested({ change: { kind: 'complete', id: appointment.id } }));
  }

  protected startCancel(appointment: Appointment): void {
    this.reason = '';
    this.cancelling.set(appointment.id);
  }

  protected cancel(appointment: Appointment): void {
    const reason = this.reason.trim();
    if (reason) {
      this.store.dispatch(scheduleActions.changeRequested({ change: { kind: 'cancel', id: appointment.id, reason } }));
      this.cancelling.set(null);
    }
  }
}
