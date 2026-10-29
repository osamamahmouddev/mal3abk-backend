class PingResponse {
  final String message;
  final String status;

  PingResponse({required this.message, required this.status});

  factory PingResponse.fromJson(Map<String, dynamic> json) {
    return PingResponse(
      message: json['message'] as String? ?? '',
      status: json['status'] as String? ?? '',
    );
  }
}

class SendOtpRequest {
  final String phoneNumber;

  SendOtpRequest({required this.phoneNumber});

  Map<String, dynamic> toJson() => {'phoneNumber': phoneNumber};
}

class SendOtpResponse {
  final bool success;
  final String message;
  final int expiresInSeconds;

  SendOtpResponse({
    required this.success,
    required this.message,
    required this.expiresInSeconds,
  });

  factory SendOtpResponse.fromJson(Map<String, dynamic> json) {
    return SendOtpResponse(
      success: json['success'] as bool? ?? true,
      message: json['message'] as String? ?? '',
      expiresInSeconds: json['expiresInSeconds'] as int? ?? 300,
    );
  }
}

class VerifyOtpRequest {
  final String phoneNumber;
  final String code;

  VerifyOtpRequest({required this.phoneNumber, required this.code});

  Map<String, dynamic> toJson() => {
    'phoneNumber': phoneNumber,
    'code': code,
  };
}

class AuthResponse {
  final String accessToken;
  final String refreshToken;
  final String tokenType;
  final int expiresIn;

  AuthResponse({
    required this.accessToken,
    required this.refreshToken,
    required this.tokenType,
    required this.expiresIn,
  });

  factory AuthResponse.fromJson(Map<String, dynamic> json) {
    return AuthResponse(
      accessToken: json['accessToken'] as String? ?? '',
      refreshToken: json['refreshToken'] as String? ?? '',
      tokenType: json['tokenType'] as String? ?? 'Bearer',
      expiresIn: json['expiresIn'] as int? ?? 900,
    );
  }
}

class RefreshTokenRequest {
  final String refreshToken;

  RefreshTokenRequest({required this.refreshToken});

  Map<String, dynamic> toJson() => {'refreshToken': refreshToken};
}
