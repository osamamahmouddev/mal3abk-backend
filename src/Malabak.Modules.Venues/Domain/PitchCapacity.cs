using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

public readonly record struct PitchCapacity : IComparable<PitchCapacity>
{
    public const int MinPlayersPerTeam = 3;
    public const int MaxPlayersPerTeam = 11;
    public const int MinTotalPlayers = MinPlayersPerTeam * 2;
    public const int MaxTotalPlayers = MaxPlayersPerTeam * 2;

    public int PlayersPerTeam { get; }

    public int TotalPlayers => PlayersPerTeam * 2;

    public string Format => $"{PlayersPerTeam}x{PlayersPerTeam}";

    private PitchCapacity(int playersPerTeam)
    {
        PlayersPerTeam = playersPerTeam;
    }

    public static Result<PitchCapacity> Create(int playersPerTeam)
    {
        if (playersPerTeam < MinPlayersPerTeam || playersPerTeam > MaxPlayersPerTeam)
        {
            return Result.Failure<PitchCapacity>(new Error(
                "Pitch.InvalidCapacity",
                $"Pitch capacity must be between {MinPlayersPerTeam} and {MaxPlayersPerTeam} players per team ({MinTotalPlayers} to {MaxTotalPlayers} total players)."));
        }

        return Result.Success(new PitchCapacity(playersPerTeam));
    }

    public static Result<PitchCapacity> FromTotalPlayers(int totalPlayers)
    {
        if (totalPlayers < MinTotalPlayers || totalPlayers > MaxTotalPlayers || totalPlayers % 2 != 0)
        {
            return Result.Failure<PitchCapacity>(new Error(
                "Pitch.InvalidTotalPlayers",
                $"Total players must be an even number between {MinTotalPlayers} and {MaxTotalPlayers}."));
        }

        var playersPerTeam = totalPlayers / 2;
        return Create(playersPerTeam);
    }

    public static PitchCapacity FiveAside => new(5);
    public static PitchCapacity SevenAside => new(7);
    public static PitchCapacity EightAside => new(8);
    public static PitchCapacity NineAside => new(9);
    public static PitchCapacity ElevenAside => new(11);

    public int CompareTo(PitchCapacity other) => PlayersPerTeam.CompareTo(other.PlayersPerTeam);

    public override string ToString() => $"{Format} ({TotalPlayers} players)";
}
