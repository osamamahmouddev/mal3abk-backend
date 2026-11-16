using Malabak.Modules.Identity.Application.Authentication.VerifyOtp;
using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.Authentication.FirebaseAuth;

public sealed record FirebaseAuthCommand(
    string IdToken,
    string? FullName = null,
    bool RestoreAccount = false) : ICommand<Result<VerifyOtpResponse>>;
