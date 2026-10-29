namespace Malabak.Api.Infrastructure.Idempotency;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class IdempotentAttribute : Attribute
{
    public bool Required { get; init; }

    public IdempotentAttribute(bool required = true)
    {
        Required = required;
    }
}
