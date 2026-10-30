import { ApiConfig, RequestOptions, PingResponse, SendOtpRequest, SendOtpResponse, VerifyOtpRequest, AuthResponse, RefreshTokenRequest, RevokeTokenRequest } from './types';
import { ProblemDetailsException, RateLimitException, UnauthorizedException, MalabakApiException } from './errors';

export class MalabakApiClient {
  private readonly config: ApiConfig;

  constructor(config: ApiConfig) {
    this.config = {
      locale: 'ar-EG',
      ...config,
      baseUrl: config.baseUrl.replace(/\/+$/, '')
    };
  }

  private async request<T>(path: string, init: RequestInit, options?: RequestOptions): Promise<T> {
    const url = `${this.config.baseUrl}${path.startsWith('/') ? path : `/${path}`}`;
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-Locale': this.config.locale || 'ar-EG',
      ...(this.config.customHeaders || {}),
      ...(options?.headers || {})
    };

    if (options?.idempotencyKey) {
      headers['Idempotency-Key'] = options.idempotencyKey;
    }

    if (this.config.getAccessToken) {
      const token = await this.config.getAccessToken();
      if (token) {
        headers['Authorization'] = `Bearer ${token}`;
      }
    }

    const response = await fetch(url, {
      ...init,
      headers,
      signal: options?.signal
    });

    if (response.status === 401) {
      if (this.config.onUnauthorized) {
        await this.config.onUnauthorized();
      }
      throw new UnauthorizedException();
    }

    if (response.status === 429) {
      const retryAfter = response.headers.get('Retry-After');
      const retryAfterSec = retryAfter ? parseInt(retryAfter, 10) : undefined;
      throw new RateLimitException('Rate limit exceeded. Please try again later.', isNaN(retryAfterSec as number) ? undefined : retryAfterSec);
    }

    if (!response.ok) {
      const contentType = response.headers.get('Content-Type');
      if (contentType && contentType.includes('application/problem+json') || contentType?.includes('application/json')) {
        try {
          const problemJson = await response.json();
          throw new ProblemDetailsException(problemJson);
        } catch (e) {
          if (e instanceof ProblemDetailsException) throw e;
        }
      }
      const rawText = await response.text();
      throw new MalabakApiException(rawText || `Request failed with status ${response.status}`, response.status);
    }

    if (response.status === 204) {
      return undefined as unknown as T;
    }

    return response.json() as Promise<T>;
  }

  async ping(options?: RequestOptions): Promise<PingResponse> {
    return this.request<PingResponse>('/api/v1/ping', { method: 'GET' }, options);
  }

  async sendOtp(request: SendOtpRequest, options?: RequestOptions): Promise<SendOtpResponse> {
    return this.request<SendOtpResponse>('/api/v1/auth/otp/send', {
      method: 'POST',
      body: JSON.stringify(request)
    }, options);
  }

  async verifyOtp(request: VerifyOtpRequest, options?: RequestOptions): Promise<AuthResponse> {
    return this.request<AuthResponse>('/api/v1/auth/otp/verify', {
      method: 'POST',
      body: JSON.stringify(request)
    }, options);
  }

  async refreshToken(request: RefreshTokenRequest, options?: RequestOptions): Promise<AuthResponse> {
    return this.request<AuthResponse>('/api/v1/auth/token/refresh', {
      method: 'POST',
      body: JSON.stringify(request)
    }, options);
  }

  async revokeToken(request: RevokeTokenRequest, options?: RequestOptions): Promise<void> {
    return this.request<void>('/api/v1/auth/token/revoke', {
      method: 'POST',
      body: JSON.stringify(request)
    }, options);
  }
}
