using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.Common;

public interface IPhoneNumberNormalizer
{
    Result<string> Normalize(string rawPhoneNumber);
}
