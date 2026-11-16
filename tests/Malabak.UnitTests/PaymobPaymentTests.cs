using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Malabak.Modules.Payments.Application.Services;
using Malabak.Modules.Payments.Domain;
using Malabak.Modules.Payments.Infrastructure;
using Malabak.Modules.Payments.Infrastructure.Paymob;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

public sealed class PaymobPaymentTests
{
    private const string _testHmacSecret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void PaymobClient_VerifyCallbackHmac_ValidSignature_ReturnsTrue()
    {
        var options = Options.Create(new PaymobOptions
        {
            HmacSecret = _testHmacSecret
        });

        var client = new PaymobClient(new HttpClient(), options, NullLogger<PaymobClient>.Instance);

        var fields = new Dictionary<string, string?>
        {
            ["amount_cents"] = "5000",
            ["created_at"] = "2026-11-16T12:00:00.000000",
            ["currency"] = "EGP",
            ["error_occured"] = "false",
            ["has_parent_transaction"] = "false",
            ["id"] = "998877",
            ["integration_id"] = "12345",
            ["is_3d_secure"] = "true",
            ["is_auth"] = "false",
            ["is_capture"] = "false",
            ["is_refunded"] = "false",
            ["is_standalone_payment"] = "true",
            ["is_voided"] = "false",
            ["order_id"] = "554433",
            ["owner"] = "1122",
            ["pending"] = "false",
            ["source_data_pan"] = "1234",
            ["source_data_sub_type"] = "MasterCard",
            ["source_data_type"] = "card",
            ["success"] = "true"
        };

        var order = new[]
        {
            "amount_cents", "created_at", "currency", "error_occured", "has_parent_transaction",
            "id", "integration_id", "is_3d_secure", "is_auth", "is_capture", "is_refunded",
            "is_standalone_payment", "is_voided", "order_id", "owner", "pending",
            "source_data_pan", "source_data_sub_type", "source_data_type", "success"
        };

        var sb = new StringBuilder();
        foreach (var key in order)
        {
            sb.Append(fields[key]);
        }

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_testHmacSecret));
        var validHmac = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));

        var isValid = client.VerifyCallbackHmac(fields, validHmac);
        isValid.Should().BeTrue();

        var isTampered = client.VerifyCallbackHmac(fields, "invalid_hmac_hash_that_does_not_match");
        isTampered.Should().BeFalse();
    }

    [Fact]
    public void PaymobClient_VerifyHmac_RawBody_ValidatesCorrectly()
    {
        var options = Options.Create(new PaymobOptions
        {
            HmacSecret = _testHmacSecret
        });

        var client = new PaymobClient(new HttpClient(), options, NullLogger<PaymobClient>.Instance);
        var rawBody = "{\"test\":\"payload\"}";

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_testHmacSecret));
        var validHmac = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody)));

        client.VerifyHmac(rawBody, validHmac).Should().BeTrue();
        client.VerifyHmac(rawBody, "tampered_hash").Should().BeFalse();
    }

    [Fact]
    public async Task ProcessPaymobWebhook_WhenInvalidHmac_RejectsAsync()
    {
        var db = CreatePaymentsDb();
        var fakePaymob = new FakePaymobClient(isValidHmac: false);
        var fakeBookingConfirmation = new FakeBookingPaymentConfirmation();
        var fakeDetails = new FakeBookingDetailsProvider();
        var fakeClock = new FakeClock();

        var service = new PaymentService(db, fakePaymob, fakeDetails, fakeBookingConfirmation, fakeClock, NullLogger<PaymentService>.Instance);

        var rawPayload = JsonSerializer.Serialize(new { obj = new { id = 123 } });
        var result = await service.ProcessPaymobWebhookAsync(rawPayload, "fake-hmac");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.InvalidHmac");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProcessPaymobWebhook_WhenMissingHmac_RejectsWithMissingSignatureAsync(string? missingHmac)
    {
        var db = CreatePaymentsDb();
        var fakePaymob = new FakePaymobClient(isValidHmac: false);
        var fakeBookingConfirmation = new FakeBookingPaymentConfirmation();
        var fakeDetails = new FakeBookingDetailsProvider();
        var fakeClock = new FakeClock();

        var service = new PaymentService(db, fakePaymob, fakeDetails, fakeBookingConfirmation, fakeClock, NullLogger<PaymentService>.Instance);

        var rawPayload = JsonSerializer.Serialize(new { obj = new { id = 123 } });
        var result = await service.ProcessPaymobWebhookAsync(rawPayload, missingHmac);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.MissingSignature");
    }

    [Fact]
    public async Task ProcessPaymobWebhook_WhenValidTransaction_ConfirmsBookingAsync()
    {
        var db = CreatePaymentsDb();
        var bookingId = Guid.NewGuid();
        var transaction = PaymentTransaction.Create(
            Guid.NewGuid(),
            bookingId,
            Guid.NewGuid(),
            60m,
            "EGP",
            "Paymob",
            "99",
            DateTime.UtcNow);

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();

        var fakePaymob = new FakePaymobClient(isValidHmac: true);
        var fakeBookingConfirmation = new FakeBookingPaymentConfirmation();
        var fakeDetails = new FakeBookingDetailsProvider();
        var fakeClock = new FakeClock();

        var service = new PaymentService(db, fakePaymob, fakeDetails, fakeBookingConfirmation, fakeClock, NullLogger<PaymentService>.Instance);

        var rawPayload = JsonSerializer.Serialize(new
        {
            type = "TRANSACTION",
            obj = new
            {
                id = 888777,
                amount_cents = 6000,
                success = true,
                order = new { id = 99 },
                currency = "EGP"
            }
        });

        var result = await service.ProcessPaymobWebhookAsync(rawPayload, "valid-hmac");

        result.IsSuccess.Should().BeTrue();
        fakeBookingConfirmation.ConfirmedBookings.Should().Contain(b => b.BookingId == bookingId && b.ProviderOrderId == "99");

        var updatedTx = await db.Transactions.FirstAsync(t => t.Id == transaction.Id);
        updatedTx.Status.Should().Be(PaymentTransactionStatus.Succeeded);
        updatedTx.ProviderTransactionId.Should().Be("888777");
    }

    [Fact]
    public async Task ProcessPaymobWebhook_WhenReportedAmountIsLessThanExpected_RejectsAsync()
    {
        var db = CreatePaymentsDb();
        var bookingId = Guid.NewGuid();
        var transaction = PaymentTransaction.Create(
            Guid.NewGuid(),
            bookingId,
            Guid.NewGuid(),
            60m, // Expected 60 EGP
            "EGP",
            "Paymob",
            "99",
            DateTime.UtcNow);

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();

        var fakePaymob = new FakePaymobClient(isValidHmac: true);
        var fakeBookingConfirmation = new FakeBookingPaymentConfirmation();
        var fakeDetails = new FakeBookingDetailsProvider();
        var fakeClock = new FakeClock();

        var service = new PaymentService(db, fakePaymob, fakeDetails, fakeBookingConfirmation, fakeClock, NullLogger<PaymentService>.Instance);

        var rawPayload = JsonSerializer.Serialize(new
        {
            type = "TRANSACTION",
            obj = new
            {
                id = 888777,
                amount_cents = 3000, // Only 30 EGP paid!
                success = true,
                order = new { id = 99 },
                currency = "EGP"
            }
        });

        var result = await service.ProcessPaymobWebhookAsync(rawPayload, "valid-hmac");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.InsufficientAmount");
        fakeBookingConfirmation.ConfirmedBookings.Should().BeEmpty();
    }

    [Fact]
    public void PaymentTransaction_MarkFailed_AfterSucceeded_DoesNotOverwriteSucceededStatus()
    {
        var tx = PaymentTransaction.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            60m,
            "EGP",
            "Paymob",
            "order-1",
            DateTime.UtcNow);

        tx.MarkSucceeded("tx-1", "card", "payload-1", DateTime.UtcNow);
        tx.Status.Should().Be(PaymentTransactionStatus.Succeeded);

        // Attempt to mark failed via duplicate/out-of-order webhook
        tx.MarkFailed("tx-1", "payload-failed", DateTime.UtcNow);
        tx.Status.Should().Be(PaymentTransactionStatus.Succeeded);
    }

    [Fact]
    public async Task PaymobClient_InProductionWithoutSecretKey_FailsValidationAsync()
    {
        var options = Options.Create(new PaymobOptions
        {
            SecretKey = string.Empty, // missing secret key
            PublicKey = "test_public_key"
        });

        var client = new PaymobClient(new HttpClient(), options, NullLogger<PaymobClient>.Instance, isProduction: true);

        var result = await client.CreateIntentionAsync(new PaymobIntentionRequest(
            50m,
            "EGP",
            Guid.NewGuid(),
            "REF-1",
            "First",
            "Last",
            "+201000000000",
            "test@example.com"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Paymob.ConfigurationError");
    }

    private static PaymentsDbContext CreatePaymentsDb()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PaymentsDbContext(options);
    }

    private sealed class FakePaymobClient(bool isValidHmac) : IPaymobClient
    {
        public Task<Result<PaymobIntentionResult>> CreateIntentionAsync(
            PaymobIntentionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(new PaymobIntentionResult("fake_client_secret_xyz", "https://accept.paymob.com/unifiedcheckout/?xyz", "fake_intention_1", "fake_order_1")));
        }

        public bool VerifyHmac(string rawBody, string receivedHmac) => isValidHmac;

        public bool VerifyCallbackHmac(IDictionary<string, string?> fields, string receivedHmac) => isValidHmac;
    }

    private sealed class FakeBookingPaymentConfirmation : IBookingPaymentConfirmationContract
    {
        public List<ConfirmBookingCommissionRequest> ConfirmedBookings { get; } = [];

        public Task<Result> ConfirmCommissionPaymentAsync(ConfirmBookingCommissionRequest request, CancellationToken cancellationToken = default)
        {
            ConfirmedBookings.Add(request);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FakeBookingDetailsProvider : IBookingPaymentDetailsProvider
    {
        public Task<Result<PayableBookingDetails>> GetPayableBookingAsync(Guid bookingId, Guid playerId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(new PayableBookingDetails(
                bookingId,
                playerId,
                "REF-1",
                60m,
                "EGP",
                "PendingDeposit",
                false)));
        }
    }

    private sealed class FakeClock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        public DateTime UtcNow => new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
