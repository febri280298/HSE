import { Component } from '@angular/core';

/** Tanda HSE System: perisai dengan centang. Ukurannya mengikuti lebar elemen induk. */
@Component({
  selector: 'app-brand-mark',
  template: `
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M12 2 4 5v6c0 5.25 3.4 9.74 8 11 4.6-1.26 8-5.75 8-11V5l-8-3Z" fill="var(--p-primary-500)" />
      <path d="m8.5 12 2.5 2.5 4.5-5" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
    </svg>
  `,
  styles: `
    :host { display: inline-block; line-height: 0; }
    svg { width: 100%; height: auto; }
  `,
})
export class BrandMarkComponent {}
