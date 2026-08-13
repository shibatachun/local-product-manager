import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthApiService } from '../../core/auth-api.service';
import { AppUserAdmin, CurrentUser, UserSessionAdmin } from '../../core/models';
import { LabelPrintingApiService, LabelPrintingSettings } from '../../core/label-printing-api.service';

@Component({
  selector: 'app-admin-page',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './admin.page.html',
  styleUrl: './admin.page.css',
})
export class AdminPage {
  readonly auth = inject(AuthApiService);
  readonly labelPrinting = inject(LabelPrintingApiService);
  readonly currentUser = signal<CurrentUser | null>(null);
  readonly users = signal<AppUserAdmin[]>([]);
  readonly sessions = signal<UserSessionAdmin[]>([]);
  readonly notice = signal('');
  readonly loading = signal(false);
  readonly loginForm = { userName: 'admin123', password: '' };
  readonly resetForm = { password: '' };
  labelSettings: LabelPrintingSettings = { enabled: false, provider: 'BarTender', endpointUrl: 'http://localhost:5159', templateName: '', printerName: '', timeoutSeconds: 15 };

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
        this.labelSettings = await this.labelPrinting.settings();
      } else {
        this.users.set([]);
        this.sessions.set([]);
      }
    }, false);
  }

  async saveLabelSettings(): Promise<void> {
    await this.run(async () => {
      this.labelSettings = await this.labelPrinting.saveSettings(this.labelSettings);
      this.show('Third-party label printing settings saved.');
    });
  }

  async testLabelConnection(): Promise<void> {
    await this.run(async () => {
      this.labelSettings = await this.labelPrinting.saveSettings(this.labelSettings);
      this.show((await this.labelPrinting.test()).message);
    });
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

  exportSocialData(): void {
    window.location.href = this.auth.socialDataExportUrl();
  }

  async onSocialDataImportSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (!file) return;
    if (!confirm('Import this social data backup? This only replaces social account snapshots, content snapshots, and daily deltas. Product and user data will not be changed.')) return;

    await this.run(async () => {
      const result = await this.auth.importSocialData(file);
      this.show(`${result.status} Account snapshots: ${result.accountSnapshots}, content snapshots: ${result.contentSnapshots}, daily deltas: ${result.dailyDeltas}.`);
    });
  }

  async resetSocialData(): Promise<void> {
    if (!this.resetForm.password) {
      this.show('Enter the admin password before resetting social data.');
      return;
    }
    const confirmed = confirm('Reset all social snapshots, content snapshots, and daily delta data? Social accounts will remain. Products, users, and attribution data will not be changed.');
    if (!confirmed) return;

    await this.run(async () => {
      const result = await this.auth.resetSocialData(this.resetForm.password);
      this.resetForm.password = '';
      this.show(`${result.status} Account snapshots: ${result.accountSnapshots}, content snapshots: ${result.contentSnapshots}, daily deltas: ${result.dailyDeltas}.`);
    });
  }

  private async run(action: () => Promise<void>, showErrors = true): Promise<void> {
    this.loading.set(true);
    try {
      await action();
    } catch (error: any) {
      if (showErrors) this.show(error?.error?.message || error?.error || error?.message || 'Action failed.');
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
