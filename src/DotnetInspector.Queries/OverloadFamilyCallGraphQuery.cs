using ILInspector.Analysis;
using ILInspector.CallGraph;

namespace DotnetInspector.Queries;

/// <summary>
/// One exact declared member whose declaring type and metadata name identify an
/// overload family, plus the finite outbound graph bounds.
/// </summary>
public sealed class OverloadFamilyCallGraphRequest
{
    public OverloadFamilyCallGraphRequest(
        MethodIdentity anchor,
        int maxDepth,
        int maxNodes)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 2);
        Anchor = anchor;
        MaxDepth = maxDepth;
        MaxNodes = maxNodes;
    }

    public MethodIdentity Anchor { get; }
    public TypeRef DeclaringType => Anchor.DeclaringType;
    public string MetadataName => Anchor.Name;
    public int MaxDepth { get; }
    public int MaxNodes { get; }
}

/// <summary>
/// Resolves and projects one exact overload family from already-produced local
/// call-graph evidence.
/// </summary>
public static class OverloadFamilyCallGraphQuery
{
    public static InspectionQuery<InspectionGraphDocument> Definition { get; } =
        new(
            "Overload-family call graph",
            InspectionCost.Unbounded);

    public static InspectionGraphDocument Execute(
        LibraryCallGraphAnalysisResult callGraph,
        OverloadFamilyCallGraphRequest request)
    {
        ArgumentNullException.ThrowIfNull(callGraph);
        ArgumentNullException.ThrowIfNull(request);
        if (!callGraph.WasRequested)
        {
            throw new InvalidOperationException(
                "Call-graph evidence was not requested for this Analysis execution.");
        }
        if (!callGraph.HasFullMethodEvidenceScope)
        {
            throw new InspectionQueryException(
                "Overload-family discovery requires complete method evidence for the analyzed module.");
        }

        MethodIdentity? anchor = callGraph.DeclaredMethods.SingleOrDefault(
            method => method == request.Anchor);
        if (anchor is null)
        {
            throw new InspectionQueryException(
                "The overload-family anchor is not a declared method in this Analysis generation.");
        }

        MethodIdentity[] family =
        [
            .. callGraph.DeclaredMethods
                .Where(method =>
                    method.ModuleVersionId == anchor.ModuleVersionId
                    && method.DeclaringType.Equals(
                        anchor.DeclaringType)
                    && StringComparer.Ordinal.Equals(
                        method.Name,
                        anchor.Name))
                .OrderBy(static method =>
                    method.MetadataToken),
        ];
        if (family.Length < 2)
        {
            throw new InspectionQueryException(
                $"'{anchor.DeclaringType.ToDisplayString()}.{anchor.Name}' "
                + "does not identify an overload family with at least two declared methods.");
        }
        if (family.Length > request.MaxNodes)
        {
            throw new InspectionQueryException(
                $"The overload family contains {family.Length} declared methods, "
                + $"which exceeds the combined {request.MaxNodes}-node bound.");
        }

        CallTreeNode[] roots =
        [
            .. family.Select(method =>
                callGraph.BuildCallTree(
                    method.MetadataToken,
                    request.MaxDepth,
                    request.MaxNodes)),
        ];
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                roots,
                request.MaxNodes);
        return CallGraphInspectionGraphAdapter
            .CreateEqualRootOutgoingNeighborhood(
                projection,
                request.MaxDepth,
                request.MaxNodes);
    }
}
