import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastModule } from 'primeng/toast';

import { LayoutService } from './layout/layout.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastModule],
  template: '<p-toast position="top-right" /><router-outlet />',
})
export class AppComponent {
  /** Dibuat sejak awal supaya mode gelap tersimpan juga berlaku di halaman login. */
  private readonly layout = inject(LayoutService);
}
