using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Domain;

public static class TeamErrors
{
    public static readonly Error NotFound = Error.NotFound("Team.NotFound", "The team was not found.");
    public static readonly Error SlugAlreadyExists = Error.Conflict("Team.SlugAlreadyExists", "A team with this slug already exists.");
    public static readonly Error NameAlreadyExists = Error.Conflict("Team.NameAlreadyExists", "A team with this name already exists in this city.");
    public static readonly Error InvalidName = Error.Validation("Team.InvalidName", "The team name is invalid.");
    public static readonly Error InvalidLocation = Error.Validation("Team.InvalidLocation", "City and governorate are required.");
    public static readonly Error PlayerAlreadyInTeam = Error.Conflict("Team.PlayerAlreadyInTeam", "The player is already a member of this team.");
    public static readonly Error PlayerNotInTeam = Error.NotFound("Team.PlayerNotInTeam", "The player is not a member of this team.");
    public static readonly Error MemberNotFound = Error.NotFound("Team.MemberNotFound", "The team member was not found.");
    public static readonly Error NotCaptain = Error.Forbidden("Team.NotCaptain", "Only the team captain is authorized to perform this action.");
    public static readonly Error NotCaptainOrViceCaptain = Error.Forbidden("Team.NotCaptainOrViceCaptain", "Only the captain or vice-captain is authorized to perform this action.");
    public static readonly Error NotAuthorizedToManageRoster = Error.Forbidden("Team.NotAuthorizedToManageRoster", "You are not authorized to modify this member's roster status.");
    public static readonly Error CannotRemoveCaptain = Error.Conflict("Team.CannotRemoveCaptain", "The team captain cannot be removed from the team. Transfer captaincy first.");
    public static readonly Error SelfRemovalMustTransferCaptaincy = Error.Conflict("Team.SelfRemovalMustTransferCaptaincy", "As the captain, you must transfer captaincy to another member before leaving the team.");
    public static readonly Error CannotChangeCaptainRole = Error.Conflict("Team.CannotChangeCaptainRole", "Captain role cannot be modified directly. Use captaincy transfer.");
    public static readonly Error InvalidInviteCode = Error.Validation("Team.InvalidInviteCode", "The invite code is invalid or does not match any active team.");
    public static readonly Error InviteCodeExpired = Error.Conflict("Team.InviteCodeExpired", "This team invite code has expired. Please request a new invite.");
    public static readonly Error CannotTransferToSelf = Error.Validation("Team.CannotTransferToSelf", "Cannot transfer captaincy to yourself.");
    public static readonly Error TeamNotRecruiting = Error.Conflict("Team.NotRecruiting", "This team is currently not accepting new join requests.");
    public static readonly Error TeamRosterFull = Error.Conflict("Team.RosterFull", "The team roster has reached its maximum capacity of 25 players.");
    public static readonly Error MaxPlayerTeamsExceeded = Error.Conflict("Team.MaxPlayerTeamsExceeded", "You have reached the maximum allowed limit of 5 active teams.");
    public static readonly Error DuplicateJoinRequest = Error.Conflict("Team.DuplicateJoinRequest", "You already have a pending join request for this team.");
    public static readonly Error JoinRequestNotFound = Error.NotFound("Team.JoinRequestNotFound", "The join request was not found.");
    public static readonly Error JoinRequestAlreadyHandled = Error.Conflict("Team.JoinRequestAlreadyHandled", "This join request has already been reviewed.");
}
