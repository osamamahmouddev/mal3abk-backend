using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Pitches.DeletePitch;

public sealed record DeletePitchCommand(Guid PitchId, Guid OwnerId) : ICommand<Result>;
