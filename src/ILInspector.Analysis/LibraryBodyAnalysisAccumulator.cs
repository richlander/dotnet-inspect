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
    readonly bool _isScoped;
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
        _isScoped = plan.IsScoped;
        _exceptionTypeNames = _includeMethodEvidence
            ? ComputeExceptionTypeNames()
            : new HashSet<string>(StringComparer.Ordinal);
    }

    internal LibraryBodyAnalysisResult Build(
        IReadOnlyList<LibraryMethodAnalysisResult> results)
    {
        var declaredMethods = ImmutableArray.CreateBuilder<MethodIdentity>();
        var methods = ImmutableArray.CreateBuilder<MethodIdentity>();
        var failedMethodBodies =
            ImmutableArray.CreateBuilder<FailedMethodBodyAnalysis>();
        var declaredMethodsByBody =
            new Dictionary<MethodIdentity, MethodIdentity>();
        var unsafeLeverageMethods = ImmutableArray.CreateBuilder<MethodIdentity>();
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var resultSinks = ImmutableArray.CreateBuilder<MethodResultSink>();
        var fieldStores = ImmutableArray.CreateBuilder<FieldStoreFact>();
        var fieldLoads = ImmutableArray.CreateBuilder<FieldLoadFact>();
        var returnFlows =
            ImmutableArray.CreateBuilder<MethodReturnFlow>();
        var localThrows = ImmutableArray.CreateBuilder<MethodLocalThrowEvidence>();
        var unsafeEvidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();
        var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        var optimizationOpportunities = ImmutableArray.CreateBuilder<OptimizationOpportunity>();
        var stringMaterializations =
            ImmutableArray.CreateBuilder<
                StringMaterializationOccurrence>();
        var bodySignals = new Dictionary<int, BodySignals>();
        var implementationProfiles =
            ImmutableArray.CreateBuilder<MethodBodyImplementationMetrics>();
        var allocationOccurrences = new Dictionary<int, ImmutableArray<AllocationOccurrence>>();
        var unsafetyOccurrences = new Dictionary<int, ImmutableArray<UnsafetyOccurrence>>();
        var suppressedOpportunityTokens = new HashSet<int>();
        var scopeExcludedOpportunityTokens =
            new HashSet<int>();
        var leakFindings = ImmutableArray.CreateBuilder<LeakTriageFinding>();
        var leakCandidates = ImmutableArray.CreateBuilder<LeakTriageCandidate>();
        var exceptionPathCandidates =
            ImmutableArray.CreateBuilder<ArrayPoolExceptionPathCandidate>();
        var leakFailures =
            ImmutableArray.CreateBuilder<LeakTriageFailure>();
        var ownershipFlow =
            ImmutableArray.CreateBuilder<ArrayPoolOwnershipMethodEvidence>();
        var declaredSources = new Dictionary<int, MethodIdentity>();
        var implementationMetrics =
            ImmutableArray
                .CreateBuilder<MethodImplementationMetricEvidence>();
        var implementationMetricDiagnostics =
            ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        int none = 0, impl = 0, expl = 0, unavailable = 0;

        foreach (var result in results)
        {
            if (result.HasCaller
                && result.DeclaredMethod is not null)
            {
                declaredMethodsByBody[result.Caller!] =
                    result.DeclaredMethod;
            }
        }

        ImmutableArray<MethodIdentity> physicalMethods =
        [
            .. results
                .Where(static result => result.HasCaller)
                .Select(static result => result.Caller!),
        ];
        var methodMap = MethodDefinitionMap.Create(
            physicalMethods,
            _primaryMetadataResolver.ModuleName);
        Dictionary<int, MethodIdentity> methodsByToken =
            physicalMethods.ToDictionary(
                static method => method.MetadataToken);

        // Merge per-method results in metadata order, reproducing the exact sequence of appends
        // the original sequential loop performed. A method that hit a recoverable failure carries
        // its partial contributions (accumulated before the throw) alongside its diagnostic, so
        // even the failure path is byte-identical to the sequential build.
        foreach (var r in results)
        {
            if (r.LocalThrows is { } methodLocalThrows)
                localThrows.Add(methodLocalThrows);
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
                if (r.HasBody
                    && r.Diagnostic is { } failedBodyDiagnostic)
                {
                    failedMethodBodies.Add(
                        new FailedMethodBodyAnalysis(
                            r.Token,
                            failedBodyDiagnostic));
                }
                if (r.Diagnostic is not null)
                    diagnostics.Add(r.Diagnostic);
                continue;
            }
            switch (r.Mode)
            {
                case CallerUnsafeMode.Explicit: expl++; break;
                case CallerUnsafeMode.Implicit: impl++; break;
                case CallerUnsafeMode.Unavailable: unavailable++; break;
                default: none++; break;
            }
            declaredMethods.Add(r.Caller!);
            ImmutableArray<DirectCall> normalizedCalls =
                NormalizeSameImageCallContracts(
                    r.Calls,
                    methodMap,
                    methodsByToken);
            if (!r.UnsafeEvidence.IsDefaultOrEmpty
                || !normalizedCalls.IsDefaultOrEmpty)
            {
                unsafeEvidence.AddRange(
                    ReconcileCallSafetyEvidence(
                        r.UnsafeEvidence,
                        normalizedCalls));
            }
            if (r.IsLeverage)
                unsafeLeverageMethods.Add(r.Caller!);
            if (r.HasBody)
                methods.Add(r.Caller!);
            if (!normalizedCalls.IsDefaultOrEmpty)
            {
                calls.AddRange(
                    normalizedCalls.Select(call =>
                    {
                        MethodIdentity declared =
                            ResolveDeclaredMethod(
                                call.Caller,
                                declaredMethodsByBody);
                        return declared == call.Caller
                            ? call
                            : call with { Caller = declared };
                    }));
            }
            if (!r.ResultSinks.IsDefaultOrEmpty)
            {
                resultSinks.AddRange(
                    r.ResultSinks.Select(sink =>
                    {
                        MethodIdentity declared =
                            ResolveDeclaredMethod(
                                sink.Caller,
                                declaredMethodsByBody);
                        return declared == sink.Caller
                            ? sink
                            : sink with { Caller = declared };
                    }));
            }
            if (!r.FieldStores.IsDefaultOrEmpty)
            {
                fieldStores.AddRange(
                    r.FieldStores.Select(store =>
                    {
                        MethodIdentity declared =
                            ResolveDeclaredMethod(
                                store.Caller,
                                declaredMethodsByBody);
                        return declared == store.Caller
                            ? store
                            : store with { Caller = declared };
                    }));
            }
            if (!r.FieldLoads.IsDefaultOrEmpty)
            {
                fieldLoads.AddRange(
                    r.FieldLoads.Select(load =>
                    {
                        MethodIdentity declared =
                            ResolveDeclaredMethod(
                                load.Caller,
                                declaredMethodsByBody);
                        return declared == load.Caller
                            ? load
                            : load with { Caller = declared };
                    }));
            }
            if (!r.ReturnFlows.IsDefaultOrEmpty)
            {
                returnFlows.AddRange(
                    r.ReturnFlows.Select(flow =>
                    {
                        MethodIdentity declared =
                            ResolveDeclaredMethod(
                                flow.Caller,
                                declaredMethodsByBody);
                        return declared == flow.Caller
                            ? flow
                            : flow with { Caller = declared };
                    }));
            }
            if (!r.Allocations.IsDefaultOrEmpty)
                allocationOccurrences[r.Token] = r.Allocations;
            if (!r.Unsafety.IsDefaultOrEmpty)
                unsafetyOccurrences[r.Token] = r.Unsafety;
            if (!r.Opportunities.IsDefaultOrEmpty)
                optimizationOpportunities.AddRange(r.Opportunities);
            if (!r.StringMaterializations.IsDefaultOrEmpty)
            {
                stringMaterializations.AddRange(
                    r.StringMaterializations.Select(
                        occurrence =>
                        {
                            MethodIdentity declared =
                                ResolveDeclaredMethod(
                                    occurrence.Method,
                                    declaredMethodsByBody);
                            return declared
                                    == occurrence.Method
                                ? occurrence
                                : occurrence with
                                {
                                    Method = declared,
                                };
                        }));
            }
            if (r.Suppressed)
                suppressedOpportunityTokens.Add(r.Token);
            if (r.ScopeExcluded)
                scopeExcludedOpportunityTokens.Add(r.Token);
            if (r.HasSignals)
                bodySignals[r.Token] = r.Signals;
            if (r.ImplementationMetrics is { } implementationMetric)
                implementationMetrics.Add(implementationMetric);
            if (r.ImplementationMetricDiagnostic is { } metricDiagnostic)
                implementationMetricDiagnostics.Add(metricDiagnostic);
            if (r.ImplementationProfile is { } implementationProfile)
            {
                if (r.Diagnostic is { } profileDiagnostic)
                {
                    implementationProfile = implementationProfile with
                    {
                        IncompleteReasons =
                        [
                            .. implementationProfile.IncompleteReasons,
                            profileDiagnostic.Message,
                        ],
                    };
                }
                implementationProfiles.Add(implementationProfile);
            }
            if (r.Diagnostic is not null)
                diagnostics.Add(r.Diagnostic);
            if (r.DeclaredSource is { } declaredSource)
                declaredSources[r.Token] = declaredSource;
        }

        var methodArray = methods.ToImmutable();
        var directCalls = calls.ToImmutable();
        bool fieldAccessCensusComplete =
            results.All(result =>
                !result.RequiresCompleteFieldAccessCensus
                || result.FieldAccessCensusComplete);
        HashSet<TypeRef> typesWithCurrentInstanceMutations =
        [
            .. results
                .Where(result =>
                    result.Caller is not null
                    && !result.CurrentInstanceMutations.IsDefaultOrEmpty)
                .Select(result => result.Caller!.DeclaringType),
        ];
        RemoveExternallyStoredAsyncFieldSources(
            resultSinks,
            fieldStores,
            fieldLoads,
            typesWithCurrentInstanceMutations,
            _isScoped || !fieldAccessCensusComplete);
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
                FailedMethodBodies: failedMethodBodies.ToImmutable(),
                DirectCalls: directCalls,
                ResultSinks: resultSinks.ToImmutable(),
                FieldStores: fieldStores.ToImmutable(),
                FieldLoads: fieldLoads.ToImmutable(),
                ReturnFlows: returnFlows.ToImmutable(),
                BodySignals: bodySignals,
                ImplementationMetrics:
                    implementationMetrics.ToImmutable(),
                ImplementationMetricDiagnostics:
                    implementationMetricDiagnostics.ToImmutable(),
                ImplementationProfiles:
                    implementationProfiles.ToImmutable(),
                InAssemblyTypeIsException: _includeMethodEvidence
                    ? BuildInAssemblyExceptionMap()
                    : new Dictionary<
                        (string Namespace, string Name),
                        bool>(),
                NonHeapNewObjOperandTokens:
                    nonHeapNewObjOperandTokens,
                DeclaredSources: declaredSources,
                LocalThrows: localThrows.ToImmutable()),
            Safety: new(
                Evidence: unsafeEvidence.ToImmutable(),
                LeverageMethods: unsafeLeverageMethods.ToImmutable(),
                Rules: _primaryMetadataResolver.MemorySafetyRules,
                Modes: new UnsafeModeBreakdown(
                    none,
                    impl,
                    expl,
                    unavailable),
                Occurrences: unsafetyOccurrences),
            Allocations: new(allocationOccurrences),
            Optimizations: new(
                Opportunities: optimizationOpportunities.ToImmutable(),
                StringMaterializations:
                    stringMaterializations.ToImmutable(),
                SuppressedMethodTokens: suppressedOpportunityTokens,
                ScopeExcludedMethodTokens:
                    scopeExcludedOpportunityTokens,
                ExceptionTypeNames: _exceptionTypeNames),
            OwnershipFlow: new(ownershipFlow.ToImmutable()),
            Resources: new(leakTriageResult),
            Diagnostics: diagnostics.ToImmutable());
    }

    static ImmutableArray<DirectCall> NormalizeSameImageCallContracts(
        ImmutableArray<DirectCall> calls,
        MethodDefinitionMap methodMap,
        IReadOnlyDictionary<int, MethodIdentity> methodsByToken)
    {
        if (calls.IsDefaultOrEmpty)
            return calls;

        return
        [
            .. calls.Select(call =>
            {
                if (call.Kind is not (
                    CallKind.Call
                    or CallKind.CallVirtual
                    or CallKind.NewObject))
                {
                    return call;
                }

                int targetToken = methodMap.Resolve(call);
                return targetToken != 0
                    && methodsByToken.TryGetValue(
                        targetToken,
                        out MethodIdentity? target)
                    ? call with
                    {
                        TargetCallerUnsafeMode =
                            target.CallerUnsafeMode,
                    }
                    : call;
            }),
        ];
    }

    static ImmutableArray<UnsafeEvidence> ReconcileCallSafetyEvidence(
        ImmutableArray<UnsafeEvidence> evidence,
        ImmutableArray<DirectCall> calls)
    {
        if (calls.IsDefaultOrEmpty)
            return evidence;

        IEnumerable<UnsafeEvidence> nonCallEvidence =
            evidence.IsDefaultOrEmpty
                ? []
                : evidence.Where(
                    static item => item.Reason != "Unsafe call");
        IEnumerable<UnsafeEvidence> callEvidence =
            calls.Where(
                    static call =>
                        call.Kind != CallKind.CallIndirect)
                .Select(call =>
                    MethodSafetyAnalysis.InspectCall(
                        call.Caller,
                        call.Callee,
                        call.Kind,
                        call.ILOffset,
                        call.OperandToken,
                        call.TargetCallerUnsafeMode))
                .OfType<UnsafeEvidence>();

        return
        [
            .. nonCallEvidence
                .Concat(callEvidence)
                .OrderBy(static item => item.ILOffset ?? int.MinValue),
        ];
    }

    static void RemoveExternallyStoredAsyncFieldSources(
        ImmutableArray<MethodResultSink>.Builder resultSinks,
        ImmutableArray<FieldStoreFact>.Builder fieldStores,
        ImmutableArray<FieldLoadFact>.Builder fieldLoads,
        IReadOnlySet<TypeRef> typesWithCurrentInstanceMutations,
        bool withholdWholeAssemblyProof)
    {
        for (int index = 0; index < resultSinks.Count; index++)
        {
            MethodResultSink sink = resultSinks[index];
            if (sink.StateMachineFieldSource is not { } source)
                continue;

            bool hasExternalStore = fieldStores.Any(store =>
                store.EvidenceMethod != sink.EvidenceMethod
                && store.IsReachable != false
                && source.Field.MightBeSameFieldAs(
                    store.Identity));
            bool hasExternalAddressEscape = fieldLoads.Any(load =>
                load.EvidenceMethod != sink.EvidenceMethod
                && load.IsAddress
                && load.IsReachable != false
                && source.Field.MightBeSameFieldAs(
                    load.Identity));
            if (!withholdWholeAssemblyProof
                && !hasExternalStore
                && !hasExternalAddressEscape
                && !typesWithCurrentInstanceMutations.Contains(
                    source.Field.DeclaringType))
            {
                continue;
            }

            resultSinks[index] = sink with
            {
                StateMachineFieldSource = null,
            };
        }
    }

    static MethodIdentity ResolveDeclaredMethod(
        MethodIdentity method,
        IReadOnlyDictionary<MethodIdentity, MethodIdentity>
            declaredMethodsByBody)
    {
        MethodIdentity current = method;
        for (int depth = 0;
            depth <= declaredMethodsByBody.Count;
            depth++)
        {
            if (!declaredMethodsByBody.TryGetValue(
                    current,
                    out MethodIdentity? declared)
                || declared == current)
            {
                return current;
            }
            current = declared;
        }

        throw new InvalidOperationException(
            "Declared-method resolution contains a cycle.");
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

        var path = new List<TypeDefinitionHandle>();
        bool result = false;
        while (!cache.TryGetValue(typeHandle, out result))
        {
            // A cycle must terminate before local-throw qualification runs.
            cache[typeHandle] = false;
            path.Add(typeHandle);
            EntityHandle baseHandle = _reader.GetTypeDefinition(typeHandle).BaseType;
            if (baseHandle.IsNil)
                break;
            if (baseHandle.Kind == HandleKind.TypeDefinition)
            {
                typeHandle = (TypeDefinitionHandle)baseHandle;
                continue;
            }
            result = baseHandle.Kind == HandleKind.TypeReference
                && IsExceptionReference((TypeReferenceHandle)baseHandle);
            break;
        }
        foreach (TypeDefinitionHandle visited in path)
            cache[visited] = result;
        return result;
    }

    bool IsExceptionReference(TypeReferenceHandle handle)
    {
        var type = TypeRefDecoder.Instance.GetTypeFromReference(_reader, handle, 0);
        return type.Name.EndsWith("Exception", StringComparison.Ordinal);
    }

}
