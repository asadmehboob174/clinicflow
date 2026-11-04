import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService, errorText } from '../../../shared/api.service';
import { AuthService } from '../../../shared/auth.service';
import { CLINIC_OFFSET } from '../../../shared/clinic-time';
import { Appointment } from '../../../shared/models';
import { LoginBox } from './login-box';

@Component({
  selector: 'app-visits',
  imports: [DatePipe, RouterLink, LoginBox],
  template: `
    <h1 class="text-2xl font-bold">My visits</h1>

    @if (!auth.isLoggedIn()) {
      <div class="mt-4"><app-login-box message="Please log in to see your visits." /></div>
    } @else {
      @if (error(); as message) {
        <p class="mt-4 rounded-xl bg-red-50 p-3 text-red-800" role="alert">{{ message }}</p>
      }

      <section class="mt-4 grid gap-3">
        @for (visit of visits(); track visit.id) {
          <article class="card p-4" data-testid="visit">
            <div class="flex flex-wrap items-start justify-between gap-2">
              <div>
                <p class="font-semibold">{{ visit.treatmentName }}</p>
                <p class="text-sm text-stone-600">{{ visit.doctorName }}</p>
                <p class="mt-1 text-sm">{{ visit.startsAtUtc | date: 'EEE d MMM, HH:mm' : offset }}</p>
              </div>
              <span class="rounded-full bg-stone-100 px-2.5 py-0.5 text-sm font-medium">{{ visit.status }}</span>
            </div>
            @if (visit.cancelReason) {
              <p class="mt-2 text-sm text-stone-600">{{ visit.cancelReason }}</p>
            }
            @if (visit.status === 'Booked' || visit.status === 'Confirmed') {
              <button class="btn btn-soft mt-3" (click)="cancel(visit)">Cancel visit</button>
            }
          </article>
        } @empty {
          <div class="card p-6 text-center">
            <p>You have no visits yet.</p>
            <a routerLink="/book" class="btn btn-main mt-3">Book a visit</a>
          </div>
        }
      </section>
    }
  `,
})
export class VisitsPage {
  private readonly api = inject(ApiService);

  protected readonly auth = inject(AuthService);
  protected readonly offset = CLINIC_OFFSET;
  protected readonly visits = signal<Appointment[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      if (this.auth.isLoggedIn()) {
        this.load();
      }
    });
  }

  protected cancel(visit: Appointment): void {
    if (confirm('Cancel this visit?')) {
      this.api.cancel(visit.id, 'Cancelled by the patient').subscribe({
        next: () => this.load(),
        error: error => this.error.set(errorText(error)),
      });
    }
  }

  private load(): void {
    this.api.myAppointments().subscribe({
      next: visits => this.visits.set(visits),
      error: error => this.error.set(errorText(error)),
    });
  }
}
