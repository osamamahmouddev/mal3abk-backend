using FluentAssertions;
using Malabak.Modules.Identity.Infrastructure.Services;
using Xunit;

namespace Malabak.UnitTests;

public sealed class TotpReplayTests
{
    private readonly TotpService _totp = new();
    private static readonly DateTime _now = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);

    private (string Secret, string Code, long Step) Current()
    {
        var secret = _totp.GenerateSecret();
        var step = new DateTimeOffset(_now).ToUnixTimeSeconds() / 30;
        return (secret, TotpService.Compute(TotpService.FromBase32(secret), step), step);
    }

    [Fact]
    public void TryVerifyCode_NoPreviousStep_AcceptsAndReportsTheStep()
    {
        var (secret, code, step) = Current();

        _totp.TryVerifyCode(secret, code, _now, null, out var matched).Should().BeTrue();
        matched.Should().Be(step);
    }

    [Fact]
    public void TryVerifyCode_CodeFromAlreadyUsedStep_IsRejected()
    {
        var (secret, code, step) = Current();

        _totp.TryVerifyCode(secret, code, _now.AddSeconds(5), step, out _).Should().BeFalse();
        _totp.TryVerifyCode(secret, code, _now, step + 1, out _).Should().BeFalse("older steps than the last used one are replays too");
    }

    [Fact]
    public void TryVerifyCode_NextStepCode_IsAcceptedAfterTheCurrentStepWasUsed()
    {
        var (secret, _, step) = Current();
        var nextCode = TotpService.Compute(TotpService.FromBase32(secret), step + 1);

        _totp.TryVerifyCode(secret, nextCode, _now, step, out var matched).Should().BeTrue();
        matched.Should().Be(step + 1);
    }
}
