import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideStore } from '@ngrx/store';
import { authInterceptor, AuthService } from '../../../shared/auth.service';
import { routes } from './app.routes';
import * as scheduleEffects from './schedule/schedule.effects';
import { scheduleFeature } from './schedule/schedule.state';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideAppInitializer(() => inject(AuthService).init()),
    provideStore({ [scheduleFeature.name]: scheduleFeature.reducer }),
    provideEffects(scheduleEffects),
  ],
};
