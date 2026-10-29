import 'dart:convert';
import 'package:http/http.dart' as http;
import 'models.dart';
import 'exceptions.dart';

typedef TokenProvider = Future<String?> Function();
typedef UnauthorizedCallback = Future<void> Function();

class MalabakMobileClient {
  final String baseUrl;
  final TokenProvider? tokenProvider;
  final UnauthorizedCallback? onUnauthorized;
  final String defaultLocale;
  final http.Client _httpClient;

  MalabakMobileClient({
    required this.baseUrl,
    this.tokenProvider,
    this.onUnauthorized,
    this.defaultLocale = 'ar-EG',
    http.Client? httpClient,
  }) : _httpClient = httpClient ?? http.Client();

  String _cleanUrl(String path) {
    final base = baseUrl.endsWith('/') ? baseUrl.substring(0, baseUrl.length - 1) : baseUrl;
    final normalizedPath = path.startsWith('/') ? path : '/$path';
    return '$base$normalizedPath';
  }

  Future<Map<String, String>> _buildHeaders({
    String? idempotencyKey,
    Map<String, String>? customHeaders,
  }) async {
    final headers = <String, String>{
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-Locale': defaultLocale,
    };

    if (idempotencyKey != null && idempotencyKey.isNotEmpty) {
      headers['Idempotency-Key'] = idempotencyKey;
    }

    if (tokenProvider != null) {
      final token = await tokenProvider!();
      if (token != null && token.isNotEmpty) {
        headers['Authorization'] = 'Bearer $token';
      }
    }

    if (customHeaders != null) {
      headers.addAll(customHeaders);
    }

    return headers;
  }

  Future<dynamic> _handleResponse(http.Response response) async {
    if (response.statusCode == 401) {
      if (onUnauthorized != null) {
        await onUnauthorized!();
      }
      throw UnauthorizedException();
    }

    if (response.statusCode == 429) {
      final retryAfter = response.headers['retry-after'];
      final retrySeconds = retryAfter != null ? int.tryParse(retryAfter) : null;
      throw RateLimitException(retryAfterSeconds: retrySeconds);
    }

    if (response.statusCode >= 400) {
      final contentType = response.headers['content-type'] ?? '';
      if (contentType.contains('application/problem+json') || contentType.contains('application/json')) {
        try {
          final jsonMap = jsonDecode(response.body) as Map<String, dynamic>;
          throw ProblemDetailsException.fromJson(response.statusCode, jsonMap);
        } on ProblemDetailsException {
          rethrow;
        } catch (_) {
          // Fall through
        }
      }
      throw MalabakApiException(
        response.body.isNotEmpty ? response.body : 'Request failed with status ${response.statusCode}',
        response.statusCode,
      );
    }

    if (response.statusCode == 204 || response.body.isEmpty) {
      return null;
    }

    return jsonDecode(response.body);
  }

  Future<PingResponse> ping({Map<String, String>? headers}) async {
    final url = Uri.parse(_cleanUrl('/api/v1/ping'));
    final reqHeaders = await _buildHeaders(customHeaders: headers);
    final response = await _httpClient.get(url, headers: reqHeaders);
    final json = await _handleResponse(response) as Map<String, dynamic>;
    return PingResponse.fromJson(json);
  }

  Future<SendOtpResponse> sendOtp(
    SendOtpRequest request, {
    String? idempotencyKey,
    Map<String, String>? headers,
  }) async {
    final url = Uri.parse(_cleanUrl('/api/v1/auth/otp/send'));
    final reqHeaders = await _buildHeaders(idempotencyKey: idempotencyKey, customHeaders: headers);
    final response = await _httpClient.post(url, headers: reqHeaders, body: jsonEncode(request.toJson()));
    final json = await _handleResponse(response) as Map<String, dynamic>;
    return SendOtpResponse.fromJson(json);
  }

  Future<AuthResponse> verifyOtp(
    VerifyOtpRequest request, {
    String? idempotencyKey,
    Map<String, String>? headers,
  }) async {
    final url = Uri.parse(_cleanUrl('/api/v1/auth/otp/verify'));
    final reqHeaders = await _buildHeaders(idempotencyKey: idempotencyKey, customHeaders: headers);
    final response = await _httpClient.post(url, headers: reqHeaders, body: jsonEncode(request.toJson()));
    final json = await _handleResponse(response) as Map<String, dynamic>;
    return AuthResponse.fromJson(json);
  }

  Future<AuthResponse> refreshToken(
    RefreshTokenRequest request, {
    Map<String, String>? headers,
  }) async {
    final url = Uri.parse(_cleanUrl('/api/v1/auth/token/refresh'));
    final reqHeaders = await _buildHeaders(customHeaders: headers);
    final response = await _httpClient.post(url, headers: reqHeaders, body: jsonEncode(request.toJson()));
    final json = await _handleResponse(response) as Map<String, dynamic>;
    return AuthResponse.fromJson(json);
  }

  void close() {
    _httpClient.close();
  }
}
