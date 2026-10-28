namespace Malabak.SharedKernel;

public static class MoneyErrors
{
    public static readonly Error NegativeAmount = Error.Validation("Money.NegativeAmount", "Money amount cannot be negative.");
    public static readonly Error InvalidCurrency = Error.Validation("Money.InvalidCurrency", "Currency code must be a 3-letter ISO code.");
    public static readonly Error CurrencyMismatch = Error.Validation("Money.CurrencyMismatch", "Cannot operate on money with different currencies.");
    public static readonly Error NegativeMultiplier = Error.Validation("Money.NegativeMultiplier", "Multiplier cannot be negative.");
    public static readonly Error InvalidDivisor = Error.Validation("Money.InvalidDivisor", "Divisor must be greater than zero.");
}
