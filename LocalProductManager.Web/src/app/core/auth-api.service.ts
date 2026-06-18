import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AppUserAdmin, AuthAdminSnapshot, CurrentUser } from './models';

@Injectable({ providedIn: 'root' })
export class AuthApiService {
  private readonly http = inject(HttpClient);
  readonly baseUrl = `http://${globalThis.location?.hostname || 'localhost'}:5077/api`;

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
    }));
  }
}
