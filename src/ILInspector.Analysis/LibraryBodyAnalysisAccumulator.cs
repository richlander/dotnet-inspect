using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Analysis;

/// <summary>
/// Merges one assembly's method-local results in metadata order and projects
/// the immutable analysis result bundle.
/// </summary>
internal sealed class LibraryBodyAnalysisAccumulator
{
    readonly MetadataReader _reader;
    readonly LibraryBodyPrimaryMetadataResolver _primaryMetadataResolver;
    readonly bool _includeMethodEvidence;
    readonly bool _includeLeakTriage;
    readonly IReadOnlySet<string> _exceptionTypeNames;

    internal LibraryBodyAnalysisAccumulator(
        MetadataReader reader,
        LibraryBodyPrimaryMetadataResolver primaryMetadataResolver,
        LibraryBodyAnalysisPlan plan)
    {
        _reader = reader;
        _primaryMetadataResolver = primaryMetadataResolver;
        _includeMethodEvidence = plan.Includes(
            LibraryBodyAnalysisFeatures.MethodEvidence);
        _includeLeakTriage = plan.Includes(
            LibraryBodyAnalysisFeatures.LeakTriage);
        _exceptionTypeNames = _includeMethodEvidence
            ? ComputeExceptionTypeNames()
            : new HashSet<string>(StringComparer.Ordinal);
    }

    internal LibraryBodyAnalysisResult Build(
        IReadOnlyList<LibraryMethodAnalysisResult> results)
    {
        var declaredMethods = ImmutableArray.CreateBuilder<MethodIdentity>();
        var methods = ImmutableArray.CreateBuilder<MethodIdentity>();
        var unsafeLeverageMethods = ImmutableArray.CreateBuilder<MethodIdentity>();
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var unsafeEvidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();
        var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        var optimizationOpportunities = ImmutableArray.CreateBuilder<OptimizationOpportunity>();
        var bodySignals = new Dictionary<int, BodySignals>();
        var allocationOccurrences = new Dictionary<int, ImmutableArray<AllocationOccurrence>>();
        var unsafetyOccurrences = new Dictionary<int, ImmutableArray<UnsafetyOccurrence>>();
        var suppressedOpportunityTokens = new HashSet<int>();
        var leakFindings = ImmutableArray.CreateBuilder<LeakTriageFinding>();
        var leakCandidates = ImmutableArray.CreateBuilder<LeakTriageCandidate>();
        var exceptionPathCandidates =
            ImmutableArray.CreateBuilder<ArrayPoolExceptionPathCandidate>();
        var leakFailures =
            ImmutableArray.CreateBuilder<LeakTriageFailure>();
        var ownershipFlow =
            ImmutableArray.CreateBuilder<ArrayPoolOwnershipMethodEvidence>();
        var declaredSources = new Dictionary<int, MethodIdentity>();
        int none = 0, impl = 0, expl = 0;

        // Merge per-method results in metadata order, reproducing the exact sequence of appends
        // the original sequential loop performed. A method that hit a recoverable failure carries
        // its partial contributions (accumulated before the throw) alongside its diagnostic, so
        // even the failure path is byte-identical to the sequential build.
        foreach (var r in results)
        {
            if (r.LeakTriage is { } leakTriage)
            {
                leakFindings.AddRange(leakTriage.Findings);
                leakCandidates.AddRange(leakTriage.Candidates);
                exceptionPathCandidates.AddRange(
                    leakTriage.ExceptionPathCandidates);
                leakFailures.AddRange(leakTriage.Failures);
            }
            if (r.OwnershipFlow is { } methodOwnership
                && (!methodOwnership.Rents.IsEmpty
                    || !methodOwnership.Parameters.IsEmpty
                    || !methodOwnership.IsComplete))
            {
                ownershipFlow.Add(methodOwnership);
            }
            if (!r.HasCaller)
            {
                if (r.Diagnostic is not null)
                    diagnostics.Add(r.Diagnostic);
                continue;
            }
            switch (r.Mode)
            {
                case CallerUnsafeMode.Explicit: expl++; break;
                case CallerUnsafeMode.Implicit: impl++; break;
                default: none++; break;
            }
            declaredMethods.Add(r.Caller!);
            if (!r.UnsafeEvidence.IsDefaultOrEmpty)
                unsafeEvidence.AddRange(r.UnsafeEvidence);
            if (r.IsLeverage)
                unsafeLeverageMethods.Add(r.Caller!);
            if (r.HasBody)
                methods.Add(r.Caller!);
            if (!r.Calls.IsDefaultOrEmpty)
                calls.AddRange(r.Calls);
            if (!r.Allocations.IsDefaultOrEmpty)
                allocationOccurrences[r.Token] = r.Allocations;
            if (!r.Unsafety.IsDefaultOrEmpty)
                unsafetyOccurrences[r.Token] = r.Unsafety;
            if (!r.Opportunities.IsDefaultOrEmpty)
                optimizationOpportunities.AddRange(r.Opportunities);
            if (r.Suppressed)
                suppressedOpportunityTokens.Add(r.Token);
            if (r.HasSignals)
                bodySignals[r.Token] = r.Signals;
            if (r.Diagnostic is not null)
                diagnostics.Add(r.Diagnostic);
            if (r.DeclaredSource is { } declaredSource)
                declaredSources[r.Token] = declaredSource;
        }

        var methodArray = methods.ToImmutable();
        var directCalls = calls.ToImmutable();
        var nonHeapNewObjOperandTokens = _includeMethodEvidence
            ? ComputeNonHeapNewObjOperandTokens(directCalls)
            : new HashSet<int>();
        LeakTriageResult? leakTriageResult = _includeLeakTriage
            ? new LeakTriageResult(
                leakFindings.ToImmutable(),
                leakCandidates.ToImmutable())
            {
                ExceptionPathCandidates =
                    exceptionPathCandidates.ToImmutable(),
                Failures = leakFailures.ToImmutable(),
            }
            : null;
        return new(
            Methods: new(
                DeclaredMethods: declaredMethods.ToImmutable(),
                Methods: methodArray,
                DirectCalls: directCalls,
                BodySignals: bodySignals,
                InAssemblyTypeIsException: _includeMethodEvidence
                    ? BuildInAssemblyExceptionMap()
                    : new Dictionary<
                        (string Namespace, string Name),
                        bool>(),
                NonHeapNewObjOperandTokens:
                    nonHeapNewObjOperandTokens,
                DeclaredSources: declaredSources),
            Safety: new(
                Evidence: unsafeEvidence.ToImmutable(),
                LeverageMethods: unsafeLeverageMethods.ToImmutable(),
                UpdatedRulesEnabled: _primaryMetadataResolver.MemorySafetyRulesEnabled,
                Modes: new UnsafeModeBreakdown(none, impl, expl),
                Occurrences: unsafetyOccurrences),
            Allocations: new(allocationOccurrences),
            Optimizations: new(
                Opportunities: optimizationOpportunities.ToImmutable(),
                SuppressedMethodTokens: suppressedOpportunityTokens,
                ExceptionTypeNames: _exceptionTypeNames),
            OwnershipFlow: new(ownershipFlow.ToImmutable()),
            Resources: new(leakTriageResult),
            Diagnostics: diagnostics.ToImmutable());
    }

    // (struct/enum) and therefore do not allocate on the heap (#1804). Classified here,
    // during Build, where the metadata reader is available — the lazy signal and
    // allocation-density paths run after the reader is released, so they consult this set
    // by operand token. Resolves: framework/in-assembly value types by name, in-assembly
    // value-type definitions, and cross-assembly GENERIC structs via the TypeSpec
    // signature blob (the same authority box detection uses). A cross-assembly NON-generic
    // user struct is a bare TypeRef whose value-type-ness is unresolvable from this
    // assembly alone, so it is intentionally excluded (an owned false positive at the
    // no-referenced-assembly-loading boundary, like the rung-2 `*Exception` suffix).
    HashSet<int> ComputeNonHeapNewObjOperandTokens(ImmutableArray<DirectCall> directCalls)
    {
        var set = new HashSet<int>();
        foreach (var call in directCalls)
        {
            if (call.Kind != CallKind.NewObject || set.Contains(call.OperandToken))
                continue;
            if (_primaryMetadataResolver.IsNonHeapNewObj(
                    call.OperandToken,
                    call.Callee.DeclaringType))
                set.Add(call.OperandToken);
        }
        return set;
    }

    // Classifies in-assembly types by whether they derive from System.Exception,
    // keyed by the same (namespace, name) the call index produces for a constructed
    // type (TypeRefDecoder, so nested types key as "Outer+Inner" and generic types
    // keep their arity-backtick name). MethodSignalAnalysis consults this so a
    // constructed in-assembly `*Exception` lookalike that does not actually derive
    // from System.Exception is not counted (#1572). Only types we can resolve
    // authoritatively (the base chain reaches System.Exception or a known root such
    // as System.Object) are recorded; a type whose chain hits an unresolvable
    // external/generic base is omitted, so it falls back to the conservative
    // name-suffix heuristic on its own name rather than on an unresolved base.
    Dictionary<(string Namespace, string Name), bool> BuildInAssemblyExceptionMap()
    {
        var map = new Dictionary<(string, string), bool>();
        foreach (var handle in _reader.TypeDefinitions)
        {
            if (ClassifyException(handle) is bool derives)
            {
                var typeRef = TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, handle, 0);
                map[(typeRef.Namespace, typeRef.Name)] = derives;
            }
        }
        return map;

        // Tri-state base-chain walk: true = derives from System.Exception; false =
        // definitely does not (the chain reaches System.Object/ValueType/Enum); null
        // = cannot be determined here (an unresolved external base or a generic
        // TypeSpecification base), so the caller defers to the name-suffix heuristic.
        // In-assembly bases are followed; only a definitive framework anchor resolves
        // the chain. The earlier "external base name ends with Exception" shortcut is
        // intentionally gone: it produced both false positives (a non-exception
        // external `*Exception` base) and authoritative false negatives (a real
        // exception whose external base does not end in "Exception").
        bool? ClassifyException(TypeDefinitionHandle start)
        {
            var visited = new HashSet<TypeDefinitionHandle>();
            var current = start;
            while (visited.Add(current))
            {
                var baseHandle = _reader.GetTypeDefinition(current).BaseType;
                if (baseHandle.IsNil)
                    return false;
                switch (baseHandle.Kind)
                {
                    case HandleKind.TypeReference:
                        var baseRef = _reader.GetTypeReference((TypeReferenceHandle)baseHandle);
                        var ns = _reader.GetString(baseRef.Namespace);
                        var name = _reader.GetString(baseRef.Name);
                        if (ns == "System" && name == "Exception")
                            return true;
                        if (ns == "System" && name is "Object" or "ValueType" or "Enum")
                            return false;
                        return null;
                    case HandleKind.TypeDefinition:
                        current = (TypeDefinitionHandle)baseHandle;
                        continue;
                    default:
                        return null;
                }
            }
            return false;
        }
    }

    IReadOnlySet<string> ComputeExceptionTypeNames()
    {
        var cache = new Dictionary<TypeDefinitionHandle, bool>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            if (IsExceptionTypeDefinition(typeHandle, cache))
                names.Add(TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, typeHandle, 0).ToQualifiedDisplayString());
        }
        return names;
    }

    bool IsExceptionTypeDefinition(TypeDefinitionHandle typeHandle, Dictionary<TypeDefinitionHandle, bool> cache)
    {
        if (cache.TryGetValue(typeHandle, out bool cached))
            return cached;

        var baseHandle = _reader.GetTypeDefinition(typeHandle).BaseType;
        if (baseHandle.IsNil)
        {
            cache[typeHandle] = false;
            return false;
        }
        bool result = baseHandle.Kind switch
        {
            HandleKind.TypeReference => IsExceptionReference(MetadataTokens.TypeReferenceHandle(MetadataTokens.GetRowNumber(baseHandle))),
            HandleKind.TypeDefinition => IsExceptionTypeDefinition(MetadataTokens.TypeDefinitionHandle(MetadataTokens.GetRowNumber(baseHandle)), cache),
            _ => false,
        };
        cache[typeHandle] = result;
        return result;
    }

    bool IsExceptionReference(TypeReferenceHandle handle)
    {
        var type = TypeRefDecoder.Instance.GetTypeFromReference(_reader, handle, 0);
        return type.Name.EndsWith("Exception", StringComparison.Ordinal);
    }

}
