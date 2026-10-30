using System.Collections.Immutable;
using ILInspector.Analysis.Planning;

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
    /// Projects focused whole-library call evidence into the leverage result.
    /// </summary>
    public static LibraryLeverageAnalysisResult FromMethodLeverageProducer(
        LibraryCallGraphAnalysisResult callGraph)
    {
        ArgumentNullException.ThrowIfNull(callGraph);
        return new(
            callGraph.Receipt,
            callGraph,
            new GeneratedFrameworkTypeSet(callGraph),
            []);
    }

    /// <summary>
    /// Common identity, coverage, and diagnostics for the producing execution.
    /// </summary>
    public LibraryBodyAnalysisReceipt Receipt { get; }

    /// <summary>
    /// Exact Method-source work when this result came from Assembly Analysis
    /// Operation.
    /// </summary>
    public MethodDefinitionSourceReceipt? MethodSourceReceipt =>
        _callGraph.MethodSourceReceipt;

    /// <summary>
    /// Exact Producer Planning participation when this result came from
    /// Assembly Analysis Operation.
    /// </summary>
    public WorkReceipt? ProducerReceipt =>
        _callGraph.ProducerReceipt;

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

        return _callGraph.TopLeverage(count, scope);
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
