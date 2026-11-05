using System.Security.Cryptography;
using Malabak.Modules.Teams.Domain;

namespace Malabak.Modules.Teams.Infrastructure.Services;

public sealed class TeamInviteCodeGenerator : ITeamInviteCodeGenerator
{
    private const string _allowedCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int _codeLength = 6;

    public string GenerateCode()
    {
        return RandomNumberGenerator.GetString(_allowedCharacters, _codeLength);
    }
}
