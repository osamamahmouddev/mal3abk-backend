namespace Malabak.SharedKernel.Contracts;

/// <summary>Feature flags for staged features (TASK-712). Unknown flags are off.</summary>
public interface IFeatureFlags
{
    Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Known feature flag keys. Phase 2/3 product features stay off until an admin enables them.</summary>
public static class FeatureFlagKeys
{
    public const string SponsoredHomeItems = "home.sponsored";
    public const string FeaturedVenues = "venues.featured";
    public const string PaymentGateway = "payments.gateway";
    public const string Tournaments = "tournaments";
    public const string Reviews = "reviews";
    public const string Referrals = "referrals";
}

/// <summary>Typed platform settings changed by admins at runtime (TASK-712). Values always fall back to code defaults.</summary>
public interface IPlatformSettings
{
    Task<int> GetIntAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> GetBoolAsync(string key, CancellationToken cancellationToken = default);

    Task<string> GetStringAsync(string key, CancellationToken cancellationToken = default);

    Task<decimal> GetDecimalAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Known platform setting keys.</summary>
public static class PlatformSettingKeys
{
    public const string HoldMinutes = "booking.default_hold_minutes";
    public const string ProofReviewMinutes = "booking.default_proof_review_minutes";
    public const string OtpMaxPerHour = "otp.max_per_hour";
    public const string OtpResendCooldownSeconds = "otp.resend_cooldown_seconds";
    public const string RankingWinPoints = "ranking.win_points";
    public const string RankingDrawPoints = "ranking.draw_points";
    public const string RankingLossPoints = "ranking.loss_points";
    public const string MaintenanceEnabled = "maintenance.enabled";
    public const string MaintenanceMessage = "maintenance.message";
    public const string DefaultHourlyCommission = "pricing.default_hourly_commission";
    public const string PlatformDepositEnabled = "platform.deposit.enabled";
    public const string PlatformInstaPayAddress = "platform.deposit.instapay_address";
    public const string PlatformVodafoneCashNumber = "platform.deposit.vodafone_cash_number";
    public const string PlatformDepositRecipientName = "platform.deposit.recipient_name";
    public const string PlatformDepositInstructions = "platform.deposit.instructions";
    public const string PlatformBankAccountIban = "platform.deposit.bank_account_iban";
    public const string PlatformBankName = "platform.deposit.bank_name";
}
