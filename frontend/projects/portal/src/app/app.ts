import { Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from '../../../shared/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink],
  template: `
    <header class="border-b border-stone-200 bg-white">
      <div class="mx-auto flex max-w-3xl items-center justify-between gap-2 px-4 py-3">
        <a routerLink="/" class="text-lg font-bold text-teal-800">ClinicFlow</a>
        <nav class="flex items-center gap-1 text-sm font-medium" aria-label="Main">
          <a routerLink="/book" class="rounded-lg px-2 py-2 hover:bg-stone-100">Book</a>
          <a routerLink="/visits" class="rounded-lg px-2 py-2 hover:bg-stone-100">My visits</a>
          @if (auth.isLoggedIn()) {
            <button class="rounded-lg px-2 py-2 hover:bg-stone-100" (click)="auth.logout()">Log out</button>
          }
        </nav>
      </div>
    </header>
    <main class="mx-auto max-w-3xl px-4 py-6"><router-outlet /></main>
    <footer class="mx-auto max-w-3xl px-4 pb-8 text-sm text-stone-500">
      A practice project. This clinic and all people here are made up.
    </footer>
  `,
})
export class App {
  protected readonly auth = inject(AuthService);
}
