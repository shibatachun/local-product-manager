import { HttpInterceptorFn } from '@angular/common/http';
import { apiBaseUrl } from './api-base';

export const apiCookieInterceptor: HttpInterceptorFn = (request, next) => {
  const apiBase = apiBaseUrl();
  return next(request.url.startsWith(apiBase)
    ? request.clone({ withCredentials: true })
    : request);
};
