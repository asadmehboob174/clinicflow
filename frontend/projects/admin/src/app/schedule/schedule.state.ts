import { createActionGroup, createFeature, createReducer, emptyProps, on, props } from '@ngrx/store';
import { clinicDate } from '../../../../shared/clinic-time';
import { Appointment, DayStats } from '../../../../shared/models';

export type ScheduleChange =
  | { kind: 'confirm'; id: string }
  | { kind: 'complete'; id: string }
  | { kind: 'cancel'; id: string; reason: string };

export const scheduleActions = createActionGroup({
  source: 'Schedule',
  events: {
    'Date Picked': props<{ date: string }>(),
    Refresh: emptyProps(),
    'Load Succeeded': props<{ appointments: Appointment[]; stats: DayStats }>(),
    'Load Failed': props<{ error: string }>(),
    'Change Requested': props<{ change: ScheduleChange }>(),
    'Change Failed': props<{ error: string }>(),
  },
});

export interface ScheduleState {
  date: string;
  appointments: Appointment[];
  stats: DayStats | null;
  loading: boolean;
  error: string | null;
}

const initialState: ScheduleState = {
  date: clinicDate(),
  appointments: [],
  stats: null,
  loading: false,
  error: null,
};

export const scheduleFeature = createFeature({
  name: 'schedule',
  reducer: createReducer(
    initialState,
    on(scheduleActions.datePicked, (state, { date }): ScheduleState => ({ ...state, date, loading: true, error: null })),
    on(scheduleActions.refresh, (state): ScheduleState => ({ ...state, loading: true, error: null })),
    on(scheduleActions.loadSucceeded, (state, { appointments, stats }): ScheduleState => ({
      ...state,
      appointments,
      stats,
      loading: false,
    })),
    on(scheduleActions.loadFailed, scheduleActions.changeFailed, (state, { error }): ScheduleState => ({
      ...state,
      loading: false,
      error,
    })),
  ),
});
