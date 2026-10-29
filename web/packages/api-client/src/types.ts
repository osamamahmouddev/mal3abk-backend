export interface ApiConfig {
  baseUrl: string;
  getAccessToken?: () => Promise<string | null> | string | null;
  locale?: 'ar-EG' | 'en-US' | string;
  onUnauthorized?: () => void | Promise<void>;
  customHeaders?: Record<string, string>;
}

export interface RequestOptions {
  headers?: Record<string, string>;
  idempotencyKey?: string;
  signal?: AbortSignal;
}

export interface PingResponse {
  message: string;
  status: string;
}

export interface SendOtpRequest {
  phoneNumber: string;
}

export interface SendOtpResponse {
  success: boolean;
  message: string;
  expiresInSeconds: number;
}

export interface VerifyOtpRequest {
  phoneNumber: string;
  code: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  tokenType: string;
  expiresIn: number;
}

export interface RefreshTokenRequest {
  refreshToken: string;
}

export interface RevokeTokenRequest {
  refreshToken: string;
}
