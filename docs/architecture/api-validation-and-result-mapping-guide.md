# API Validation, Result Mapping, Localization & Fallback Authorization Guide

## 1. Overview
This guide documents the core API request/response infrastructure implemented in **[TASK-109]**, complying with **[SECURITY.md §10.2](file:///home/osama/Desktop/mal3abk-backend/SECURITY.md)**, **[rule.md §5](file:///home/osama/Desktop/mal3abk-backend/rule.md)**, and **[ADR-0001](file:///home/osama/Desktop/mal3abk-backend/docs/adr/0001-backend-stack.md)**.

The infrastructure provides:
1. **Fallback Authorization Policy (Deny-by-Default):** Every endpoint requires authentication unless explicitly annotated with `.AllowAnonymous()`.
2. **FluentValidation Request Pipeline:** Minimal API endpoint filter validating incoming request DTOs and returning RFC 9457 `ValidationProblemDetails`.
3. **Unified `Result`-to-HTTP Mapping:** Railway-oriented mapping from `Result` / `Result<T>` to HTTP status codes with machine error codes and localized error messages.
4. **Localization Pipeline:** Egyptian Arabic (`ar-EG`, default) and US English (`en-US`), with `X-Locale` and `Accept-Language` header resolution and translated error resources.
5. **Africa/Cairo TimeZone Provider:** Cross-platform `ITimeZoneProvider` converting UTC to Cairo local time with accurate Egypt Daylight Saving Time (DST) transitions.

---

## 2. Fallback Authorization Policy (Deny-by-Default)

Per **[SECURITY.md §10.2](file:///home/osama/Desktop/mal3abk-backend/SECURITY.md)**, Malabak enforces deny-by-default on all endpoints:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

Any endpoint not explicitly marked `.AllowAnonymous()` automatically returns `401 Unauthorized` for anonymous requests.

---

## 3. FluentValidation Pipeline

Endpoints apply validation using `.WithValidation<TRequest>()`:

```csharp
app.MapPost("/api/v1/bookings", async (CreateBookingCommand command, ISender sender) =>
{
    var result = await sender.Send(command);
    return result.ToHttpResult();
})
.WithValidation<CreateBookingCommand>();
```

When validation fails, [`ValidationEndpointFilter`](file:///home/osama/Desktop/mal3abk-backend/src/Malabak.Api/Infrastructure/Validation/ValidationEndpointFilter.cs) short-circuits execution and returns HTTP `400 Bad Request`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "خطأ في صحة البيانات",
  "status": 400,
  "detail": "حدث خطأ في التحقق من صحة البيانات المدخلة.",
  "instance": "/api/v1/bookings",
  "code": "validation_failed",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": {
    "PhoneNumber": [
      "رقم الهاتف غير صالح، يجب أن يكون رقماً مصرياً يبدأ بـ 010 أو 011 أو 012 أو 015."
    ]
  }
}
```

---

## 4. Single `Result`-to-HTTP Mapping

The [`ResultExtensions`](file:///home/osama/Desktop/mal3abk-backend/src/Malabak.Api/Infrastructure/Errors/ResultExtensions.cs) class maps domain `Result` and `Result<T>` to HTTP results:

| Domain `ErrorType` | HTTP Status Code | RFC 9457 Type | Default Title |
|---|---|---|---|
| `ErrorType.Validation` | `400 Bad Request` | `rfc9110#section-15.5.1` | Validation Error / خطأ في صحة البيانات |
| `ErrorType.Unauthorized` | `401 Unauthorized` | `rfc9110#section-15.5.2` | Unauthorized / غير مصرح |
| `ErrorType.Forbidden` | `403 Forbidden` | `rfc9110#section-15.5.4` | Forbidden / ممنوع الوصول |
| `ErrorType.NotFound` | `404 Not Found` | `rfc9110#section-15.5.5` | Not Found / غير موجود |
| `ErrorType.Conflict` | `409 Conflict` | `rfc9110#section-15.5.10` | Conflict / تعارض في البيانات |
| `ErrorType.Failure` | `422 Unprocessable Entity` | `rfc9110#section-15.5.21` | Unprocessable Entity / تعذر معالجة الطلب |

Usage:
```csharp
// Void result:
return result.ToHttpResult(httpContext);

// Value result:
return result.ToHttpResult(onSuccess: data => Results.Created($"/items/{data.Id}", data), httpContext: httpContext);
```

---

## 5. Localization & Culture Providers

1. **Default Culture:** `ar-EG` (Egyptian Arabic).
2. **Provider Priority:**
   - [`XLocaleRequestCultureProvider`](file:///home/osama/Desktop/mal3abk-backend/src/Malabak.Api/Infrastructure/Localization/XLocaleRequestCultureProvider.cs): checks `X-Locale: ar` (-> `ar-EG`) or `X-Locale: en` (-> `en-US`).
   - `AcceptLanguageHeaderRequestCultureProvider`: standard HTTP header.
3. **Localizer:** [`ErrorMessageLocalizer`](file:///home/osama/Desktop/mal3abk-backend/src/Malabak.Api/Infrastructure/Localization/ErrorMessageLocalizer.cs) translates error titles and details according to current UI culture.

---

## 6. Egyptian Time Zone (`Africa/Cairo`)

[`CairoTimeZoneProvider`](file:///home/osama/Desktop/mal3abk-backend/src/Malabak.SharedKernel/Time/CairoTimeZoneProvider.cs) encapsulates:
- Cross-platform resolution (`Africa/Cairo` on Linux, `Egypt Standard Time` on Windows).
- Winter Standard Time (+02:00) and Summer Daylight Saving Time (+03:00).
- Safe UTC to local and local to UTC conversions.
