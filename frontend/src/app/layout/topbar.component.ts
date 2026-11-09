import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';

import { LayoutService } from './layout.service';

@Component({
  selector: 'app-topbar',
  template: `
    <header class="layout-topbar">
      <button type="button" class="layout-menu-button layout-topbar-action" (click)="layout.toggleMenu()" aria-label="Buka atau tutup menu">
        <i class="pi pi-bars"></i>
      </button>
      <h1 class="page-title">{{ pageTitle() }}</h1>

      <div class="layout-topbar-actions">
        <button
          type="button"
          class="layout-topbar-action"
          (click)="layout.toggleDark()"
          [attr.aria-label]="layout.dark() ? 'Pakai mode terang' : 'Pakai mode gelap'"
          [title]="layout.dark() ? 'Mode terang' : 'Mode gelap'"
        >
          <i class="pi" [class.pi-sun]="layout.dark()" [class.pi-moon]="!layout.dark()"></i>
        </button>
      </div>
    </header>
  `,
  styles: `
    .page-title { margin: 0; font-size: 1.125rem; font-weight: 600; letter-spacing: -0.01em; line-height: 1.3; }
  `,
})
export class TopbarComponent {
  private readonly router = inject(Router);
  readonly layout = inject(LayoutService);

  /** Judul rute (`Daftar Temuan · HSE System`) tanpa nama aplikasinya. */
  readonly pageTitle = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.currentTitle()),
    ),
    { initialValue: this.currentTitle() },
  );

  private currentTitle(): string {
    let route = this.router.routerState.snapshot.root;
    while (route.firstChild) {
      route = route.firstChild;
    }
    return (route.title ?? '').split(' · ')[0];
  }
}
