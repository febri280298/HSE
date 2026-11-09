import { Injectable, effect, signal } from '@angular/core';

const DARK_KEY = 'hse.mode-gelap';
const COLLAPSED_KEY = 'hse.menu-ringkas';
const DESKTOP_MIN_WIDTH = 992;

function readFlag(key: string): boolean {
  try {
    return localStorage.getItem(key) === '1';
  } catch {
    return false;
  }
}

function saveFlag(key: string, value: boolean): void {
  try {
    localStorage.setItem(key, value ? '1' : '0');
  } catch {
    // Penyimpanan browser diblokir: pilihan hanya berlaku sampai halaman ditutup.
  }
}

/** Keadaan kerangka halaman: mode gelap, menu ringkas (desktop), dan menu terbuka (HP). */
@Injectable({ providedIn: 'root' })
export class LayoutService {
  readonly dark = signal(readFlag(DARK_KEY));
  readonly menuCollapsed = signal(readFlag(COLLAPSED_KEY));
  readonly mobileMenuOpen = signal(false);

  constructor() {
    document.documentElement.classList.toggle('app-dark', this.dark());
    effect(() => {
      const dark = this.dark();
      document.documentElement.classList.toggle('app-dark', dark);
      saveFlag(DARK_KEY, dark);
    });
    effect(() => saveFlag(COLLAPSED_KEY, this.menuCollapsed()));
    effect(() => document.body.classList.toggle('blocked-scroll', this.mobileMenuOpen()));
  }

  toggleDark(): void {
    this.dark.update((value) => !value);
  }

  toggleMenu(): void {
    if (window.innerWidth >= DESKTOP_MIN_WIDTH) {
      this.menuCollapsed.update((value) => !value);
    } else {
      this.mobileMenuOpen.update((value) => !value);
    }
  }

  closeMobileMenu(): void {
    this.mobileMenuOpen.set(false);
  }
}
