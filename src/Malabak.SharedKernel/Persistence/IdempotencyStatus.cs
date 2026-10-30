namespace Malabak.SharedKernel.Persistence;

public enum IdempotencyStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2
}
