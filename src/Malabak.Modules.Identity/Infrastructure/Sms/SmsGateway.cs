using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Modules.Identity.Infrastructure.Sms;

/// <summary>
/// Cost-controlled sending (SECURITY.md §4.3, T2 SMS pumping): country allow-list, daily/monthly spend caps
/// with an alert threshold, and failover across providers in configured order. Bodies are never logged.
/// </summary>
public sealed class SmsGateway : ISmsGateway
{
    private readonly IdentityDbContext _dbContext;
    private readonly IReadOnlyList<ISmsProvider> _providers;
    private readonly SmsOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<SmsGateway> _logger;

    public SmsGateway(
        IdentityDbContext dbContext,
        IEnumerable<ISmsProvider> providers,
        IOptions<SmsOptions> options,
        IClock clock,
        ILogger<SmsGateway> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _clock = clock;
        _logger = logger;

        // Order by configuration; providers not listed are never used.
        var available = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        _providers = _options.Providers.Where(available.ContainsKey).Select(name => available[name]).ToList();
    }

    public Task<SmsSendOutcome> SendOtpAsync(string toE164, string code, CancellationToken cancellationToken = default) =>
        SendAsync(toE164, SmsTemplates.OtpBody(code), SmsTemplates.Otp, _options.OtpChannel, cancellationToken);

    public async Task<SmsSendOutcome> SendAsync(string toE164, string body, string template, SmsChannel channel = SmsChannel.Sms,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var dispatch = SmsDispatch.Create(Mask(toE164), Hash(toE164), template, channel.ToString(), nowUtc);
        _dbContext.SmsDispatches.Add(dispatch);

        if (!_options.AllowedCountryPrefixes.Exists(prefix => toE164.StartsWith(prefix, StringComparison.Ordinal)))
        {
            dispatch.MarkFailed(SmsDeliveryStatus.Blocked, "country_not_allowed", nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("SMS blocked for {Phone}: destination country not allowed", dispatch.MaskedPhone);
            return SmsSendOutcome.CountryNotAllowed;
        }

        if (!await WithinSpendCapsAsync(nowUtc, cancellationToken))
        {
            dispatch.MarkFailed(SmsDeliveryStatus.Blocked, "spend_cap_reached", nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return SmsSendOutcome.SpendCapReached;
        }

        var message = new SmsMessage(toE164, body, channel, template);
        foreach (var provider in _providers.Where(p => p.Supports(channel)))
        {
            SmsSendResult result;
            try
            {
                result = await provider.SendAsync(message, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                result = SmsSendResult.Failed(ex.GetType().Name);
            }

            if (result.Success)
            {
                var cost = result.CostEgp > 0 ? result.CostEgp : _options.DefaultCostPerMessageEgp;
                dispatch.MarkSent(provider.Name, result.ProviderMessageId, cost, nowUtc);
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("SMS ({Template}) sent to {Phone} via {Provider}", template, dispatch.MaskedPhone, provider.Name);
                return SmsSendOutcome.Sent;
            }

            _logger.LogWarning("SMS provider {Provider} failed for {Phone}: {Error}; trying next", provider.Name, dispatch.MaskedPhone, result.Error);
        }

        dispatch.MarkFailed(SmsDeliveryStatus.Failed, "all_providers_failed", nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogError("ALERT: all SMS providers failed for {Phone} ({Template})", dispatch.MaskedPhone, template);
        return SmsSendOutcome.AllProvidersFailed;
    }

    private async Task<bool> WithinSpendCapsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var dayStart = nowUtc.Date;
        var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var sent = _dbContext.SmsDispatches.Where(d => d.Status == SmsDeliveryStatus.Sent || d.Status == SmsDeliveryStatus.Delivered);
        var monthSpend = await sent.Where(d => d.CreatedAtUtc >= monthStart).SumAsync(d => d.CostEgp, cancellationToken);
        var daySpend = await sent.Where(d => d.CreatedAtUtc >= dayStart).SumAsync(d => d.CostEgp, cancellationToken);
        var next = _options.DefaultCostPerMessageEgp;

        if (daySpend + next > _options.DailySpendCapEgp || monthSpend + next > _options.MonthlySpendCapEgp)
        {
            _logger.LogError("ALERT: SMS spend cap reached (day {DaySpend}/{DayCap} EGP, month {MonthSpend}/{MonthCap} EGP); sending blocked",
                daySpend, _options.DailySpendCapEgp, monthSpend, _options.MonthlySpendCapEgp);
            return false;
        }

        if (daySpend >= _options.DailySpendCapEgp * _options.AlertThreshold || monthSpend >= _options.MonthlySpendCapEgp * _options.AlertThreshold)
        {
            _logger.LogWarning("ALERT: SMS spend above {Threshold:P0} of cap (day {DaySpend} EGP, month {MonthSpend} EGP)",
                _options.AlertThreshold, daySpend, monthSpend);
        }

        return true;
    }

    internal static string Mask(string e164) => e164.Length <= 7 ? "****" : $"{e164[..6]}****{e164[^3..]}";

    internal static string Hash(string e164) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(e164)))[..32];
}
