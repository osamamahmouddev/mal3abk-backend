using System.Security.Cryptography;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;

namespace Malabak.Modules.Teams.Infrastructure.Services;

public sealed class TeamInviteCodeGenerator : ITeamInviteCodeGenerator
{
    public string GenerateCode()
    {
        return RandomNumberGenerator.GetString(TeamInviteCodes.Alphabet, TeamInviteCodes.Length);
    }
}
