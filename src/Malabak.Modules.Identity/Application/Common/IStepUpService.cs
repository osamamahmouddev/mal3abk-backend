namespace Malabak.Modules.Identity.Application.Common;

public interface IStepUpService
{
    /// <param name="binding">Optional value the token is bound to (the user's security stamp), so a password or phone
    /// change invalidates step-up tokens issued before it.</param>
    string IssueStepUpToken(Guid userId, string scope, TimeSpan? validFor = null, string? binding = null);

    /// <param name="binding">When given, the token must have been issued for exactly this binding.</param>
    bool ValidateStepUpToken(string stepUpToken, Guid expectedUserId, string expectedScope, TimeSpan? maxAge = null, string? binding = null);
}

/// <summary>Allowed step-up scopes; anything else is rejected rather than minted.</summary>
public static class StepUpScopes
{
    public const string ChangePhone = "change_phone";
    public const string ChangeEmail = "change_email";
    public const string DeleteAccount = "delete_account";
    public const string DisableTwoFactor = "disable_2fa";
    public const string AdminDestructive = "admin_destructive";
    public const string ExportData = "export_data";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ChangePhone,
        ChangeEmail,
        DeleteAccount,
        DisableTwoFactor,
        AdminDestructive,
        ExportData
    };
}
