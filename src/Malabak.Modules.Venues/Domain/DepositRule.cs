namespace Malabak.Modules.Venues.Domain;

public enum DepositType
{
    /// <summary>Deposit is a percentage (1–100) of the booking price.</summary>
    Percentage = 0,

    /// <summary>Deposit is a fixed EGP amount.</summary>
    FixedAmount = 1,

    /// <summary>The full booking price is paid upfront.</summary>
    Full = 2
}

/// <summary>How much a player must transfer through a given payment method to hold a booking.</summary>
public sealed record DepositRule
{
    public const decimal MaxFixedAmount = 100_000m;

    private DepositRule(DepositType type, decimal value)
    {
        Type = type;
        Value = value;
    }

    public DepositType Type { get; }
    public decimal Value { get; }

    public static DepositRule Create(DepositType type, decimal value)
    {
        switch (type)
        {
            case DepositType.Percentage when value is <= 0 or > 100:
                throw new ArgumentOutOfRangeException(nameof(value), "Deposit percentage must be between 1 and 100.");
            case DepositType.FixedAmount when value is <= 0 or > MaxFixedAmount || decimal.Round(value, 2) != value:
                throw new ArgumentOutOfRangeException(nameof(value), "Fixed deposit must be a positive EGP amount with at most 2 decimals.");
            case DepositType.Full:
                value = 0;
                break;
            case DepositType.Percentage or DepositType.FixedAmount:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), "Unknown deposit type.");
        }

        return new DepositRule(type, value);
    }

    /// <summary>Deposit due for a booking of <paramref name="bookingPrice"/> EGP, never more than the price itself.</summary>
    public decimal CalculateDeposit(decimal bookingPrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bookingPrice);

        var deposit = Type switch
        {
            DepositType.Percentage => decimal.Round(bookingPrice * Value / 100m, 2, MidpointRounding.AwayFromZero),
            DepositType.FixedAmount => Value,
            _ => bookingPrice
        };

        return Math.Min(deposit, bookingPrice);
    }
}
