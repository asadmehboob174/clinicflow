import { computed, inject, Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Store } from '@ngrx/store';
import { ApiService } from '../../../shared/api.service';
import { AuthService } from '../../../shared/auth.service';
import { env } from '../../../shared/env';
import { AppNotification } from '../../../shared/models';
import { scheduleActions } from './schedule/schedule.state';

@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly store = inject(Store);
  private connection?: HubConnection;

  readonly items = signal<AppNotification[]>([]);
  readonly live = signal(false);
  readonly unread = computed(() => this.items().filter(item => !item.readAtUtc).length);

  async connect(): Promise<void> {
    if (this.connection) {
      return;
    }

    this.api.notifications().subscribe(items => this.items.set(items));

    this.connection = new HubConnectionBuilder()
      .withUrl(`${env.apiUrl}/hubs/notifications`, { accessTokenFactory: async () => (await this.auth.getToken()) ?? '' })
      .withAutomaticReconnect()
      .build();

    this.connection.on('notification', (item: AppNotification) => {
      this.items.update(items => [item, ...items]);
      this.store.dispatch(scheduleActions.refresh());
    });
    this.connection.onreconnected(() => this.live.set(true));
    this.connection.onreconnecting(() => this.live.set(false));
    this.connection.onclose(() => this.live.set(false));

    try {
      await this.connection.start();
      this.live.set(this.connection.state === HubConnectionState.Connected);
    } catch {
      this.live.set(false);
    }
  }

  markRead(item: AppNotification): void {
    if (item.readAtUtc) {
      return;
    }

    this.api.markRead(item.id).subscribe(() => {
      const readAtUtc = new Date().toISOString();
      this.items.update(items => items.map(i => (i.id === item.id ? { ...i, readAtUtc } : i)));
    });
  }
}
