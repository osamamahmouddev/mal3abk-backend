namespace Malabak.Modules.Identity.Application.Common;

public interface IStepUpService
{
    string IssueStepUpToken(Guid userId, string scope, TimeSpan? validFor = null);

    bool ValidateStepUpToken(string stepUpToken, Guid expectedUserId, string expectedScope, TimeSpan? maxAge = null);
}
