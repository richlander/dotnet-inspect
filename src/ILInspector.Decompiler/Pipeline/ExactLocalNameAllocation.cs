using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

internal enum ExactLocalNameDisposition
{
    None,
    Preserved,
    NotPrinterUsable,
    Collision,
}

internal sealed record ExactLocalNameAllocation(
    ImmutableArray<string?> DisplayNames,
    ImmutableArray<ExactLocalNameDisposition> Dispositions)
{
    public static ExactLocalNameAllocation Allocate(
        IrNode scope,
        int localCount,
        ImmutableArray<string?> localNames,
        IEnumerable<string> reservedNames)
    {
        var displayNames = new string?[localCount];
        var dispositions = new ExactLocalNameDisposition[localCount];
        var taken = new HashSet<string>(reservedNames, StringComparer.Ordinal);
        var armLocalOwners = ArmScopedPatternLocals(scope);
        var armNameUsers =
            new Dictionary<(object Switch, string Name), HashSet<object>>();

        for (var index = 0;
            index < localCount && index < localNames.Length;
            index++)
        {
            if (localNames[index] is not { } name)
                continue;
            if (!CSharpNaming.IsUsableIdentifier(name))
            {
                dispositions[index] = ExactLocalNameDisposition.NotPrinterUsable;
                continue;
            }

            bool isArmLocal = armLocalOwners.TryGetValue(index, out var owner);
            if (taken.Add(name))
            {
                displayNames[index] = name;
                dispositions[index] = ExactLocalNameDisposition.Preserved;
                if (isArmLocal)
                    armNameUsers[(owner.Switch, name)] = [owner.Arm];
            }
            else if (isArmLocal
                && armNameUsers.TryGetValue(
                    (owner.Switch, name),
                    out var users)
                && users.Add(owner.Arm))
            {
                displayNames[index] = name;
                dispositions[index] = ExactLocalNameDisposition.Preserved;
            }
            else
            {
                dispositions[index] = ExactLocalNameDisposition.Collision;
            }
        }

        return new ExactLocalNameAllocation(
            [.. displayNames],
            [.. dispositions]);
    }

    public static HashSet<string> ReservedNames(
        IrNode scope,
        IEnumerable<Parameter> parameters,
        IEnumerable<string> genericParameterNames,
        IEnumerable<string>? capturedBinderNames = null)
    {
        var names = capturedBinderNames is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(
                capturedBinderNames,
                StringComparer.Ordinal);
        names.UnionWith(parameters.Select(parameter => parameter.DisplayName));
        names.UnionWith(genericParameterNames);
        foreach (var localFunction in scope
            .DescendantsOutsideNestedFunctions
            .OfType<LocalFunctionStatement>())
        {
            names.Add(localFunction.Name);
        }
        return names;
    }

    static Dictionary<int, (object Switch, object Arm)>
        ArmScopedPatternLocals(IrNode scope)
    {
        var owners = new Dictionary<int, (object, object)>();
        foreach (var arm in scope
            .DescendantsOutsideNestedFunctions
            .OfType<PatternSwitchExpressionArm>())
        {
            object owningSwitch = arm.Parent ?? arm;
            if (arm.LocalIndex is { } localIndex)
                owners[localIndex] = (owningSwitch, arm);
            if (arm.Subpattern is { } subpattern)
                owners[subpattern.LocalIndex] = (owningSwitch, arm);
        }
        foreach (var arm in scope
            .DescendantsOutsideNestedFunctions
            .OfType<UnionSwitchExpressionArm>())
        {
            if (arm.LocalIndex is { } localIndex)
                owners[localIndex] = (arm.Parent ?? arm, arm);
        }
        return owners;
    }
}
