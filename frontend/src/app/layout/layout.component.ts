import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { LayoutService } from './layout.service';
import { SidebarComponent } from './sidebar.component';
import { TopbarComponent } from './topbar.component';

/** Kerangka halaman Sakai (topbar, sidebar, footer). Semua modul tampil di dalam router-outlet ini. */
@Component({
  selector: 'app-layout',
  imports: [RouterOutlet, TopbarComponent, SidebarComponent],
  template: `
    <div
      class="layout-wrapper layout-static"
      [class.layout-collapsed]="layout.menuCollapsed()"
      [class.layout-mobile-active]="layout.mobileMenuOpen()"
    >
      <app-topbar />
      <app-sidebar />
      <div class="layout-main-container">
        <main class="layout-main"><router-outlet /></main>
        <footer class="layout-footer">© {{ year }} PT. Bonecom Tricom · HSE System</footer>
      </div>
      <div class="layout-mask" (click)="layout.closeMobileMenu()"></div>
    </div>
  `,
})
export class LayoutComponent {
  readonly layout = inject(LayoutService);
  readonly year = new Date().getFullYear();
}
