export interface ProblemDetailsData {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  traceId?: string;
  errorCode?: string;
  invalidParams?: Array<{ name: string; reason: string }>;
  errors?: Record<string, string[]>;
  [key: string]: unknown;
}

export class MalabakApiException extends Error {
  public readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = 'MalabakApiException';
    this.status = status;
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

export class ProblemDetailsException extends MalabakApiException {
  public readonly problemDetails: ProblemDetailsData;

  constructor(problemDetails: ProblemDetailsData) {
    super(
      problemDetails.detail || problemDetails.title || `API error with status ${problemDetails.status}`,
      problemDetails.status || 500
    );
    this.name = 'ProblemDetailsException';
    this.problemDetails = problemDetails;
    Object.setPrototypeOf(this, new.target.prototype);
  }

  public get errorCode(): string | undefined {
    return this.problemDetails.errorCode;
  }
}

export class RateLimitException extends MalabakApiException {
  public readonly retryAfterSeconds?: number;

  constructor(message: string, retryAfterSeconds?: number) {
    super(message, 429);
    this.name = 'RateLimitException';
    this.retryAfterSeconds = retryAfterSeconds;
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

export class UnauthorizedException extends MalabakApiException {
  constructor(message = 'Unauthorized: Access token missing, expired, or invalid') {
    super(message, 401);
    this.name = 'UnauthorizedException';
    Object.setPrototypeOf(this, new.target.prototype);
  }
}
