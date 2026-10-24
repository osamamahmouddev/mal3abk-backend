namespace Malabak.SharedKernel;

public sealed record Money
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

        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency = DefaultCurrency) => new(0m, currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        if (left.Amount < right.Amount)
        {
            throw new InvalidOperationException("Resulting money amount cannot be negative.");
        }
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Cannot operate on money with different currencies: {left.Currency} and {right.Currency}.");
        }
    }

    public override string ToString() => $"{Amount:N2} {Currency}";
}
