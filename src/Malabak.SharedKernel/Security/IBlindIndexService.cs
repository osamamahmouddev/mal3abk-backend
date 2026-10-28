namespace Malabak.SharedKernel.Security;

/// <summary>
/// Deterministic HMAC-SHA256 "blind index" so encrypted values (phone, email) can still be looked up
/// and kept unique by exact match. Callers must normalize the value first (E.164 phone, lower-case email).
/// </summary>
public interface IBlindIndexService
{
    byte[] Compute(string normalizedValue, string purpose);
}
