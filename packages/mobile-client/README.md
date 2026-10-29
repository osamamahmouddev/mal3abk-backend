# Malabak Mobile Client SDK (Flutter / Dart)

Official cross-platform Dart/Flutter HTTP client SDK for the Malabak sports venue and pitch booking platform.

## Features
- Full support for ASP.NET Core OpenAPI contract (`/api/v1`).
- RFC 9457 `ProblemDetailsException` error handling with field-level validation messages.
- Automatic `Authorization: Bearer <token>` token provider injection.
- Automatic `X-Locale: ar-EG` header support for Arabic localization.
- `Idempotency-Key` support for safe network retries.
- Rate-limiting error handling (`RateLimitException`) with `Retry-After` seconds inspection.

## Usage

```dart
final client = MalabakMobileClient(
  baseUrl: 'https://api.malabak.app',
  tokenProvider: () async => await secureStorage.read(key: 'jwt_token'),
  defaultLocale: 'ar-EG',
);

// Ping endpoint
final ping = await client.ping();
print('Status: ${ping.status}');

// Send OTP
await client.sendOtp(SendOtpRequest(phoneNumber: '+201012345678'));
```
