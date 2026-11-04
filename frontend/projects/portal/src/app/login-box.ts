import { Component, inject, input } from '@angular/core';
import { AuthService } from '../../../shared/auth.service';

@Component({
  selector: 'app-login-box',
  template: `
    <section class="card p-6 text-center">
      <p class="text-lg font-semibold">{{ message() }}</p>
      @if (auth.usesDevLogin) {
        <button class="btn btn-main mt-4" (click)="auth.login('dev:patient:sara@example.com')">Log in as Sara (sample patient)</button>
      } @else {
        <button class="btn btn-main mt-4" (click)="auth.login()">Log in</button>
      }
    </section>
  `,
})
export class LoginBox {
  protected readonly auth = inject(AuthService);
  readonly message = input.required<string>();
}
