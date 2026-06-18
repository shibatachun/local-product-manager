import { HttpInterceptorFn } from '@angular/common/http';

export const apiCookieInterceptor: HttpInterceptorFn = (request, next) => {
  const apiBase = `http://${globalThis.location?.hostname || 'localhost'}:5077/api`;
  return next(request.url.startsWith(apiBase)
    ? request.clone({ withCredentials: true })
    : request);
};
