import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { debounceTime, distinctUntilChanged, startWith, Subject, switchMap } from 'rxjs';
import { ApiService, errorText } from '../../../../shared/api.service';
import { Paged, Patient } from '../../../../shared/models';

@Component({
  selector: 'app-patients',
  imports: [DatePipe, ReactiveFormsModule],
  template: `
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h1 class="text-2xl font-bold">Patients</h1>
      <button class="btn btn-main" (click)="adding.set(!adding())">{{ adding() ? 'Close' : 'Add patient' }}</button>
    </div>

    @if (adding()) {
      <form class="panel mt-4 grid gap-3 p-4 md:grid-cols-4" [formGroup]="form" (ngSubmit)="add()">
        <input class="field" formControlName="fullName" placeholder="Full name" aria-label="Full name" />
        <input class="field" formControlName="email" placeholder="Email" aria-label="Email" type="email" />
        <input class="field" formControlName="phone" placeholder="Phone" aria-label="Phone" />
        <button class="btn btn-main" type="submit" [disabled]="form.invalid">Save</button>
        @if (error(); as message) {
          <p class="text-sm text-red-300 md:col-span-4" role="alert">{{ message }}</p>
        }
      </form>
    }

    <input class="field mt-4" [formControl]="search" placeholder="Search by name or email" aria-label="Search patients" />

    <section class="mt-4 grid gap-2">
      @for (patient of result()?.items; track patient.id) {
        <article class="panel flex flex-wrap items-center justify-between gap-2 p-3">
          <div class="min-w-0">
            <p class="font-semibold">{{ patient.fullName }}</p>
            <p class="truncate text-sm text-slate-400">{{ patient.email }} · {{ patient.phone }}</p>
          </div>
          <p class="text-xs text-slate-500">Joined {{ patient.registeredAtUtc | date: 'mediumDate' }}</p>
        </article>
      } @empty {
        <p class="panel p-8 text-center text-slate-400">No patients found.</p>
      }
    </section>
    <p class="mt-3 text-sm text-slate-500">{{ result()?.total ?? 0 }} in total</p>
  `,
})
export class PatientsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly reload = new Subject<void>();

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly form: FormGroup = inject(NonNullableFormBuilder).group({
    fullName: ['', [Validators.required, Validators.maxLength(120)]],
    email: ['', [Validators.required, Validators.email]],
    phone: ['', [Validators.required, Validators.pattern(/^\+?[0-9 ()-]{7,30}$/)]],
  });
  protected readonly result = signal<Paged<Patient> | null>(null);
  protected readonly adding = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.search.valueChanges
      .pipe(
        startWith(''),
        debounceTime(250),
        distinctUntilChanged(),
        switchMap(term => this.api.patients(term, 1)),
        takeUntilDestroyed(),
      )
      .subscribe(result => this.result.set(result));

    this.reload
      .pipe(
        switchMap(() => this.api.patients(this.search.value, 1)),
        takeUntilDestroyed(),
      )
      .subscribe(result => this.result.set(result));
  }

  ngOnInit(): void {
    this.reload.next();
  }

  protected add(): void {
    const { fullName, email, phone } = this.form.getRawValue();
    this.error.set(null);
    this.api.registerPatient(fullName, email, phone).subscribe({
      next: () => {
        this.form.reset();
        this.adding.set(false);
        this.reload.next();
      },
      error: error => this.error.set(errorText(error)),
    });
  }
}
