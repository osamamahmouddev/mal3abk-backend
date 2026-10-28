using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace Malabak.ArchitectureTests.Rules;

public sealed class DoNotUseDateTimeNowOrUtcNowRule : ICustomRule
{
    public bool MeetsRule(TypeDefinition type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return CheckType(type);
    }

    private static bool CheckType(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (method.Body?.Instructions is null)
            {
                continue;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                    instruction.Operand is MethodReference methodRef &&
                    string.Equals(methodRef.DeclaringType.FullName, "System.DateTime", StringComparison.Ordinal) &&
                    (string.Equals(methodRef.Name, "get_Now", StringComparison.Ordinal) ||
                     string.Equals(methodRef.Name, "get_UtcNow", StringComparison.Ordinal) ||
                     string.Equals(methodRef.Name, "get_Today", StringComparison.Ordinal)))
                {
                    return false;
                }
            }
        }

        foreach (var nested in type.NestedTypes)
        {
            if (!CheckType(nested))
            {
                return false;
            }
        }

        return true;
    }
}
