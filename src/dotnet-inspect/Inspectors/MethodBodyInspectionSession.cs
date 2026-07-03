using System.Collections.Immutable;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Method-body inspection composition layer (see <c>docs/design/method-body-inspection.md</c>):
/// opens a <see cref="Analysis.LibraryBodyIndex"/> once and produces method- or coordinate-scoped
/// semantic facts, so the <c>member</c> and <c>library --il-offset</c> paths converge on one
/// producer instead of each re-opening the index and re-projecting facts.
///
/// This sits above <c>ILInspector.Analysis</c> (and, in later slices, Metadata / Decompiler /
/// Research); it is deliberately not part of the lower-level <c>DotnetInspector.Services</c>
/// package layer. Early slices open from a <c>dllPath</c>; the target is to consume an
/// <see cref="AssemblyInspectionSession"/> / <see cref="ResolvedAssemblyReference"/> once the
/// shared-PE-owner composition lands.
/// </summary>
public sealed class MethodBodyInspectionSession
{
    readonly Analysis.LibraryBodyIndex _index;

    MethodBodyInspectionSession(Analysis.LibraryBodyIndex index) => _index = index;

    /// <summary>Opens a session over an assembly's analysis body index.</summary>
    public static MethodBodyInspectionSession Open(string assemblyPath, IAssemblyReferenceResolver? resolver = null)
        => new(Analysis.LibraryBodyIndex.Open(assemblyPath, resolver));

    /// <summary>
    /// Allocation facts for one method (<paramref name="methodToken"/>), optionally narrowed to a
    /// single IL coordinate (<paramref name="ilOffset"/>).
    /// </summary>
    public ImmutableArray<Analysis.AllocationFact> AllocationFacts(int methodToken, int? ilOffset = null)
        => Analysis.SemanticFactProjection.AllocationFacts(_index.GetAllocationOccurrences(), methodToken, ilOffset);

    /// <summary>Safety facts for one method, optionally narrowed to a single IL coordinate.</summary>
    public ImmutableArray<Analysis.SafetyFact> SafetyFacts(int methodToken, int? ilOffset = null)
        => Analysis.SemanticFactProjection.SafetyFacts(
            _index.GetUnsafeEvidenceByMember(), _index.GetUnsafetyOccurrences(), methodToken, ilOffset);

    /// <summary>Cost facts for one method, optionally narrowed to a single IL coordinate.</summary>
    public ImmutableArray<Analysis.CostFact> CostFacts(int methodToken, int? ilOffset = null)
        => Analysis.SemanticFactProjection.CostFacts(_index.GetDirectCallsByCaller(), methodToken, ilOffset);

    /// <summary>Outbound call graph rooted at one method.</summary>
    public Analysis.CallTreeNode CallTree(int methodToken)
        => _index.BuildCallTree(methodToken);

    /// <summary>
    /// Inbound caller graph rooted at one method. When <paramref name="scopeIndexes"/> is
    /// non-empty the reverse graph is extended across those caller-scope assemblies.
    /// </summary>
    public Analysis.CallTreeNode CallerTree(int methodToken, IReadOnlyList<Analysis.LibraryBodyIndex>? scopeIndexes = null)
        => scopeIndexes is { Count: > 0 }
            ? _index.BuildCallerTree(methodToken, scopeIndexes)
            : _index.BuildCallerTree(methodToken);
}
