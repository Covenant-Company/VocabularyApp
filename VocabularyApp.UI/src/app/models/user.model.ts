export interface User {
  id: number;
  username: string;
  email: string;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface AuthResponseData {
  success: boolean;
  errorMessage: string | null;
  user: User | null;
  token: string | null;
}

export type LoginResponse = ApiResponse<AuthResponseData>;
export type RegisterResponse = ApiResponse<AuthResponseData>;

export interface RegisterRequest {
  username: string;
  email: string;
  password: string;
}

export interface ApiResponse<T> {
  success: boolean;
  data?: T | null;
  error?: string | null;
  message?: string | null;
  code?: string;
  traceId?: string;
}
