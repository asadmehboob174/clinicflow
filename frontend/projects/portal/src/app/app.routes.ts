import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./home.page').then(m => m.HomePage), title: 'ClinicFlow' },
  { path: 'book', loadComponent: () => import('./book.page').then(m => m.BookPage), title: 'Book a visit | ClinicFlow' },
  { path: 'visits', loadComponent: () => import('./visits.page').then(m => m.VisitsPage), title: 'My visits | ClinicFlow' },
  { path: '**', redirectTo: '' },
];
