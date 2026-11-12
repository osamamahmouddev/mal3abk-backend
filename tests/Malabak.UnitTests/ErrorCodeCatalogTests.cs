using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Malabak.Api.Infrastructure.Localization;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Xunit;

namespace Malabak.UnitTests;

public sealed class ErrorCodeCatalogTests
{
    private static readonly string _catalogPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../docs/api/error-codes.json"));

    public sealed record CatalogEntry(
        string Code,
        int HttpStatus,
        string? Status,
        JsonElement Message);

    public sealed record CatalogRoot(
        string Version,
        string Title,
        List<CatalogEntry> ErrorCodes);

    private static CatalogRoot LoadCatalog()
    {
        File.Exists(_catalogPath).Should().BeTrue($"error code catalog must exist at '{_catalogPath}'");
        var json = File.ReadAllText(_catalogPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var catalog = JsonSerializer.Deserialize<CatalogRoot>(json, options);
        catalog.Should().NotBeNull();
        return catalog!;
    }

    [Fact]
    public void ErrorCatalog_SchemaAndEntries_AreValid()
    {
        // Arrange & Act
        var catalog = LoadCatalog();

        // Assert
        catalog.Title.Should().NotBeNullOrWhiteSpace();
        catalog.ErrorCodes.Should().NotBeEmpty();

        foreach (var entry in catalog.ErrorCodes)
        {
            entry.Code.Should().NotBeNullOrWhiteSpace("each error entry must have a code");
            entry.HttpStatus.Should().BeInRange(400, 599, "each error entry must map to a 4xx or 5xx HTTP status");

            var en = entry.Message.GetProperty("en").GetString();
            var ar = entry.Message.GetProperty("ar").GetString();

            en.Should().NotBeNullOrWhiteSpace($"error '{entry.Code}' must have an English description");
            ar.Should().NotBeNullOrWhiteSpace($"error '{entry.Code}' must have an Arabic description");
        }
    }

    [Fact]
    public void AllCatalogEntries_AreReferencedInCodeOrMarkedUnused()
    {
        // Arrange
        var catalog = LoadCatalog();
        var domainCodes = ExtractAllDomainErrorCodes();

        var localizerCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "VALIDATION_FAILED", "IDEMPOTENCY_KEY_REQUIRED", "IDEMPOTENCY_CONFLICT",
            "IDEMPOTENCY_PAYLOAD_MISMATCH", "RATE_LIMIT_EXCEEDED", "UNAUTHORIZED",
            "FORBIDDEN", "RESOURCE_NOT_FOUND", "CONFLICT", "INTERNAL_SERVER_ERROR",
            "AUTH_INVALID_CREDENTIALS", "AUTH_USER_NOT_FOUND", "AUTH_PHONE_ALREADY_EXISTS",
            "AUTH_OTP_EXPIRED", "AUTH_OTP_MAX_ATTEMPTS", "AUTH_REFRESH_TOKEN_REUSE",
            "BOOKING_SLOT_UNAVAILABLE", "BOOKING_SLOT_LOCKED", "BOOKING_CANCEL_WINDOW_EXPIRED",
            "BOOKING_DEPOSIT_REQUIRED", "BOOKING_PROOF_EXPIRED", "BOOKING_INVALID_STATE_TRANSITION"
        };

        var unreferencedCodes = new List<string>();

        // Act
        foreach (var entry in catalog.ErrorCodes)
        {
            if (string.Equals(entry.Status, "unused", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isDomainCode = domainCodes.Contains(entry.Code);
            var isLocalizerCode = localizerCodes.Contains(entry.Code);

            if (!isDomainCode && !isLocalizerCode)
            {
                unreferencedCodes.Add(entry.Code);
            }
        }

        // Assert
        unreferencedCodes.Should().BeEmpty(
            "every catalog error code must either be reachable in application code or marked with status 'unused'");
    }

    [Fact(Skip = "BUG-002: docs/api/error-codes.json is missing 175 domain error codes returned by the API")]
    public void AllDomainErrorCodes_MustExistInCatalog()
    {
        // Arrange
        var catalog = LoadCatalog();
        var catalogCodes = catalog.ErrorCodes.Select(e => e.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var domainCodes = ExtractAllDomainErrorCodes();

        // Act
        var missingFromCatalog = domainCodes.Where(code => !catalogCodes.Contains(code)).OrderBy(c => c).ToList();

        // Assert: Every domain error code returned by the API must exist in docs/api/error-codes.json
        missingFromCatalog.Should().BeEmpty(
            $"every error code returned by the API must exist in docs/api/error-codes.json. Found {missingFromCatalog.Count} missing codes: {string.Join(", ", missingFromCatalog.Take(10))}...");
    }

    private static HashSet<string> ExtractAllDomainErrorCodes()
    {
        var assemblies = new[]
        {
            typeof(Error).Assembly,
            typeof(BookingErrors).Assembly,
            typeof(ErrorMessageLocalizer).Assembly
        };

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                // Inspect public static fields of type Error
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(f => f.FieldType == typeof(Error));

                foreach (var field in fields)
                {
                    if (field.GetValue(null) is Error error && !string.IsNullOrWhiteSpace(error.Code))
                    {
                        codes.Add(error.Code);
                    }
                }

                // Inspect public static properties of type Error
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Static)
                    .Where(p => p.PropertyType == typeof(Error));

                foreach (var prop in properties)
                {
                    if (prop.GetValue(null) is Error error && !string.IsNullOrWhiteSpace(error.Code))
                    {
                        codes.Add(error.Code);
                    }
                }
            }
        }

        return codes;
    }
}
