using System.Collections.Immutable;

namespace ILInspector.Analysis;

internal static class AnalysisDiagnosticAggregation
{
    internal static ImmutableArray<AnalysisDiagnostic> MergeInMetadataOrder(
        ImmutableArray<AnalysisDiagnostic> methodDiagnostics,
        ImmutableArray<AnalysisDiagnostic> scopeDiagnostics)
    {
        var merged = new List<AnalysisDiagnostic>(
            methodDiagnostics.Length + scopeDiagnostics.Length);
        var candidateIndices =
            new Dictionary<FailureIdentity, List<int>>();

        AddRange(methodDiagnostics);
        AddRange(scopeDiagnostics);

        return [.. merged.OrderBy(
            static diagnostic => diagnostic.MethodToken)];

        void AddRange(
            ImmutableArray<AnalysisDiagnostic> diagnostics)
        {
            foreach (AnalysisDiagnostic diagnostic in diagnostics)
                Add(diagnostic);
        }

        void Add(AnalysisDiagnostic diagnostic)
        {
            var identity = new FailureIdentity(
                diagnostic.MethodToken,
                diagnostic.Method,
                diagnostic.Message);
            if (candidateIndices.TryGetValue(
                    identity,
                    out List<int>? indices))
            {
                foreach (int index in indices)
                {
                    AnalysisDiagnostic existing = merged[index];
                    if (!ProvenanceIsCompatible(
                            existing,
                            diagnostic))
                    {
                        continue;
                    }

                    merged[index] = existing with
                    {
                        SourceMethodToken =
                            existing.SourceMethodToken
                                ?? diagnostic.SourceMethodToken,
                        DeclaringType =
                            existing.DeclaringType
                                ?? diagnostic.DeclaringType,
                        SourceDeclaringType =
                            existing.SourceDeclaringType
                                ?? diagnostic.SourceDeclaringType,
                    };
                    return;
                }
            }
            else
            {
                indices = [];
                candidateIndices.Add(identity, indices);
            }

            indices.Add(merged.Count);
            merged.Add(diagnostic);
        }
    }

    static bool ProvenanceIsCompatible(
        AnalysisDiagnostic first,
        AnalysisDiagnostic second) =>
        NullableValueIsCompatible(
            first.SourceMethodToken,
            second.SourceMethodToken)
        && ReferenceValueIsCompatible(
            first.DeclaringType,
            second.DeclaringType)
        && ReferenceValueIsCompatible(
            first.SourceDeclaringType,
            second.SourceDeclaringType);

    static bool NullableValueIsCompatible<T>(
        T? first,
        T? second)
        where T : struct =>
        first is null
        || second is null
        || EqualityComparer<T>.Default.Equals(
            first.Value,
            second.Value);

    static bool ReferenceValueIsCompatible<T>(
        T? first,
        T? second)
        where T : class =>
        first is null
        || second is null
        || EqualityComparer<T>.Default.Equals(
            first,
            second);

    readonly record struct FailureIdentity(
        int MethodToken,
        string Method,
        string Message);
}
