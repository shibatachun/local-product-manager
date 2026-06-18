import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthApiService } from '../../core/auth-api.service';
import { AppUserAdmin, CurrentUser, UserSessionAdmin } from '../../core/models';

@Component({
  selector: 'app-admin-page',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './admin.page.html',
  styleUrl: './admin.page.css',
})
export class AdminPage {
  readonly auth = inject(AuthApiService);
  readonly currentUser = signal<CurrentUser | null>(null);
  readonly users = signal<AppUserAdmin[]>([]);
  readonly sessions = signal<UserSessionAdmin[]>([]);
  readonly notice = signal('');
  readonly loading = signal(false);
  readonly loginForm = { userName: 'admin123', password: '' };

  async ngOnInit(): Promise<void> {
    await this.refresh();
  }

  async refresh(): Promise<void> {
    await this.run(async () => {
      const user = await this.auth.me();
      this.currentUser.set(user);
      if (user.isAdmin) {
        const snapshot = await this.auth.users();
        this.users.set(snapshot.users);
        this.sessions.set(snapshot.sessions);
      } else {
        this.users.set([]);
        this.sessions.set([]);
      }
    }, false);
  }

  async login(): Promise<void> {
    await this.run(async () => {
      this.currentUser.set(await this.auth.login(this.loginForm.userName, this.loginForm.password));
      this.loginForm.password = '';
      await this.refresh();
      this.show('Admin logged in.');
    });
  }

  async logout(): Promise<void> {
    await this.run(async () => {
      this.currentUser.set(await this.auth.logout());
      this.users.set([]);
      this.sessions.set([]);
      this.show('Logged out.');
    });
  }

  async saveUser(user: AppUserAdmin): Promise<void> {
    await this.run(async () => {
      const saved = await this.auth.updateUser(user);
      this.users.update(users => users.map(item => item.id === saved.id ? saved : item));
      this.show(`${saved.userName} permissions saved.`);
    });
  }

  private async run(action: () => Promise<void>, showErrors = true): Promise<void> {
    this.loading.set(true);
    try {
      await action();
    } catch (error) {
      if (showErrors) this.show(error instanceof Error ? error.message : 'Action failed.');
    } finally {
      this.loading.set(false);
    }
  }

  private show(message: string): void {
    this.notice.set(message);
    window.setTimeout(() => {
      if (this.notice() === message) this.notice.set('');
    }, 3200);
  }
}
