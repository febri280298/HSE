import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { MenuItem as PrimeMenuItem } from 'primeng/api';
import { MenuModule } from 'primeng/menu';
import { TooltipModule } from 'primeng/tooltip';

import { MenuItem, buildMenu } from '../app.menu';
import { AuthService } from '../core/services/auth.service';
import { BrandMarkComponent } from './brand-mark.component';
import { LayoutService } from './layout.service';

interface MenuGroup {
  label: string;
  items: MenuItem[];
}

@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, RouterLinkActive, MenuModule, TooltipModule, BrandMarkComponent],
  template: `
    <aside class="layout-sidebar">
      <a class="layout-brand" routerLink="/" (click)="layout.closeMobileMenu()" aria-label="HSE System, ke Dashboard">
        <app-brand-mark class="layout-brand-mark" />
        <span class="layout-brand-text">
          <b>HSE System</b>
          <small>PT. Bonecom Tricom</small>
        </span>
      </a>

      <ul class="layout-menu">
        @for (group of groups(); track $index) {
          <li>
            @if (group.label) {
              <div class="layout-menuitem-root-text">{{ group.label }}</div>
            }
            <ul>
              @for (item of group.items; track item.link) {
                <li>
                  <a
                    [routerLink]="item.link"
                    routerLinkActive="active-route"
                    [routerLinkActiveOptions]="{ exact: true }"
                    [pTooltip]="item.title"
                    tooltipPosition="right"
                    [tooltipDisabled]="!layout.menuCollapsed()"
                    [attr.aria-label]="item.title"
                    (click)="layout.closeMobileMenu()"
                  >
                    <i class="layout-menuitem-icon" [class]="item.icon"></i>
                    <span class="layout-menuitem-text">{{ item.title }}</span>
                  </a>
                </li>
              }
            </ul>
          </li>
        }
      </ul>

      <div class="layout-sidebar-foot">
        <button type="button" class="layout-user" (click)="userMenu.toggle($event)" aria-haspopup="true" aria-label="Menu pengguna">
          <span class="layout-user-avatar">{{ initials() }}</span>
          <span class="layout-user-text">
            <b>{{ user()?.displayName }}</b>
            <small>{{ roleText() }}</small>
          </span>
          <i class="pi pi-ellipsis-v layout-user-more"></i>
        </button>
        <p-menu #userMenu [model]="userItems" [popup]="true" appendTo="body" />
      </div>
    </aside>
  `,
})
export class SidebarComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly layout = inject(LayoutService);

  readonly user = this.auth.user;
  readonly roleText = computed(() => this.user()?.roles.join(', ') ?? '');
  readonly initials = computed(() =>
    (this.user()?.displayName ?? '')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((word) => word[0].toUpperCase())
      .join(''),
  );

  readonly userItems: PrimeMenuItem[] = [
    {
      label: 'Ganti Password',
      icon: 'pi pi-lock',
      command: () => {
        this.layout.closeMobileMenu();
        void this.router.navigateByUrl('/ganti-password');
      },
    },
    { separator: true },
    { label: 'Keluar', icon: 'pi pi-sign-out', command: () => void this.auth.logout() },
  ];

  /** Dihitung ulang setiap data pengguna berubah (mis. peran diubah admin). */
  readonly groups = computed<MenuGroup[]>(() => {
    this.auth.user();
    const groups: MenuGroup[] = [{ label: '', items: [] }];
    for (const item of buildMenu((permission) => this.auth.can(permission))) {
      if (item.group) {
        groups.push({ label: item.title, items: [] });
      } else {
        groups[groups.length - 1].items.push(item);
      }
    }
    return groups;
  });
}
