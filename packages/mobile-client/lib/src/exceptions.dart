class MalabakApiException implements Exception {
  final String message;
  final int statusCode;

  MalabakApiException(this.message, this.statusCode);

  @override
  String toString() => 'MalabakApiException(status: $statusCode, message: $message)';
}

class ProblemDetailsException extends MalabakApiException {
  final String? type;
  final String? title;
  final String? detail;
  final String? instance;
  final String? traceId;
  final String? errorCode;
  final Map<String, List<String>> errors;

  ProblemDetailsException({
    required int statusCode,
    this.type,
    this.title,
    this.detail,
    this.instance,
    this.traceId,
    this.errorCode,
    this.errors = const {},
  }) : super(detail ?? title ?? 'API Error with status $statusCode', statusCode);

  factory ProblemDetailsException.fromJson(int statusCode, Map<String, dynamic> json) {
    final rawErrors = json['errors'] as Map<String, dynamic>? ?? {};
    final parsedErrors = <String, List<String>>{};
    rawErrors.forEach((key, value) {
      if (value is List) {
        parsedErrors[key] = value.map((e) => e.toString()).toList();
      }
    });

    return ProblemDetailsException(
      statusCode: statusCode,
      type: json['type'] as String?,
      title: json['title'] as String?,
      detail: json['detail'] as String?,
      instance: json['instance'] as String?,
      traceId: json['traceId'] as String?,
      errorCode: json['errorCode'] as String?,
      errors: parsedErrors,
    );
  }

  @override
  String toString() =>
      'ProblemDetailsException(status: $statusCode, errorCode: $errorCode, title: $title, detail: $detail, errors: $errors)';
}

class RateLimitException extends MalabakApiException {
  final int? retryAfterSeconds;

  RateLimitException({String? message, this.retryAfterSeconds})
      : super(message ?? 'Rate limit exceeded. Please retry later.', 429);
}

class UnauthorizedException extends MalabakApiException {
  UnauthorizedException([String message = 'Unauthorized: Invalid or expired access token'])
      : super(message, 401);
}
