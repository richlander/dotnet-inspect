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
        IEnumerable<string> reservedNames,
        IReadOnlySet<int> retainedLocalSlots,
        IReadOnlyDictionary<int, IrNode>? declarationScopes = null)
    {
        var displayNames = new string?[localCount];
        var dispositions = new ExactLocalNameDisposition[localCount];
        var reserved = new HashSet<string>(reservedNames, StringComparer.Ordinal);
        var users = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var index = 0;
            index < localCount && index < localNames.Length;
            index++)
        {
            if (!retainedLocalSlots.Contains(index))
                continue;
            if (localNames[index] is not { } name)
                continue;
            if (!CSharpNaming.IsUsableIdentifier(name))
            {
                dispositions[index] = ExactLocalNameDisposition.NotPrinterUsable;
                continue;
            }

            if (reserved.Contains(name)
                || users.TryGetValue(name, out var previous)
                    && previous.Any(other => !Disjoint(index, other)))
            {
                dispositions[index] = ExactLocalNameDisposition.Collision;
                continue;
            }
            displayNames[index] = name;
            dispositions[index] = ExactLocalNameDisposition.Preserved;
            if (!users.TryGetValue(name, out var sameName))
                users.Add(name, sameName = []);
            sameName.Add(index);
        }

        return new ExactLocalNameAllocation(
            [.. displayNames],
            [.. dispositions]);

        bool Disjoint(int left, int right)
        {
            declarationScopes ??=
                LocalDeclarationPlan.Create(scope, localCount)
                    .DeclarationScopes;
            return declarationScopes.TryGetValue(left, out var leftScope)
                && declarationScopes.TryGetValue(right, out var rightScope)
                && !ScopesOverlap(leftScope, rightScope);
        }
    }

    internal static bool ScopesOverlap(IrNode left, IrNode right)
        => Contains(left, right) || Contains(right, left);

    internal static bool Contains(IrNode scope, IrNode node)
    {
        for (IrNode? current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(scope, current))
                return true;
        }
        return false;
    }

    public static HashSet<int> RetainedLocalSlots(
        IrNode scope,
        int localCount,
        IReadOnlySet<int>? eliminatedLocalSlots = null)
    {
        var retained = new HashSet<int>();
        for (var index = 0; index < localCount; index++)
        {
            if (eliminatedLocalSlots?.Contains(index) != true
                && IrFunction.LocalSlotReferencesInScope(scope, index).Any())
            {
                retained.Add(index);
            }
        }
        return retained;
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

}
