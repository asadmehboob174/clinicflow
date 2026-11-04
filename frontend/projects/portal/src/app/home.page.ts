import { CurrencyPipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../shared/api.service';

@Component({
  selector: 'app-home',
  imports: [RouterLink, CurrencyPipe],
  template: `
    <section class="py-6">
      <h1 class="text-3xl font-bold md:text-4xl">Book your dentist visit in one minute</h1>
      <p class="mt-2 text-stone-600">Pick a treatment, a doctor and a time. No phone calls.</p>
      <a routerLink="/book" class="btn btn-main mt-5">Book a visit</a>
    </section>

    <h2 class="mt-4 text-xl font-bold">Treatments</h2>
    <section class="mt-3 grid gap-3 sm:grid-cols-2">
      @for (treatment of treatments(); track treatment.id) {
        <article class="card p-4">
          <p class="font-semibold">{{ treatment.name }}</p>
          <p class="text-sm text-stone-600">
            {{ treatment.durationMinutes }} min · {{ treatment.price | currency: 'USD' : 'symbol' : '1.0-0' }}
          </p>
        </article>
      }
    </section>

    <h2 class="mt-8 text-xl font-bold">Our doctors</h2>
    <section class="mt-3 grid gap-3 sm:grid-cols-2">
      @for (doctor of doctors(); track doctor.id) {
        @if (doctor.isActive) {
          <article class="card p-4">
            <p class="font-semibold">{{ doctor.fullName }}</p>
            <p class="text-sm text-stone-600">{{ doctor.specialty }}</p>
          </article>
        }
      }
    </section>
  `,
})
export class HomePage {
  private readonly api = inject(ApiService);

  protected readonly treatments = toSignal(this.api.treatments(), { initialValue: [] });
  protected readonly doctors = toSignal(this.api.doctors(), { initialValue: [] });
}
