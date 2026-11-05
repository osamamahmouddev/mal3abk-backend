namespace Malabak.Modules.Teams.Domain;

public interface ITeamSlugGenerator
{
    string GenerateSlug(string teamName);
}
