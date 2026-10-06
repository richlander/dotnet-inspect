using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// Whole-library leverage ranking over one focused call-graph result.
/// </summary>
public sealed class LibraryLeverageAnalysisResult
{
    private readonly LibraryCallGraphAnalysisResult _callGraph;
    private readonly GeneratedFrameworkTypeSet _generatedFrameworkTypes;
    private readonly ImmutableArray<MethodIdentity> _unsafeMethods;

    internal LibraryLeverageAnalysisResult(
        LibraryBodyAnalysisReceipt receipt,
        LibraryCallGraphAnalysisResult callGraph,
        GeneratedFrameworkTypeSet generatedFrameworkTypes,
        ImmutableArray<MethodIdentity> unsafeMethods)
    {
        Receipt = receipt;
        _callGraph = callGraph;
        _generatedFrameworkTypes = generatedFrameworkTypes;
        _unsafeMethods = unsafeMethods;
    }

    /// <summary>
    /// Common identity, coverage, and diagnostics for the producing execution.
    /// </summary>
    public LibraryBodyAnalysisReceipt Receipt { get; }

    /// <summary>Whether method-evidence production participated.</summary>
    public bool WasRequested =>
        Receipt.Features.HasFlag(
            LibraryBodyAnalysisFeatures.MethodEvidence);

    /// <summary>
    /// Exact identities of structurally recognized generated-framework types.
    /// </summary>
    public ImmutableHashSet<TypeRef> GeneratedFrameworkTypes =>
        _generatedFrameworkTypes.Types;

    /// <summary>
    /// Ranks methods by distinct direct callers and supporting graph signals.
    /// </summary>
    public ImmutableArray<MethodLeverage> Top(
        int count = 25,
        Func<MethodIdentity, bool>? scope = null)
    {
        if (!WasRequested)
        {
            throw new InvalidOperationException(
                "Leverage analysis was not requested for this Analysis "
                + "execution.");
        }

        return MethodLeverageRanking.Top(
            _callGraph.DirectCalls,
            _callGraph.Methods,
            count,
            scope,
            maxDepth: 64,
            _callGraph.DeclaredMethodMap);
    }

    /// <summary>
    /// Ranks methods that impose an unsafe obligation by distinct direct
    /// callers.
    /// </summary>
    public ImmutableArray<UnsafeMethodLeverage> TopUnsafe(int count = 6)
    {
        if (!WasRequested)
        {
            throw new InvalidOperationException(
                "Leverage analysis was not requested for this Analysis "
                + "execution.");
        }

        return UnsafeLeverage.Top(
            _callGraph.PhysicalDirectCalls,
            _unsafeMethods,
            count,
            _callGraph.DeclaredMethodMap);
    }
}

internal sealed class GeneratedFrameworkTypeSet
{
    private readonly LibraryCallGraphAnalysisResult _callGraph;
    private ImmutableHashSet<TypeRef>? _types;

    internal GeneratedFrameworkTypeSet(
        LibraryCallGraphAnalysisResult callGraph)
    {
        _callGraph = callGraph;
    }

    internal ImmutableHashSet<TypeRef> Types =>
        _types ??=
            GeneratedFrameworkTypeAnalysis.Collect(
                    _callGraph.PhysicalDirectCalls,
                    _callGraph.Methods)
                .ToImmutableHashSet();
}
