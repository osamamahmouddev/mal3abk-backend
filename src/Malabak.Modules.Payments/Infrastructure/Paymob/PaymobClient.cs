using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Malabak.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Modules.Payments.Infrastructure.Paymob;

public sealed class PaymobClient : IPaymobClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PaymobOptions _options;
    private readonly ILogger<PaymobClient> _logger;
    private readonly IClock? _clock;
    private readonly bool _isProduction;

    public PaymobClient(
        HttpClient httpClient,
        IOptions<PaymobOptions> options,
        ILogger<PaymobClient> logger,
        IClock? clock = null,
        bool? isProduction = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _clock = clock;
        _isProduction = isProduction ?? string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<Result<PaymobIntentionResult>> CreateIntentionAsync(PaymobIntentionRequest request, CancellationToken cancellationToken = default)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? new UriBuilder(Uri.UriSchemeHttps, "accept.paymob.com").Uri.ToString().TrimEnd('/')
            : _options.BaseUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            if (_isProduction)
            {
                _logger.LogError("Paymob secret key is missing in production environment.");
                return Result.Failure<PaymobIntentionResult>(Error.Failure("Paymob.ConfigurationError", "Payment gateway is not properly configured."));
            }

            _logger.LogWarning("Paymob secret key is not configured. Returning simulated mock checkout for test/dev.");
            var mockSecret = Guid.NewGuid().ToString("N");
            var mockUrl = $"{baseUrl}/unifiedcheckout/?publicKey={_options.PublicKey}&clientSecret={mockSecret}";
            return Result.Success(new PaymobIntentionResult(mockSecret, mockUrl, "mock_intention_" + request.BookingId, "mock_order_" + request.BookingReference));
        }

        try
        {
            // Amount in cents / piastres (e.g. 50.00 EGP = 5000 cents)
            var amountCents = (int)Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero);

            var paymentMethods = new List<int>();
            if (_options.CardIntegrationId > 0)
            {
                paymentMethods.Add(_options.CardIntegrationId);
            }
            if (_options.WalletIntegrationId > 0)
            {
                paymentMethods.Add(_options.WalletIntegrationId);
            }

            var payload = new
            {
                amount = amountCents,
                currency = request.Currency,
                payment_methods = paymentMethods.Count > 0 ? (object)paymentMethods : new[] { "card", "wallet" },
                items = new[]
                {
                    new
                    {
                        name = $"Platform Commission - Booking {request.BookingReference}",
                        amount = amountCents,
                        quantity = 1
                    }
                },
                billing_data = new
                {
                    first_name = string.IsNullOrWhiteSpace(request.FirstName) ? "Malabak" : request.FirstName,
                    last_name = string.IsNullOrWhiteSpace(request.LastName) ? "Player" : request.LastName,
                    phone_number = string.IsNullOrWhiteSpace(request.PhoneNumber) ? "+201000000000" : request.PhoneNumber,
                    email = string.IsNullOrWhiteSpace(request.Email) ? "player@malabak.app" : request.Email
                },
                customer = new
                {
                    first_name = string.IsNullOrWhiteSpace(request.FirstName) ? "Malabak" : request.FirstName,
                    last_name = string.IsNullOrWhiteSpace(request.LastName) ? "Player" : request.LastName,
                    phone_number = string.IsNullOrWhiteSpace(request.PhoneNumber) ? "+201000000000" : request.PhoneNumber,
                    email = string.IsNullOrWhiteSpace(request.Email) ? "player@malabak.app" : request.Email
                },
                extras = new
                {
                    booking_id = request.BookingId.ToString(),
                    booking_reference = request.BookingReference
                },
                special_reference = $"{request.BookingReference}_{(_clock?.UtcNow ?? DateTime.UtcNow).Ticks}"
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/intention/")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Token {_options.SecretKey}");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Paymob Intention API returned HTTP error {StatusCode}", response.StatusCode);
                return Result.Failure<PaymobIntentionResult>(Error.Failure("Paymob.IntentionFailed", "Failed to initiate online payment session with Paymob."));
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var clientSecret = root.TryGetProperty("client_secret", out var csProp) ? csProp.GetString() : null;
            var intentionId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;

            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                _logger.LogError("Paymob Intention API response did not contain client_secret: {Response}", content);
                return Result.Failure<PaymobIntentionResult>(Error.Failure("Paymob.InvalidResponse", "Invalid response from Paymob."));
            }

            var checkoutUrl = $"{baseUrl}/unifiedcheckout/?publicKey={_options.PublicKey}&clientSecret={clientSecret}";
            return Result.Success(new PaymobIntentionResult(clientSecret, checkoutUrl, intentionId, request.BookingReference));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling Paymob Intention API");
            return Result.Failure<PaymobIntentionResult>(Error.Failure("Paymob.Exception", "Payment gateway communication error."));
        }
    }

    public bool VerifyHmac(string rawBody, string receivedHmac)
    {
        if (string.IsNullOrWhiteSpace(_options.HmacSecret) || string.IsNullOrWhiteSpace(receivedHmac))
        {
            return false;
        }

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_options.HmacSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var computedHex = Convert.ToHexStringLower(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHex),
            Encoding.UTF8.GetBytes(receivedHmac.ToLowerInvariant().Trim()));
    }

    public bool VerifyCallbackHmac(IDictionary<string, string?> fields, string receivedHmac)
    {
        if (string.IsNullOrWhiteSpace(_options.HmacSecret) || string.IsNullOrWhiteSpace(receivedHmac))
        {
            return false;
        }

        // Standard Paymob transaction HMAC concatenation order:
        string[] order =
        [
            "amount_cents",
            "created_at",
            "currency",
            "error_occured",
            "has_parent_transaction",
            "id",
            "integration_id",
            "is_3d_secure",
            "is_auth",
            "is_capture",
            "is_refunded",
            "is_standalone_payment",
            "is_voided",
            "order_id",
            "owner",
            "pending",
            "source_data_pan",
            "source_data_sub_type",
            "source_data_type",
            "success"
        ];

        var sb = new StringBuilder();
        foreach (var key in order)
        {
            if (fields.TryGetValue(key, out var val) && val is not null)
            {
                sb.Append(val);
            }
        }

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_options.HmacSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        var computedHex = Convert.ToHexStringLower(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHex),
            Encoding.UTF8.GetBytes(receivedHmac.ToLowerInvariant().Trim()));
    }
}
