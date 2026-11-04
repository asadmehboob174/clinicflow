import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { catchError, combineLatest, map, of, startWith, switchMap } from 'rxjs';
import { ApiService, errorText } from '../../../shared/api.service';
import { AuthService } from '../../../shared/auth.service';
import { CLINIC_OFFSET, clinicDate } from '../../../shared/clinic-time';
import { Doctor, Patient, Treatment } from '../../../shared/models';
import { LoginBox } from './login-box';

@Component({
  selector: 'app-book',
  imports: [CurrencyPipe, DatePipe, ReactiveFormsModule, LoginBox],
  templateUrl: './book.page.html',
})
export class BookPage {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  protected readonly auth = inject(AuthService);
  protected readonly offset = CLINIC_OFFSET;
  protected readonly days = Array.from({ length: 14 }, (_, i) => clinicDate(i + 1));

  protected readonly treatments = toSignal(this.api.treatments(), { initialValue: [] as Treatment[] });
  protected readonly allDoctors = toSignal(this.api.doctors(), { initialValue: [] as Doctor[] });
  protected readonly doctors = computed(() => this.allDoctors().filter(d => d.isActive));

  protected readonly treatment = signal<Treatment | null>(null);
  protected readonly doctor = signal<Doctor | null>(null);
  protected readonly date = signal(this.days[0]);
  protected readonly slot = signal<string | null>(null);
  protected readonly profile = signal<Patient | null>(null);
  protected readonly needsProfile = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);
  private readonly refresh = signal(0);

  protected readonly profileForm = inject(NonNullableFormBuilder).group({
    fullName: ['', [Validators.required, Validators.maxLength(120)]],
    phone: ['', [Validators.required, Validators.pattern(/^\+?[0-9 ()-]{7,30}$/)]],
  });

  /** Any change to the choices starts a new search, and switchMap drops the old one. */
  protected readonly slots = toSignal(
    combineLatest([
      toObservable(this.treatment),
      toObservable(this.doctor),
      toObservable(this.date),
      toObservable(this.refresh),
    ]).pipe(
      switchMap(([treatment, doctor, date]) => {
        if (!treatment || !doctor) {
          return of({ loading: false, times: [] as string[] });
        }
        return this.api.slots(doctor.id, treatment.id, date).pipe(
          map(times => ({ loading: false, times })),
          startWith({ loading: true, times: [] as string[] }),
          catchError(() => of({ loading: false, times: [] as string[] })),
        );
      }),
    ),
    { initialValue: { loading: false, times: [] as string[] } },
  );

  constructor() {
    effect(() => {
      if (this.auth.isLoggedIn()) {
        this.loadProfile();
      }
    });
  }

  protected pick(next: { treatment?: Treatment; doctor?: Doctor; date?: string }): void {
    this.slot.set(null);
    this.error.set(null);
    if (next.treatment) this.treatment.set(next.treatment);
    if (next.doctor) this.doctor.set(next.doctor);
    if (next.date) this.date.set(next.date);
  }

  protected saveProfile(): void {
    const { fullName, phone } = this.profileForm.getRawValue();
    this.api.registerPatient(fullName, this.auth.email(), phone).subscribe({
      next: () => this.loadProfile(),
      error: error => this.error.set(errorText(error)),
    });
  }

  protected book(): void {
    const [treatment, doctor, slot, profile] = [this.treatment(), this.doctor(), this.slot(), this.profile()];
    if (!treatment || !doctor || !slot || !profile) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.api.book(profile.id, doctor.id, treatment.id, slot).subscribe({
      next: () => void this.router.navigate(['/visits']),
      error: error => {
        this.saving.set(false);
        this.error.set(errorText(error));
        this.slot.set(null);
        this.refresh.update(n => n + 1);
      },
    });
  }

  private loadProfile(): void {
    this.api
      .myProfile()
      .pipe(
        catchError((error: HttpErrorResponse) => {
          if (error.status === 404) {
            this.needsProfile.set(true);
            return of(null);
          }
          throw error;
        }),
      )
      .subscribe(profile => {
        this.profile.set(profile);
        if (profile) {
          this.needsProfile.set(false);
        }
      });
  }
}
