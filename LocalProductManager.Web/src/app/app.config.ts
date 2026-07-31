import { ApplicationConfig, isDevMode, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { routes } from './app.routes';
import { apiCookieInterceptor } from './core/api-cookie.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([apiCookieInterceptor])),
    provideRouter(routes),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      // Do not let the first-time app-shell prefetch compete with product
      // thumbnails for the browser's newly established HTTPS connections.
      registrationStrategy: 'registerWithDelay:30000',
    }),
  ]
};
