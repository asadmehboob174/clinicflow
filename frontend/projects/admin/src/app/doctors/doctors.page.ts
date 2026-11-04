import { Component, inject, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { switchMap } from 'rxjs';
import { ApiService, errorText } from '../../../../shared/api.service';
import { AuthService } from '../../../../shared/auth.service';
import { Doctor } from '../../../../shared/models';

const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const WEEKDAYS = [1, 2, 3, 4, 5];

@Component({
  selector: 'app-doctors',
  imports: [ReactiveFormsModule],
  template: `
    <h1 class="text-2xl font-bold">Doctors</h1>

    @if (auth.isAdmin()) {
      <form class="panel mt-4 grid gap-3 p-4 md:grid-cols-[1fr_1fr_auto]" [formGroup]="form" (ngSubmit)="add()">
        <input class="field" formControlName="fullName" placeholder="Full name" aria-label="Full name" />
        <input class="field" formControlName="specialty" placeholder="Specialty" aria-label="Specialty" />
        <button class="btn btn-main" type="submit" [disabled]="form.invalid">Add doctor</button>
      </form>
    } @else {
      <p class="mt-2 text-sm text-slate-400">Only admins can change doctors.</p>
    }

    @if (error(); as message) {
      <p class="mt-3 text-sm text-red-300" role="alert">{{ message }}</p>
    }

    <section class="mt-4 grid gap-3 md:grid-cols-2">
      @for (doctor of doctors(); track doctor.id) {
        <article class="panel p-4" [class.opacity-60]="!doctor.isActive">
          <div class="flex items-start justify-between gap-2">
            <div>
              <p class="font-semibold">{{ doctor.fullName }}</p>
              <p class="text-sm text-slate-400">{{ doctor.specialty }}</p>
            </div>
            @if (!doctor.isActive) {
              <span class="rounded-full bg-slate-500/20 px-2 py-0.5 text-xs text-slate-300">Inactive</span>
            } @else if (auth.isAdmin()) {
              <button class="btn btn-danger" (click)="deactivate(doctor)">Deactivate</button>
            }
          </div>
          <p class="mt-3 text-sm text-slate-300">{{ hours(doctor) }}</p>
        </article>
      }
    </section>
  `,
})
export class DoctorsPage implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly auth = inject(AuthService);
  protected readonly doctors = signal<Doctor[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    fullName: ['', [Validators.required, Validators.maxLength(120)]],
    specialty: ['', [Validators.required, Validators.maxLength(80)]],
  });

  ngOnInit(): void {
    this.load();
  }

  protected hours(doctor: Doctor): string {
    if (doctor.workingHours.length === 0) {
      return 'No working hours set';
    }
    return doctor.workingHours
      .map(h => `${DAYS[h.day]} ${h.start.slice(0, 5)}-${h.end.slice(0, 5)}`)
      .join(', ');
  }

  protected add(): void {
    const { fullName, specialty } = this.form.getRawValue();
    const hours = WEEKDAYS.map(day => ({ day, start: '09:00:00', end: '17:00:00' }));
    this.api
      .createDoctor(fullName, specialty)
      .pipe(switchMap(id => this.api.setWorkingHours(id, hours)))
      .subscribe({
        next: () => {
          this.form.reset();
          this.load();
        },
        error: error => this.error.set(errorText(error)),
      });
  }

  protected deactivate(doctor: Doctor): void {
    if (confirm(`Deactivate ${doctor.fullName}? Their upcoming visits will be cancelled.`)) {
      this.api.deactivateDoctor(doctor.id).subscribe({
        next: () => this.load(),
        error: error => this.error.set(errorText(error)),
      });
    }
  }

  private load(): void {
    this.api.doctors().subscribe(doctors => this.doctors.set(doctors));
  }
}
