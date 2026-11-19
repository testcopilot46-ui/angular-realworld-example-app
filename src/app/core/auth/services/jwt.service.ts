import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class JwtService {
  getToken(): string {
    // JWT is now stored in httpOnly cookie, not accessible from JS
    // This method returns empty string for backward compatibility
    return '';
  }

  saveToken(token: string): void {
    // JWT is now set via httpOnly cookie by the backend
    // This method is a no-op for backward compatibility
  }

  destroyToken(): void {
    // JWT is now cleared via httpOnly cookie by calling logout endpoint
    // This method is a no-op - the logout is handled by UserService
  }
}
