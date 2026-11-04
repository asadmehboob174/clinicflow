import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'today' },
  { path: 'today', loadComponent: () => import('./schedule/today.page').then(m => m.TodayPage), title: 'Today | ClinicFlow' },
  { path: 'patients', loadComponent: () => import('./patients/patients.page').then(m => m.PatientsPage), title: 'Patients | ClinicFlow' },
  { path: 'doctors', loadComponent: () => import('./doctors/doctors.page').then(m => m.DoctorsPage), title: 'Doctors | ClinicFlow' },
  { path: '**', redirectTo: 'today' },
];
