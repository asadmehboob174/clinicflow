import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { catchError, concatMap, forkJoin, map, of, switchMap, withLatestFrom } from 'rxjs';
import { ApiService, errorText } from '../../../../shared/api.service';
import { scheduleActions, scheduleFeature, ScheduleChange } from './schedule.state';

/** switchMap: when the date changes quickly, the old request is dropped and cannot overwrite the new day. */
export const load = createEffect(
  (actions = inject(Actions), store = inject(Store), api = inject(ApiService)) =>
    actions.pipe(
      ofType(scheduleActions.datePicked, scheduleActions.refresh),
      withLatestFrom(store.select(scheduleFeature.selectDate)),
      switchMap(([, date]) =>
        forkJoin({ appointments: api.daySchedule(date), stats: api.dayStats(date) }).pipe(
          map(result => scheduleActions.loadSucceeded(result)),
          catchError(error => of(scheduleActions.loadFailed({ error: errorText(error) }))),
        ),
      ),
    ),
  { functional: true },
);

/** concatMap: changes run in the order the user made them. */
export const change = createEffect(
  (actions = inject(Actions), api = inject(ApiService)) =>
    actions.pipe(
      ofType(scheduleActions.changeRequested),
      concatMap(({ change }) =>
        send(api, change).pipe(
          map(() => scheduleActions.refresh()),
          catchError(error => of(scheduleActions.changeFailed({ error: errorText(error) }))),
        ),
      ),
    ),
  { functional: true },
);

function send(api: ApiService, change: ScheduleChange) {
  switch (change.kind) {
    case 'confirm':
      return api.confirm(change.id);
    case 'complete':
      return api.complete(change.id);
    case 'cancel':
      return api.cancel(change.id, change.reason);
  }
}
