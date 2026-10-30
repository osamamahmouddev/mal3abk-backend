namespace Malabak.SharedKernel;

public static class IdGenerator
{
    /// <summary>
    /// Generates a time-ordered Guid Version 7 (RFC 9562).
    /// Highly optimized for sequential B-Tree index insertions in PostgreSQL,
    /// preventing page-splits and index fragmentation compared to random v4 UUIDs.
    /// </summary>
    public static Guid NewGuid() => Guid.CreateVersion7();
}
