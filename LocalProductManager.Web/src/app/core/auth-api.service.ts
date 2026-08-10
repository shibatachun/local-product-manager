import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { apiBaseUrl } from './api-base';
import { AppUserAdmin, AuthAdminSnapshot, CurrentUser, SocialDataMaintenanceResult } from './models';

@Injectable({ providedIn: 'root' })
export class AuthApiService {
  private readonly http = inject(HttpClient);
  readonly baseUrl = apiBaseUrl();

  me(): Promise<CurrentUser> {
    return firstValueFrom(this.http.get<CurrentUser>(`${this.baseUrl}/auth/me`));
  }

  login(userName: string, password: string): Promise<CurrentUser> {
    return firstValueFrom(this.http.post<CurrentUser>(`${this.baseUrl}/auth/login`, { userName, password }));
  }

  logout(): Promise<CurrentUser> {
    return firstValueFrom(this.http.post<CurrentUser>(`${this.baseUrl}/auth/logout`, {}));
  }

  users(): Promise<AuthAdminSnapshot> {
    return firstValueFrom(this.http.get<AuthAdminSnapshot>(`${this.baseUrl}/auth/users`));
  }

  updateUser(user: AppUserAdmin): Promise<AppUserAdmin> {
    return firstValueFrom(this.http.put<AppUserAdmin>(`${this.baseUrl}/auth/users/${user.id}`, {
      role: user.role,
      isEnabled: user.isEnabled,
      canManageStorage: user.canManageStorage,
      canEditAttribution: user.canEditAttribution,
      canManageSocial: user.canManageSocial,
      canImportData: user.canImportData,
      canEditProductDetails: user.canEditProductDetails,
    }));
  }

  socialDataExportUrl(): string {
    return `${this.baseUrl}/social-data/export`;
  }

  importSocialData(file: File): Promise<SocialDataMaintenanceResult> {
    const data = new FormData();
    data.append('file', file);
    return firstValueFrom(this.http.post<SocialDataMaintenanceResult>(`${this.baseUrl}/social-data/import`, data));
  }

  resetSocialData(adminPassword: string): Promise<SocialDataMaintenanceResult> {
    return firstValueFrom(this.http.post<SocialDataMaintenanceResult>(`${this.baseUrl}/social-data/reset`, {
      adminPassword,
    }));
  }
}
