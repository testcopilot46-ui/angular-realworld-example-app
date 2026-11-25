import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { JwtService } from '../auth/services/jwt.service';

export const tokenInterceptor: HttpInterceptorFn = (req, next) => {
  // JWT is now stored in httpOnly cookie, sent automatically by browser
  // No need to manually add Authorization header
  return next(req);
};
