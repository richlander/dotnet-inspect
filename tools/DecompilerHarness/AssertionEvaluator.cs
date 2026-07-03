using System.Reflection;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Decompiler.Pipeline.InverseArchitecture;
using ILInspector.Decompiler.Tests.InverseArchitecture;

namespace ILInspector.DecompilerHarness;

internal static class AssertionEvaluator
{
    public sealed record PredicateResult(string Name, IReadOnlyList<AssumptionViolation> Violations);

    public static IReadOnlyList<PredicateResult> EvaluateAssumptions(IrFunction function)
    {
        var results = new List<PredicateResult>();
        var executed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in InverseLedger.NodeTypes(typeof(IrFunction).Assembly))
        {
            if (type.GetCustomAttribute<InverseOfAttribute>() is not { Assumes: { } assumes }
                || !executed.Add(assumes))
            {
                continue;
            }

            var method = typeof(InverseAssumptions).GetMethod(
                assumes,
                BindingFlags.Public | BindingFlags.Static,
                [typeof(IrFunction)]);
            if (method is null)
                continue;

            try
            {
                var result = method.Invoke(null, [function]) as IReadOnlyList<AssumptionViolation>;
                results.Add(new PredicateResult(assumes, result ?? Array.Empty<AssumptionViolation>()));
            }
            catch (Exception ex)
            {
                string message = ex.InnerException is { } inner
                    ? $"{inner.GetType().Name}: {inner.Message}"
                    : $"{ex.GetType().Name}: {ex.Message}";
                results.Add(new PredicateResult(
                    assumes,
                    [new AssumptionViolation(function, $"Predicate {assumes} threw {message}")]));
            }
        }
        return results;
    }
}
