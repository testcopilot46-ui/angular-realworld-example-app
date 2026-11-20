import { HttpInterceptorFn } from '@angular/common/http';

// Requests go to /api on the same origin; in development proxy.conf.json forwards them to the ASP.NET Core backend.
export const API_BASE_URL = '/api';

export const apiInterceptor: HttpInterceptorFn = (req, next) => {
  const apiReq = req.clone({ url: `${API_BASE_URL}${req.url}` });
  return next(apiReq);
};
