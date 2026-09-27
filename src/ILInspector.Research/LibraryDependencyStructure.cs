using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Research;

public enum LibraryDependencyStructureUnavailableReason
{
    MethodEvidenceNotRequested,
    NonWholeLibraryScope,
}

public abstract record LibraryDependencyStructureResult
{
    private LibraryDependencyStructureResult()
    {
    }

    public sealed record Available(
        LibraryDependencyStructureDocument Document)
        : LibraryDependencyStructureResult;

    public sealed record Unavailable(
        LibraryDependencyStructureUnavailableReason Reason,
        string Message,
        LibraryBodyAnalysisReceipt Receipt)
        : LibraryDependencyStructureResult;
}

/// <summary>
/// Whether absence facts (acyclic, no call dependency) may be stated
/// unqualified. <see cref="Qualified"/> means they hold only among admitted
/// call evidence.
/// </summary>
public enum LibraryDependencyCompleteness
{
    Complete,
    Qualified,
}

/// <summary>Why a call is counted as unresolved in the population receipt.</summary>
public enum LibraryDependencyUnresolvedReason
{
    Indirect,
    UnsupportedSignature,
    MalformedSignature,
    InvalidGenericDeclaration,
    Unmatched,
    Ambiguous,
    ModuleReference,
}

public sealed record LibraryDependencyCounts(
    int Invocations,
    int FunctionReferences)
{
    public int Total => Invocations + FunctionReferences;
}

/// <summary>
/// A type declared in the inspected module that declares at least one method.
/// <see cref="TypeKey"/> is the Library Metrics join currency.
/// </summary>
public sealed record LibraryDependencyTypeNode(
    string TypeKey,
    TypeRef Type,
    string Namespace,
    int IntraTypeRelationshipCount);

/// <summary>
/// One referenced assembly (exactly as referenced, never forwarded) and
/// namespace, or the intrinsic core library.
/// </summary>
public sealed record LibraryDependencyExternalNode(
    string Key,
    AssemblyReferenceIdentity? Assembly,
    bool IsIntrinsicCoreLibrary,
    string Namespace);

public sealed record LibraryDependencyTypeEdge(
    string SourceTypeKey,
    string TargetTypeKey,
    LibraryDependencyCounts Counts);

public sealed record LibraryDependencyExternalTypeEdge(
    string SourceTypeKey,
    string ExternalKey,
    LibraryDependencyCounts Counts);

public sealed record LibraryDependencyNamespaceNode(
    string Namespace,
    bool IsGlobalNamespace,
    int TypeCount,
    int IntraNamespaceRelationshipCount,
    int? CycleIndex,
    int Level);

public sealed record LibraryDependencyNamespaceEdge(
    string SourceNamespace,
    string TargetNamespace,
    LibraryDependencyCounts Counts,
    int ContributingTypeEdgeCount,
    ImmutableArray<LibraryDependencyTypeEdge> ExplainingTypeEdges,
    int RemainingContributorCount);

public sealed record LibraryDependencyExternalNamespaceEdge(
    string SourceNamespace,
    string ExternalKey,
    LibraryDependencyCounts Counts,
    int ContributingTypeEdgeCount,
    ImmutableArray<LibraryDependencyExternalTypeEdge> ExplainingTypeEdges,
    int RemainingContributorCount);

public sealed record LibraryDependencyNamespaceCycle(
    ImmutableArray<string> Namespaces);

public sealed record LibraryDependencyUnresolvedCount(
    LibraryDependencyUnresolvedReason Reason,
    int Count);

/// <summary>
/// Exact accounting of every examined direct call. Internal, external,
/// runtime-provided, and unresolved occurrences partition
/// <see cref="ExaminedCallCount"/>.
/// </summary>
public sealed record LibraryDependencyPopulationReceipt(
    int ExaminedCallCount,
    int InternalCallCount,
    int SameTypeCallCount,
    int ExternalCallCount,
    int RuntimeProvidedCallCount,
    int UnresolvedCallCount,
    ImmutableArray<LibraryDependencyUnresolvedCount> UnresolvedReasons,
    int IncompleteBodyCount,
    int TypeCount,
    int NamespaceCount,
    int ExternalNodeCount);

public sealed record LibraryDependencyStructureDocument(
    LibraryBodyAnalysisReceipt AnalysisReceipt,
    string MethodologyVersion,
    LibraryDependencyPopulationReceipt Population,
    LibraryDependencyCompleteness Completeness,
    ImmutableArray<LibraryDependencyTypeNode> Types,
    ImmutableArray<LibraryDependencyExternalNode> ExternalNodes,
    ImmutableArray<LibraryDependencyTypeEdge> TypeEdges,
    ImmutableArray<LibraryDependencyExternalTypeEdge> ExternalTypeEdges,
    ImmutableArray<LibraryDependencyNamespaceNode> Namespaces,
    ImmutableArray<LibraryDependencyNamespaceEdge> NamespaceEdges,
    ImmutableArray<LibraryDependencyExternalNamespaceEdge> ExternalNamespaceEdges,
    ImmutableArray<LibraryDependencyNamespaceCycle> Cycles,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

/// <summary>
/// The Library Dependency Structure Research owner: one library's complete
/// admitted type dependency graph, its projection onto declared namespaces and
/// referenced assemblies, and the namespace cycles and levels derived from it.
/// See <c>docs/design/library-dependency-structure.md</c>.
/// </summary>
public static class LibraryDependencyStructure
{
    public const string CurrentMethodologyVersion =
        "library-dependency-structure.v1";

    public const int MaximumExplainingTypeEdges = 5;

    const string IntrinsicCoreLibraryKey = "<intrinsic-core-library>";

    public static LibraryDependencyStructureResult Execute(
        LibraryBodyAnalysisExecution analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return Execute(analysis.CallGraph);
    }

    public static LibraryDependencyStructureResult Execute(
        LibraryCallGraphAnalysisResult callGraph)
    {
        ArgumentNullException.ThrowIfNull(callGraph);
        if (!callGraph.WasRequested)
        {
            return new LibraryDependencyStructureResult.Unavailable(
                LibraryDependencyStructureUnavailableReason
                    .MethodEvidenceNotRequested,
                "Library Dependency Structure requires method call evidence.",
                callGraph.Receipt);
        }
        if (!callGraph.HasFullMethodEvidenceScope)
        {
            return new LibraryDependencyStructureResult.Unavailable(
                LibraryDependencyStructureUnavailableReason
                    .NonWholeLibraryScope,
                "Library Dependency Structure requires unscoped whole-library "
                    + "method evidence.",
                callGraph.Receipt);
        }

        return new LibraryDependencyStructureResult.Available(
            Build(callGraph));
    }

    static LibraryDependencyStructureDocument Build(
        LibraryCallGraphAnalysisResult callGraph)
    {
        // Type nodes: method-declaring types of the inspected module.
        var types = new Dictionary<string, TypeRef>(StringComparer.Ordinal);
        foreach (MethodIdentity method in callGraph.DeclaredMethods)
            types.TryAdd(LibraryStructuralReport.TypeKey(method.DeclaringType), method.DeclaringType);

        var intraType = new Dictionary<string, int>(StringComparer.Ordinal);
        var typeEdges = new Dictionary<(string Source, string Target), MutableCounts>();
        var externalEdges = new Dictionary<(string Source, string External), MutableCounts>();
        var externalNodes = new Dictionary<string, LibraryDependencyExternalNode>(StringComparer.Ordinal);
        var unresolved = new Dictionary<LibraryDependencyUnresolvedReason, int>();
        var occurrences = new HashSet<(int Body, int Offset, int Operand)>();
        int examined = 0, internalCalls = 0, sameType = 0, external = 0, runtimeProvided = 0;

        foreach (DirectCall call in callGraph.DirectCalls)
        {
            if (!occurrences.Add((call.EvidenceMethod.MetadataToken, call.ILOffset, call.OperandToken)))
            {
                throw new InvalidOperationException(
                    "Library Dependency Structure requires one direct call per physical call site; "
                        + $"duplicate call site at body 0x{call.EvidenceMethod.MetadataToken:X8}, "
                        + $"IL offset {call.ILOffset}, operand 0x{call.OperandToken:X8}.");
            }

            examined++;
            bool functionReference = call.Kind is CallKind.LoadFunction or CallKind.LoadVirtualFunction;
            string sourceKey = LibraryStructuralReport.TypeKey(call.Caller.DeclaringType);
            types.TryAdd(sourceKey, call.Caller.DeclaringType);

            switch (callGraph.ResolveTarget(call))
            {
                case DirectCallTarget.CurrentModule current:
                    MethodIdentity target = callGraph.ResolveDeclaredMethod(current.Method) ?? current.Method;
                    string targetKey = LibraryStructuralReport.TypeKey(target.DeclaringType);
                    types.TryAdd(targetKey, target.DeclaringType);
                    internalCalls++;
                    if (targetKey == sourceKey)
                    {
                        sameType++;
                        intraType[sourceKey] = intraType.GetValueOrDefault(sourceKey) + 1;
                    }
                    else
                    {
                        Get(typeEdges, (sourceKey, targetKey)).Add(functionReference);
                    }
                    break;

                case DirectCallTarget.External { Origin: TypeReferenceOrigin.ModuleReference }:
                    Count(unresolved, LibraryDependencyUnresolvedReason.ModuleReference);
                    break;

                case DirectCallTarget.External externalTarget
                    when ExternalNode(externalTarget.Origin, call.Callee.DeclaringType) is { } node:
                    externalNodes.TryAdd(node.Key, node);
                    external++;
                    Get(externalEdges, (sourceKey, node.Key)).Add(functionReference);
                    break;

                case DirectCallTarget.External:
                    Count(unresolved, LibraryDependencyUnresolvedReason.Unmatched);
                    break;

                case DirectCallTarget.RuntimeProvided:
                    runtimeProvided++;
                    break;

                case DirectCallTarget.Unresolved failure:
                    Count(unresolved, Map(failure.Reason));
                    break;
            }
        }

        int incompleteBodies = callGraph.Diagnostics
            .Select(static diagnostic => diagnostic.MethodToken)
            .Distinct()
            .Count();
        int unresolvedTotal = unresolved.Values.Sum();

        ImmutableArray<LibraryDependencyTypeNode> typeNodes =
        [
            .. types
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new LibraryDependencyTypeNode(
                    pair.Key,
                    pair.Value,
                    NamespaceOf(pair.Value),
                    intraType.GetValueOrDefault(pair.Key))),
        ];
        var namespaceOfType = typeNodes.ToDictionary(
            static node => node.TypeKey,
            static node => node.Namespace,
            StringComparer.Ordinal);

        ImmutableArray<LibraryDependencyTypeEdge> typeEdgeRows =
        [
            .. typeEdges
                .Select(static pair => new LibraryDependencyTypeEdge(
                    pair.Key.Source, pair.Key.Target, pair.Value.ToCounts()))
                .OrderBy(static edge => edge.SourceTypeKey, StringComparer.Ordinal)
                .ThenBy(static edge => edge.TargetTypeKey, StringComparer.Ordinal),
        ];
        ImmutableArray<LibraryDependencyExternalTypeEdge> externalTypeEdgeRows =
        [
            .. externalEdges
                .Select(static pair => new LibraryDependencyExternalTypeEdge(
                    pair.Key.Source, pair.Key.External, pair.Value.ToCounts()))
                .OrderBy(static edge => edge.SourceTypeKey, StringComparer.Ordinal)
                .ThenBy(static edge => edge.ExternalKey, StringComparer.Ordinal),
        ];

        // Namespace projection.
        var namespaceEdgeGroups = typeEdgeRows
            .Where(edge => namespaceOfType[edge.SourceTypeKey] != namespaceOfType[edge.TargetTypeKey])
            .GroupBy(edge => (Source: namespaceOfType[edge.SourceTypeKey], Target: namespaceOfType[edge.TargetTypeKey]))
            .ToArray();
        ImmutableArray<LibraryDependencyNamespaceEdge> namespaceEdges =
        [
            .. namespaceEdgeGroups
                .Select(group => new LibraryDependencyNamespaceEdge(
                    group.Key.Source,
                    group.Key.Target,
                    Sum(group.Select(static edge => edge.Counts)),
                    group.Count(),
                    Explain(group, static edge => edge.Counts, static edge => edge.SourceTypeKey, static edge => edge.TargetTypeKey),
                    Math.Max(0, group.Count() - MaximumExplainingTypeEdges)))
                .OrderBy(static edge => edge.SourceNamespace, StringComparer.Ordinal)
                .ThenBy(static edge => edge.TargetNamespace, StringComparer.Ordinal),
        ];
        ImmutableArray<LibraryDependencyExternalNamespaceEdge> externalNamespaceEdges =
        [
            .. externalTypeEdgeRows
                .GroupBy(edge => (Source: namespaceOfType[edge.SourceTypeKey], edge.ExternalKey))
                .Select(group => new LibraryDependencyExternalNamespaceEdge(
                    group.Key.Source,
                    group.Key.ExternalKey,
                    Sum(group.Select(static edge => edge.Counts)),
                    group.Count(),
                    Explain(group, static edge => edge.Counts, static edge => edge.SourceTypeKey, static edge => edge.ExternalKey),
                    Math.Max(0, group.Count() - MaximumExplainingTypeEdges)))
                .OrderBy(static edge => edge.SourceNamespace, StringComparer.Ordinal)
                .ThenBy(static edge => edge.ExternalKey, StringComparer.Ordinal),
        ];

        string[] namespaces =
        [
            .. typeNodes
                .Select(static node => node.Namespace)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        var intraNamespace = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (LibraryDependencyTypeNode node in typeNodes)
            intraNamespace[node.Namespace] = intraNamespace.GetValueOrDefault(node.Namespace) + node.IntraTypeRelationshipCount;
        foreach (LibraryDependencyTypeEdge edge in typeEdgeRows)
        {
            string ns = namespaceOfType[edge.SourceTypeKey];
            if (ns == namespaceOfType[edge.TargetTypeKey])
                intraNamespace[ns] = intraNamespace.GetValueOrDefault(ns) + edge.Counts.Total;
        }

        (ImmutableArray<LibraryDependencyNamespaceCycle> cycles,
            Dictionary<string, int> cycleIndex,
            Dictionary<string, int> levels) =
            NamespaceStructure.Derive(namespaces, namespaceEdges);

        ImmutableArray<LibraryDependencyNamespaceNode> namespaceNodes =
        [
            .. namespaces.Select(ns => new LibraryDependencyNamespaceNode(
                ns,
                ns.Length == 0,
                typeNodes.Count(node => node.Namespace == ns),
                intraNamespace.GetValueOrDefault(ns),
                cycleIndex.TryGetValue(ns, out int index) ? index : null,
                levels[ns])),
        ];

        var population = new LibraryDependencyPopulationReceipt(
            examined,
            internalCalls,
            sameType,
            external,
            runtimeProvided,
            unresolvedTotal,
            [
                .. unresolved
                    .OrderBy(static pair => pair.Key)
                    .Select(static pair => new LibraryDependencyUnresolvedCount(pair.Key, pair.Value)),
            ],
            incompleteBodies,
            typeNodes.Length,
            namespaceNodes.Length,
            externalNodes.Count);

        return new LibraryDependencyStructureDocument(
            callGraph.Receipt,
            CurrentMethodologyVersion,
            population,
            unresolvedTotal == 0 && incompleteBodies == 0
                ? LibraryDependencyCompleteness.Complete
                : LibraryDependencyCompleteness.Qualified,
            typeNodes,
            [.. externalNodes.Values.OrderBy(static node => node.Key, StringComparer.Ordinal)],
            typeEdgeRows,
            externalTypeEdgeRows,
            namespaceNodes,
            namespaceEdges,
            externalNamespaceEdges,
            cycles,
            callGraph.Diagnostics);
    }

    static string NamespaceOf(TypeRef type) =>
        type.Resolution?.Type.Namespace ?? type.Namespace;

    static LibraryDependencyExternalNode? ExternalNode(
        TypeReferenceOrigin origin,
        TypeRef declaringType)
    {
        TypeRef definition = declaringType.Kind == TypeRefKind.GenericInstance
            ? declaringType.ElementType ?? declaringType
            : declaringType;
        string ns = NamespaceOf(definition);
        return origin switch
        {
            TypeReferenceOrigin.AssemblyReference reference => new(
                ExternalKey(reference.Assembly, ns),
                reference.Assembly,
                IsIntrinsicCoreLibrary: false,
                ns),
            TypeReferenceOrigin.IntrinsicCoreLibrary => new(
                ExternalKey(assembly: null, ns),
                Assembly: null,
                IsIntrinsicCoreLibrary: true,
                ns),
            _ => null,
        };
    }

    /// <summary>
    /// The exact external node identity. Assembly names and namespaces come from
    /// untrusted metadata and may contain any separator, so each component is
    /// length-prefixed: distinct (assembly, namespace) pairs never share a key.
    /// </summary>
    internal static string ExternalKey(AssemblyReferenceIdentity? assembly, string ns) =>
        assembly is null
            ? string.Concat(
                Component("intrinsic", IntrinsicCoreLibraryKey),
                Component("namespace", ns))
            : string.Concat(
                Component("assembly", assembly.Name),
                Component("version", assembly.Version?.ToString()),
                Component("culture", assembly.Culture),
                Component("publicKeyToken", assembly.PublicKeyToken),
                Component("namespace", ns));

    static string Component(string name, string? value) =>
        value is null
            ? $"{name}=null;"
            : $"{name}={value.Length}:{value};";

    static LibraryDependencyUnresolvedReason Map(DirectCallTargetUnresolvedReason reason) =>
        reason switch
        {
            DirectCallTargetUnresolvedReason.Indirect => LibraryDependencyUnresolvedReason.Indirect,
            DirectCallTargetUnresolvedReason.UnsupportedSignature => LibraryDependencyUnresolvedReason.UnsupportedSignature,
            DirectCallTargetUnresolvedReason.MalformedSignature => LibraryDependencyUnresolvedReason.MalformedSignature,
            DirectCallTargetUnresolvedReason.InvalidGenericDeclaration => LibraryDependencyUnresolvedReason.InvalidGenericDeclaration,
            DirectCallTargetUnresolvedReason.Ambiguous => LibraryDependencyUnresolvedReason.Ambiguous,
            _ => LibraryDependencyUnresolvedReason.Unmatched,
        };

    static ImmutableArray<TEdge> Explain<TEdge>(
        IEnumerable<TEdge> contributors,
        Func<TEdge, LibraryDependencyCounts> counts,
        Func<TEdge, string> source,
        Func<TEdge, string> target) =>
        [
            .. contributors
                .OrderByDescending(edge => counts(edge).Total)
                .ThenBy(source, StringComparer.Ordinal)
                .ThenBy(target, StringComparer.Ordinal)
                .Take(MaximumExplainingTypeEdges),
        ];

    static LibraryDependencyCounts Sum(IEnumerable<LibraryDependencyCounts> counts)
    {
        int invocations = 0, functionReferences = 0;
        foreach (LibraryDependencyCounts count in counts)
        {
            invocations += count.Invocations;
            functionReferences += count.FunctionReferences;
        }
        return new(invocations, functionReferences);
    }

    static void Count<TKey>(Dictionary<TKey, int> counts, TKey key)
        where TKey : notnull =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    static MutableCounts Get<TKey>(Dictionary<TKey, MutableCounts> map, TKey key)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out MutableCounts? counts))
            map[key] = counts = new();
        return counts;
    }

    sealed class MutableCounts
    {
        int _invocations;
        int _functionReferences;

        public void Add(bool functionReference)
        {
            if (functionReference)
                _functionReferences++;
            else
                _invocations++;
        }

        public LibraryDependencyCounts ToCounts() => new(_invocations, _functionReferences);
    }
}
