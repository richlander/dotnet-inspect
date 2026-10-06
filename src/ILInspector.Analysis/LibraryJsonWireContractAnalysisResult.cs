using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// Detached call and value-flow evidence used to authenticate System.Text.Json
/// wire contracts for JavaScript exports.
/// </summary>
public sealed class LibraryJsonWireContractAnalysisResult
{
    readonly LibraryCallGraphAnalysisResult _callGraph;

    internal LibraryJsonWireContractAnalysisResult(
        LibraryBodyAnalysisReceipt receipt,
        LibraryCallGraphAnalysisResult callGraph,
        LibraryBodyAnalysisResult analysis)
    {
        Receipt = receipt;
        _callGraph = callGraph;
        ResultSinks = analysis.Methods.ResultSinks;
        FieldStores = analysis.Methods.FieldStores;
        FieldLoads = analysis.Methods.FieldLoads;
        ReturnFlows = analysis.Methods.ReturnFlows;
    }

    /// <summary>
    /// Common identity, coverage, and diagnostics for the producing execution.
    /// </summary>
    public LibraryBodyAnalysisReceipt Receipt { get; }

    /// <summary>Whether JSON wire-contract value-flow production participated.</summary>
    public bool WasRequested =>
        Receipt.Features.HasFlag(
            LibraryBodyAnalysisFeatures.JsonWireContractFlow);

    /// <summary>Identity of the analyzed module.</summary>
    public LibraryBodyModuleIdentity ModuleIdentity =>
        Receipt.ModuleIdentity;

    /// <summary>Visible body-analysis failures from the producing execution.</summary>
    public ImmutableArray<AnalysisDiagnostic> Diagnostics =>
        Receipt.Diagnostics;

    /// <summary>Logical declared methods admitted to the analysis scope.</summary>
    public ImmutableArray<MethodIdentity> DeclaredMethods =>
        _callGraph.DeclaredMethods;

    /// <summary>Physical bodies visited by the analysis.</summary>
    public ImmutableArray<MethodIdentity> Methods =>
        _callGraph.Methods;

    /// <summary>Resolved direct calls found in admitted method bodies.</summary>
    public ImmutableArray<DirectCall> DirectCalls =>
        _callGraph.DirectCalls;

    /// <summary>Direct calls grouped by their physical evidence body.</summary>
    public IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
        DirectCallsByEvidenceMethod =>
        _callGraph.DirectCallsByEvidenceMethod;

    /// <summary>Observed method-result sink flows.</summary>
    public ImmutableArray<MethodResultSink> ResultSinks { get; }

    /// <summary>Observed field stores with value provenance.</summary>
    public ImmutableArray<FieldStoreFact> FieldStores { get; }

    /// <summary>Observed field loads with value provenance.</summary>
    public ImmutableArray<FieldLoadFact> FieldLoads { get; }

    /// <summary>Observed method-return value flows.</summary>
    public ImmutableArray<MethodReturnFlow> ReturnFlows { get; }

    /// <summary>
    /// Resolves a physical or lifted method identity to its declared source
    /// method when the producing analysis authenticated that relationship.
    /// </summary>
    public MethodIdentity? ResolveDeclaredMethod(
        MethodIdentity caller) =>
        _callGraph.ResolveDeclaredMethod(caller);
}
