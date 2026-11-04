import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../../shared/auth.service';
import { NotificationsService } from './notifications.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly notifications = inject(NotificationsService);
  protected readonly bellOpen = signal(false);

  protected readonly links = [
    { path: '/today', label: 'Today' },
    { path: '/patients', label: 'Patients' },
    { path: '/doctors', label: 'Doctors' },
  ];

  constructor() {
    effect(() => {
      if (this.auth.isStaff()) {
        void this.notifications.connect();
      }
    });
  }
}
