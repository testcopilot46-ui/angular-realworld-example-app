export interface User {
  email: string;
  username: string;
  bio: string | null;
  image: string | null;
  token?: string; // Optional for backward compatibility with RealWorld spec
}
