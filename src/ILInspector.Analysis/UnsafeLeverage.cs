using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// A requires-unsafe method (per <see cref="CallerUnsafeMode"/>) ranked by how
/// many other methods call it directly. Marking these first propagates the
/// <c>unsafe</c> requirement to the most callers.
/// </summary>
public sealed record UnsafeMethodLeverage(
    MethodIdentity Method,
    int DirectCallerCount,
    CallerUnsafeMode Mode);

public static class UnsafeLeverage
{
    /// <summary>
    /// The <paramref name="count"/> most-leveraged requires-unsafe methods,
    /// ranked by distinct direct callers. Pure over the index's arrays so it is
    /// testable without a real assembly.
    /// </summary>
    public static ImmutableArray<UnsafeMethodLeverage> Top(
        ImmutableArray<DirectCall> directCalls,
        ImmutableArray<MethodIdentity> methods,
        int count)
    {
        // Distinct direct callers per callee, keyed by the targeted MethodDef token. An
        // intra-assembly call references that method's own token (peeled from a MethodSpec
        // for generic-method calls), so this matches a method's MetadataToken to its callers.
        var callersByToken = directCalls
            .GroupBy(call => call.CalleeDefinitionToken)
            .ToDictionary(group => group.Key, group => group.Select(c => c.Caller.MetadataToken).Distinct().Count());

        return methods
            .Where(method => method.CallerUnsafeMode != CallerUnsafeMode.None)
            .Select(method => new UnsafeMethodLeverage(
                method,
                callersByToken.GetValueOrDefault(method.MetadataToken),
                method.CallerUnsafeMode))
            .OrderByDescending(entry => entry.DirectCallerCount)
            .ThenBy(entry => entry.Method.MetadataToken)
            .Take(count)
            .ToImmutableArray();
    }
}
