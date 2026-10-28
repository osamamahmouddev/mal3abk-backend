using System.Globalization;

namespace Malabak.SharedKernel;

public sealed record Money : IComparable<Money>
{
    public const string DefaultCurrency = "EGP";

    public decimal Amount { get; init; }
    public string Currency { get; init; }

    public Money(decimal amount, string currency = DefaultCurrency)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Amount cannot be negative.", nameof(amount));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency must be specified.", nameof(currency));
        }

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3 || !normalizedCurrency.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException($"Invalid currency code '{currency}'. Must be a 3-letter ISO code.", nameof(currency));
        }

        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = normalizedCurrency;
    }

    private Money()
    {
        Currency = DefaultCurrency;
    }

    public static Result<Money> Create(decimal amount, string currency = DefaultCurrency)
    {
        if (amount < 0)
        {
            return Result.Failure<Money>(MoneyErrors.NegativeAmount);
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            return Result.Failure<Money>(MoneyErrors.InvalidCurrency);
        }

        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsAsciiLetterUpper))
        {
            return Result.Failure<Money>(MoneyErrors.InvalidCurrency);
        }

        return Result.Success(new Money(amount, normalized));
    }

    public static Money From(decimal amount, string currency = DefaultCurrency) => new(amount, currency);

    public static Money Zero(string currency = DefaultCurrency) => new(0m, currency);

    public static Money operator +(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        EnsureSameCurrency(left, right);
        if (left.Amount < right.Amount)
        {
            throw new InvalidOperationException("Resulting money amount cannot be negative.");
        }

        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator *(Money left, decimal multiplier)
    {
        ArgumentNullException.ThrowIfNull(left);

        if (multiplier < 0)
        {
            throw new ArgumentException("Multiplier cannot be negative.", nameof(multiplier));
        }

        return new Money(left.Amount * multiplier, left.Currency);
    }

    public static Money operator *(decimal multiplier, Money right) => right * multiplier;

    public static Money operator *(Money left, int multiplier) => left * (decimal)multiplier;

    public static Money operator *(int multiplier, Money right) => right * (decimal)multiplier;

    public static Money operator /(Money left, decimal divisor)
    {
        ArgumentNullException.ThrowIfNull(left);

        if (divisor <= 0)
        {
            throw new ArgumentException("Divisor must be greater than zero.", nameof(divisor));
        }

        return new Money(left.Amount / divisor, left.Currency);
    }

    public static Money operator /(Money left, int divisor) => left / (decimal)divisor;

    public static bool operator <(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        EnsureSameCurrency(left, right);
        return left.Amount < right.Amount;
    }

    public static bool operator <=(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        EnsureSameCurrency(left, right);
        return left.Amount <= right.Amount;
    }

    public static bool operator >(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        EnsureSameCurrency(left, right);
        return left.Amount > right.Amount;
    }

    public static bool operator >=(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        EnsureSameCurrency(left, right);
        return left.Amount >= right.Amount;
    }

    public int CompareTo(Money? other)
    {
        if (other is null)
        {
            return 1;
        }

        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Cannot operate on money with different currencies: {left.Currency} and {right.Currency}.");
        }
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount:N2} {Currency}");
}
