using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis;

/// <summary>Materialized IL body evidence for one assembly.</summary>
public sealed class LibraryBodyIndex
{
    LibraryBodyIndex(
        string path,
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<DirectCall> directCalls,
        ImmutableArray<UnsafeEvidence> unsafeEvidence,
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        ImmutableArray<OptimizationOpportunity> optimizationOpportunities,
        ImmutableArray<MethodIdentity> unsafeLeverageMethods,
        bool memorySafetyRulesEnabled,
        UnsafeModeBreakdown unsafeModes,
        IReadOnlyDictionary<int, BodySignals> bodySignals,
        IReadOnlyDictionary<(string Namespace, string Name), bool> inAssemblyTypeIsException,
        IReadOnlySet<int> suppressedOpportunityTokens,
        IReadOnlySet<string> exceptionTypeNames,
        IReadOnlySet<int> nonHeapNewObjOperandTokens)
    {
        Path = path;
        Methods = methods;
        DirectCalls = directCalls;
        UnsafeEvidence = unsafeEvidence;
        Diagnostics = diagnostics;
        _rawOpportunities = optimizationOpportunities;
        _unsafeLeverageMethods = unsafeLeverageMethods;
        MemorySafetyRulesEnabled = memorySafetyRulesEnabled;
        UnsafeModes = unsafeModes;
        _bodySignals = bodySignals;
        _inAssemblyTypeIsException = inAssemblyTypeIsException;
        _suppressedOpportunityTokens = suppressedOpportunityTokens;
        _exceptionTypeNames = exceptionTypeNames;
        _nonHeapNewObjOperandTokens = nonHeapNewObjOperandTokens;
    }

    public string Path { get; }
    public ImmutableArray<MethodIdentity> Methods { get; }
    public ImmutableArray<DirectCall> DirectCalls { get; }
    public ImmutableArray<UnsafeEvidence> UnsafeEvidence { get; }
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    readonly ImmutableArray<OptimizationOpportunity> _rawOpportunities;
    readonly ImmutableArray<MethodIdentity> _unsafeLeverageMethods;
    ImmutableArray<OptimizationOpportunity> _opportunities;

    /// <summary>
    /// Source/IL optimization opportunities, each enriched with the containing method's
    /// <see cref="MethodLeverage.RootReach"/> so callers can prioritize the intersection
    /// of high-leverage methods and actionable rewrite shapes. Computed once on first
    /// access (the leverage join walks the whole-assembly call graph).
    /// </summary>
    public ImmutableArray<OptimizationOpportunity> OptimizationOpportunities
    {
        get
        {
            if (_opportunities.IsDefault)
            {
                var reachByToken = new Dictionary<int, int>();
                foreach (var entry in TopLeverage(int.MaxValue))
                    reachByToken[entry.Method.MetadataToken] = entry.RootReach;
                _opportunities =
                [
                    .. _rawOpportunities.Select(opportunity =>
                    {
                        int reach = reachByToken.TryGetValue(opportunity.Method.MetadataToken, out int r) ? r : opportunity.RootReach;
                        var adjusted = reach != opportunity.RootReach ? opportunity with { RootReach = reach } : opportunity;
                        var confidence = AdjustDelegateConfidenceForReach(adjusted.Shape, adjusted.InLoop, adjusted.Confidence, reach);
                        return confidence != adjusted.Confidence ? adjusted with { Confidence = confidence } : adjusted;
                    }),
                    .. AllocationHotspots(reachByToken, new HashSet<int>(_rawOpportunities.Select(o => o.Method.MetadataToken))),
                    .. ScanMethodsInvokedInLoops(reachByToken),
                ];
            }
            return _opportunities;
        }
    }

    bool IsExceptionConstruction(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
        if (_exceptionTypeNames.Contains(definition.ToQualifiedDisplayString()))
            return true;
        if (Methods.Length > 0
            && definition.Assembly == Methods[0].AssemblyName)
            return false;
        return definition.Name.EndsWith("Exception", StringComparison.Ordinal);
    }

    // A method that allocates densely is a real perf signal only when the allocations are
    // both REPEATED (in a loop) and not already pinpointed by a specific shape — otherwise
    // the count is dominated by intrinsic, non-reducible construction (e.g. a serializer
    // building its output model), which floods the section on allocation-heavy assemblies.
    // So allocation-hotspot fires only for a method that (1) is not already covered by a
    // specific-shape row, (2) allocates inside a loop, and (3) clears a high density bar.
    // Exception-construction allocations are excluded (throw-path only), and source-/
    // compiler-generated methods are suppressed just as for shaped opportunities.
    const int AllocationHotspotThreshold = 16;

    // A non-loop delegate is allocated once per call, so it is low-value in a cold method —
    // but on a high-reach (widely-reached, hot) method it is a real per-call heap allocation
    // worth surfacing. Lift such rows from "low" to "medium" so genuinely hot escaping
    // delegates are not buried among the cold one-shots. Threshold chosen against real
    // assemblies (on Aspire.Dashboard this promotes ~19 of 293 non-loop delegate rows).
    public const int DelegateHotRootReach = 10;

    // Adjust a delegate row's confidence once its method's RootReach is known: a cold-looking
    // (low) non-loop delegate on a high-reach method becomes medium. Loop delegates (already
    // high) and non-delegate shapes are unchanged. Pure for testability.
    public static string AdjustDelegateConfidenceForReach(string shape, bool inLoop, string confidence, int rootReach)
        => !inLoop
            && confidence == "low"
            && rootReach >= DelegateHotRootReach
            && shape is "capturing-delegate" or "instance-method-group-delegate"
            ? "medium"
            : confidence;

    IEnumerable<OptimizationOpportunity> AllocationHotspots(Dictionary<int, int> reachByToken, IReadOnlySet<int> methodsWithSpecificShape)
    {
        var methodByToken = new Dictionary<int, MethodIdentity>(Methods.Length);
        foreach (var method in Methods)
            methodByToken[method.MetadataToken] = method;

        // Per-method steady-state object allocations (newobj that is not an exception
        // constructor) and whether any such allocation is on a loop back-edge.
        var steadyNewobj = new Dictionary<int, int>();
        var steadyNewobjLoop = new HashSet<int>();
        foreach (var call in DirectCalls)
        {
            if (call.Kind != CallKind.NewObject)
                continue;
            if (call.Callee.Kind != MemberKind.Unsupported
                && IsExceptionConstruction(call.Callee.DeclaringType))
                continue;
            // A `newobj` of a value type (Span/Nullable/ValueTuple/in-assembly struct, or a
            // cross-assembly generic struct resolved via the TypeSpec blob during Build) does
            // not allocate on the heap, so it must not count toward allocation density (#1804).
            if (_nonHeapNewObjOperandTokens.Contains(call.OperandToken))
                continue;
            int token = call.Caller.MetadataToken;
            steadyNewobj[token] = steadyNewobj.GetValueOrDefault(token) + 1;
            if (call.InLoop)
                steadyNewobjLoop.Add(token);
        }

        foreach (var (token, method) in methodByToken)
        {
            if (_suppressedOpportunityTokens.Contains(token))
                continue;
            // Dedup: a method already pinpointed by a specific shape (delegate/box/array/…)
            // doesn't also need a vague aggregate-density row; that only double-counts and
            // drowns the actionable specific rows.
            if (methodsWithSpecificShape.Contains(token))
                continue;
            _bodySignals.TryGetValue(token, out var body);
            // Only loop allocations are repeated; a once-per-call dense method is usually
            // intrinsic construction (e.g. building an output object), not reducible waste.
            bool inLoop = steadyNewobjLoop.Contains(token) || body.AllocInLoop;
            if (!inLoop)
                continue;
            int allocations = steadyNewobj.GetValueOrDefault(token) + body.Newarr + body.Boxes;
            if (allocations < AllocationHotspotThreshold)
                continue;
            yield return new OptimizationOpportunity(
                method,
                "allocation-hotspot",
                $"{allocations} heap allocations in a loop (newobj/newarr/box)",
                "Many allocations in one loop are often reducible: pool or cache reused objects, use spans/stackalloc for transient buffers, and avoid intermediate collections on hot paths.",
                "medium",
                true,
                null,
                "Aggregate loop-allocation density (excludes exception construction); some may be intrinsic object construction. Review the loop body for reducible temporaries.",
                reachByToken.GetValueOrDefault(token));
        }
    }

    // A membership/search LINQ terminal on System.Linq.Enumerable: one that walks the
    // sequence to answer a lookup/membership question and whose canonical fix is an
    // indexed lookup (HashSet/Dictionary). Lazy operators (Where/Select/OrderBy) are
    // excluded — they do not enumerate at the call site — as are materializers
    // (ToArray/ToList), which have a different fix shape.
    //
    // Only the predicate/value overloads do real O(n) work. The parameterless positional
    // and aggregate overloads (First(), Single(), Count(), Any()) are O(1) — a positional
    // read, or the ICollection.Count fast path — so they are NOT scans and must not be
    // flagged. Every scanning overload takes the source plus a predicate/value, so it has
    // at least two parameters in Enumerable's static signature; gate on that arity.
    public static bool IsLinqMembershipScan(MemberRef member, out string op)
    {
        op = "";
        if (member.Kind == MemberKind.Unsupported)
            return false;
        if (!IsEnumerableDefinition(member.DeclaringType))
            return false;
        if (member.ParameterTypes.Length < 2)
            return false;
        switch (member.Name)
        {
            case "Any":
            case "All":
            case "First":
            case "FirstOrDefault":
            case "Last":
            case "LastOrDefault":
            case "Single":
            case "SingleOrDefault":
            case "Count":
            case "LongCount":
            case "Contains":
                op = member.Name;
                return true;
            default:
                return false;
        }
    }

    // System.Linq.Enumerable across target frameworks: the type lives in System.Linq on
    // .NET 5+ reference assemblies, in System.Core on .NET Framework, and canonicalizes to
    // the core library on netstandard. IsKnownFrameworkType matches all three facades and
    // also enforces the public-key-token trust gate (#1708 Row A), so a user assembly named
    // System.Linq exposing an Enumerable lookalike is not mistaken for real LINQ.
    static bool IsEnumerableDefinition(TypeRef type)
        => FrameworkIdentity.IsKnownFrameworkType(type, "System.Linq", "System.Linq", "Enumerable");

    // System.String.Concat — the lowering of the `+` / `+=` string operators (and of simple
    // interpolations like `$"{a}-{b}"`). Each call allocates a fresh string. Inside a loop,
    // when the result is stored back into one of its own inputs, it is the StringBuilder
    // anti-pattern: `s += …` repeatedly copies the growing accumulator (O(n^2)).
    public static bool IsStringConcat(MemberRef member)
        => member.Kind != MemberKind.Unsupported
            && member.Name == "Concat"
            && FrameworkIdentity.IsCoreLibraryType(member.DeclaringType, "System", "String");

    // A GetEnumerator call that returns a reference-type enumerator — i.e. iterating the
    // sequence allocates an enumerator object on the heap. `foreach` over a concrete type with a
    // struct enumerator (List<T>.Enumerator, …) returns it by value and allocates nothing; only
    // a foreach over an interface (IEnumerable/IEnumerable<T>) binds to GetEnumerator returning
    // the framework IEnumerator/IEnumerator<T> interface, whose implementation is a heap object.
    // The return type is matched by trusted-framework identity (#1708), not namespace+name, so a
    // user type that merely reuses the IEnumerator namespace and name is not mistaken for it.
    public static bool IsInterfaceEnumeratorAllocation(MemberRef member)
    {
        if (member.Kind == MemberKind.Unsupported || member.Name != "GetEnumerator")
            return false;
        var ret = member.ReturnType;
        var def = ret.Kind == TypeRefKind.GenericInstance ? ret.ElementType ?? ret : ret;
        return FrameworkIdentity.IsCoreLibraryType(def, "System.Collections.Generic", "IEnumerator`1")
            || FrameworkIdentity.IsCoreLibraryType(def, "System.Collections", "IEnumerator");
    }

    // A lazy/deferred Enumerable operator (Where/Select/…): it returns an iterator without
    // enumerating at the call site. A helper that returns such a query is itself a deferred
    // linear scan — the scan runs when the caller enumerates the result.
    static bool IsLinqLazyProducer(MemberRef member, out string op)
    {
        op = "";
        if (member.Kind == MemberKind.Unsupported || !IsEnumerableDefinition(member.DeclaringType))
            return false;
        switch (member.Name)
        {
            case "Where":
            case "Select":
            case "SelectMany":
            case "OfType":
            case "Cast":
                op = member.Name;
                return true;
            default:
                return false;
        }
    }

    // The method's return type is an enumerable sequence (IEnumerable / IEnumerable<T>),
    // i.e. it can hand back a deferred query for the caller to enumerate.
    static bool ReturnsEnumerableSequence(TypeRef returnType)
    {
        var def = returnType.Kind == TypeRefKind.GenericInstance ? returnType.ElementType ?? returnType : returnType;
        if (def.Kind == TypeRefKind.Unsupported)
            return false;
        return (def.Namespace == "System.Collections.Generic" && def.Name == "IEnumerable`1")
            || (def.Namespace == "System.Collections" && def.Name == "IEnumerable");
    }

    // Cross-method repeated scan: an in-assembly method that linearly scans a sequence is
    // itself invoked at a call site inside a loop, so the scan runs on every iteration even
    // though the scan and the loop live in different methods — the same O(n*m) shape as
    // linq-scan-in-loop, split across a call boundary where a single-method scan would miss
    // it. "Scans a sequence" covers both a method that performs a membership terminal
    // (IsLinqMembershipScan) and a method that returns a deferred Where/Select query the
    // caller enumerates (a lazy producer returning IEnumerable). Reported once per scanning
    // method, naming a representative looping caller.
    IEnumerable<OptimizationOpportunity> ScanMethodsInvokedInLoops(Dictionary<int, int> reachByToken)
    {
        var methodByToken = new Dictionary<int, MethodIdentity>(Methods.Length);
        foreach (var method in Methods)
            methodByToken[method.MetadataToken] = method;

        // Methods that linearly scan a sequence, keyed by the containing (caller) method
        // token. The recorded op is a representative for the evidence string. Two kinds:
        // a membership terminal in the body, or a deferred query returned for the caller to
        // enumerate (lazy producer whose return type is an enumerable sequence).
        var scanningMethods = new Dictionary<int, string>();
        var inAssemblyCallees = new Dictionary<int, HashSet<int>>();
        var lazyReturning = new Dictionary<int, string>();
        foreach (var call in DirectCalls)
        {
            if (IsLinqMembershipScan(call.Callee, out var membershipOp))
            {
                scanningMethods.TryAdd(call.Caller.MetadataToken, membershipOp);
            }
            else if (IsLinqLazyProducer(call.Callee, out var lazyOp)
                && methodByToken.TryGetValue(call.Caller.MetadataToken, out var producer)
                && ReturnsEnumerableSequence(producer.ReturnType))
            {
                lazyReturning.TryAdd(call.Caller.MetadataToken, lazyOp);
            }

            // Record in-assembly call edges for transitive lazy-producer propagation.
            if (methodByToken.ContainsKey(call.CalleeDefinitionToken))
            {
                if (!inAssemblyCallees.TryGetValue(call.Caller.MetadataToken, out var callees))
                    inAssemblyCallees[call.Caller.MetadataToken] = callees = [];
                callees.Add(call.CalleeDefinitionToken);
            }
        }

        // Transitive closure: a sequence-returning method that calls a known lazy-returning
        // method is itself treated as lazy-returning (e.g. an instance overload delegating to
        // a static Where helper, as in Aspire's OtlpSpan.GetChildSpans()).
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (callerToken, callees) in inAssemblyCallees)
            {
                if (lazyReturning.ContainsKey(callerToken))
                    continue;
                if (!methodByToken.TryGetValue(callerToken, out var m) || !ReturnsEnumerableSequence(m.ReturnType))
                    continue;
                foreach (var callee in callees)
                {
                    if (lazyReturning.TryGetValue(callee, out var op))
                    {
                        lazyReturning[callerToken] = op;
                        changed = true;
                        break;
                    }
                }
            }
        }

        foreach (var (token, op) in lazyReturning)
            scanningMethods.TryAdd(token, op);

        // Methods already reported by the in-method linq-scan-in-loop shape: do not also
        // emit a (weaker, low-confidence) cross-method row for the same method.
        var inMethodScanLoopTokens = new HashSet<int>();
        foreach (var opportunity in _rawOpportunities)
            if (opportunity.Shape == "linq-scan-in-loop")
                inMethodScanLoopTokens.Add(opportunity.Method.MetadataToken);

        var emitted = new HashSet<int>();
        foreach (var call in DirectCalls)
        {
            if (!call.InLoop)
                continue;
            int calleeToken = call.CalleeDefinitionToken;
            if (!scanningMethods.TryGetValue(calleeToken, out var op))
                continue;
            if (!methodByToken.TryGetValue(calleeToken, out var method))
                continue;
            if (_suppressedOpportunityTokens.Contains(calleeToken))
                continue;
            if (inMethodScanLoopTokens.Contains(calleeToken))
                continue;
            // A method that scans itself in a loop is already reported as linq-scan-in-loop;
            // and a method invoking itself recursively is not a caller-loop in the O(n*m) sense.
            if (call.Caller.MetadataToken == calleeToken)
                continue;
            if (!emitted.Add(calleeToken))
                continue;

            yield return new OptimizationOpportunity(
                method,
                "scan-method-in-loop-call",
                $"Linearly scans a sequence (Enumerable.{op}); invoked inside a loop by {call.Caller.DeclaringType.ToQualifiedDisplayString()}::{call.Caller.Name}",
                "A method that linearly scans a sequence is called on every iteration of a caller's loop; precompute an index the caller can reuse, or hoist the scan out of the loop.",
                "low",
                true,
                null,
                "Quadratic only if the scanned sequence grows with the caller's loop; confirm the sequence is the loop-variant collection and not small/constant.",
                reachByToken.GetValueOrDefault(calleeToken));
        }
    }

    /// <summary>
    /// Whether the module opted into the updated memory-safety rules via
    /// <c>MemorySafetyRulesAttribute</c> (Roslyn's <c>UseUpdatedMemorySafetyRules</c>).
    /// When false, every requires-unsafe member is <see cref="CallerUnsafeMode.Implicit"/>.
    /// </summary>
    public bool MemorySafetyRulesEnabled { get; }

    /// <summary>Per-<see cref="CallerUnsafeMode"/> method counts across the whole assembly.</summary>
    public UnsafeModeBreakdown UnsafeModes { get; }

    Dictionary<int, MethodSignals>? _signals;
    readonly IReadOnlyDictionary<int, BodySignals> _bodySignals;
    readonly IReadOnlyDictionary<(string Namespace, string Name), bool> _inAssemblyTypeIsException;
    readonly IReadOnlySet<int> _suppressedOpportunityTokens;
    readonly IReadOnlySet<string> _exceptionTypeNames;
    readonly IReadOnlySet<int> _nonHeapNewObjOperandTokens;

    /// <summary>
    /// Per-method analysis signals (allocations, copies, unsafe, reflection,
    /// throw/catch/finally, evidence offsets), keyed by metadata token. Computed once
    /// from the call index and the body-scan signals, reused by the call-graph builders.
    /// </summary>
    Dictionary<int, MethodSignals> Signals => _signals ??= MethodSignalAnalysis.Collect(DirectCalls, UnsafeEvidence, _bodySignals, _inAssemblyTypeIsException, _nonHeapNewObjOperandTokens);

    /// <summary>
    /// Returns per-method body/call signals keyed by metadata token.
    /// </summary>
    public IReadOnlyDictionary<int, MethodSignals> GetMethodSignals() => Signals;

    IReadOnlySet<string>? _generatedFrameworkTypes;

    /// <summary>
    /// Qualified names of types recognized as protobuf/gRPC generated implementation detail,
    /// detected structurally (no attributes are emitted on this code). A type qualifies when
    /// any of its methods bootstraps protobuf generated infrastructure — calling
    /// <c>Google.Protobuf.Reflection.FileDescriptor.FromGeneratedCode</c>, constructing
    /// <c>Google.Protobuf.Reflection.GeneratedClrTypeInfo</c>, or constructing the
    /// per-message <c>Google.Protobuf.MessageParser&lt;T&gt;</c> — where the bootstrap type
    /// comes from the real <c>Google.Protobuf</c> assembly (a user assembly can declare
    /// <c>Google.Protobuf.*</c> lookalikes, so namespace/name alone is not sufficient,
    /// #1580) — or is a gRPC stub that both
    /// declares infrastructure members whose names are codegen-only (<c>__ServiceName</c>,
    /// <c>__Helper_*</c>, <c>__Marshaller_*</c>, <c>__Method_*</c>) <em>and</em> calls into
    /// <c>Grpc.Core</c> (the binding/marshalling APIs a generated stub uses). A generated
    /// member name alone is not sufficient — an ordinary user type can declare a
    /// <c>__Helper_*</c> method — so the structural <c>Grpc.Core</c> tie is required to avoid
    /// classifying user lookalikes as generated. gRPC binding calls
    /// (<c>ServerServiceDefinition</c>/<c>Marshallers</c>) are still not a signal on their own,
    /// since hand-written registration uses them without the generated members. These signals
    /// appear in generated protobuf/gRPC code, so perf triage can mark them in Top Leverage and
    /// suppress them from Performance Triage like other generated detail.
    /// </summary>
    public IReadOnlySet<string> GeneratedFrameworkTypeNames => _generatedFrameworkTypes ??= ComputeGeneratedFrameworkTypes();

    IReadOnlySet<string> ComputeGeneratedFrameworkTypes()
    {
        var generated = new HashSet<string>(StringComparer.Ordinal);

        foreach (var call in DirectCalls)
        {
            var callee = call.Callee;
            if (callee.Kind == MemberKind.Unsupported)
                continue;
            bool protobufBootstrap =
                (IsProtobufType(callee.DeclaringType, "Google.Protobuf.Reflection", "FileDescriptor")
                    && callee.Name == "FromGeneratedCode")
                || (IsProtobufType(callee.DeclaringType, "Google.Protobuf.Reflection", "GeneratedClrTypeInfo")
                    && callee.Name == ".ctor")
                || (IsProtobufType(callee.DeclaringType, "Google.Protobuf", "MessageParser")
                    && callee.Name == ".ctor");
            // Only protobuf generated bootstraps are matched by call: FromGeneratedCode,
            // GeneratedClrTypeInfo, and MessageParser<T> construction are codegen signals.
            // The bootstrap type must come from the real Google.Protobuf assembly: a user
            // assembly can declare Google.Protobuf.* lookalike types and call them from
            // ordinary product code, and name/namespace-only matching would misclassify
            // that product type as generated (#1580).
            // gRPC binding APIs (ServerServiceDefinition/Marshallers) are deliberately NOT
            // matched by call, since hand-written low-level gRPC registration legitimately
            // calls them; gRPC stubs are instead recognized by their generated __*
            // infrastructure members below.
            if (protobufBootstrap)
                generated.Add(call.Caller.DeclaringType.ToQualifiedDisplayString());
        }

        // gRPC service stubs declare marshaller/serialization-helper infrastructure members
        // whose names are codegen-only. A generated-looking member name is not enough on its
        // own — an ordinary user type can declare a __Helper_* method — so require a structural
        // tie to gRPC: the same type must also call into Grpc.Core (the binding/marshalling
        // APIs a generated stub uses). This keeps hand-written gRPC registration (Grpc.Core
        // calls but no __* members) and user lookalikes (__* members but no Grpc.Core calls)
        // out of the generated set.
        var typesCallingGrpcCore = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in DirectCalls)
        {
            if (call.Callee.Kind == MemberKind.Unsupported)
                continue;
            if (IsGrpcCoreNamespace(NamedDefinition(call.Callee.DeclaringType).Namespace))
                typesCallingGrpcCore.Add(call.Caller.DeclaringType.ToQualifiedDisplayString());
        }

        foreach (var method in Methods)
        {
            if (method.Name == "__ServiceName"
                || method.Name.StartsWith("__Helper_", StringComparison.Ordinal)
                || method.Name.StartsWith("__Marshaller_", StringComparison.Ordinal)
                || method.Name.StartsWith("__Method_", StringComparison.Ordinal))
            {
                var typeName = method.DeclaringType.ToQualifiedDisplayString();
                if (typesCallingGrpcCore.Contains(typeName))
                    generated.Add(typeName);
            }
        }

        return generated;
    }

    static TypeRef NamedDefinition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance && type.ElementType is { } element ? element : type;

    static bool IsGrpcCoreNamespace(string? ns)
        => ns is not null
            && (ns == "Grpc.Core" || ns.StartsWith("Grpc.Core.", StringComparison.Ordinal));

    static bool IsProtobufType(TypeRef type, string ns, string name)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        return definition is not null
            && definition.TrustedProtobufAssembly
            && definition.Assembly == "Google.Protobuf"
            && definition.Namespace == ns
            && StripGenericArity(definition.Name) == name;
    }

    static string StripGenericArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    public static LibraryBodyIndex Open(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
            throw new BadImageFormatException($"No managed metadata: {path}");
        var reader = peReader.GetMetadataReader();
        var builder = new IndexBuilder(path, reader, peReader);
        var index = builder.Build();
        return new LibraryBodyIndex(
            path, index.Methods, index.DirectCalls, index.UnsafeEvidence, index.Diagnostics,
            index.OptimizationOpportunities, index.UnsafeLeverageMethods, builder.MemorySafetyRulesEnabled, index.UnsafeModes,
            index.BodySignals, index.InAssemblyTypeIsException, index.SuppressedOpportunityTokens, index.ExceptionTypeNames,
            index.NonHeapNewObjOperandTokens);
    }

    public ImmutableArray<DirectCall> FindCalls(MemberPattern pattern)
        => [.. DirectCalls.Where(call => pattern.Matches(call.Callee))];

    /// <summary>
    /// The most-leveraged requires-unsafe methods, ranked by distinct direct
    /// callers — the highest-value targets for `unsafe` marking, since marking
    /// them propagates the requirement to the most callers.
    /// </summary>
    public ImmutableArray<UnsafeMethodLeverage> TopUnsafeLeverage(int count = 6)
        => UnsafeLeverage.Top(DirectCalls, _unsafeLeverageMethods, count);

    /// <summary>
    /// The most-leveraged methods in this assembly, ranked by distinct direct
    /// callers. <paramref name="scope"/> optionally restricts which methods are
    /// ranked (for example, members declared on one selected type) while fanin
    /// is still measured across every caller in the assembly.
    /// </summary>
    public ImmutableArray<MethodLeverage> TopLeverage(int count = 25, Func<MethodIdentity, bool>? scope = null)
        => MethodLeverageRanking.Top(DirectCalls, Methods, count, scope);

    /// <summary>
    /// Requires-unsafe methods whose signature carries no pointer — the unsafe
    /// obligation is visible only via the attribute / <c>unsafe</c> modifier,
    /// hidden from a caller reading the parameter and return types.
    /// </summary>
    public ImmutableArray<OpaqueUnsafeMethod> OpaqueUnsafeMethods()
        => OpaqueUnsafe.Collect(Methods);

    /// <summary>
    /// Requires-unsafe methods whose body shows no directly-visible unsafe
    /// operation — an absence claim (never "safe"): a pointer local optimized
    /// away in Release erases the trace of a real dereference.
    /// </summary>
    public ImmutableArray<HollowUnsafeMethod> HollowUnsafeMethods()
        => HollowUnsafe.Collect(Methods, UnsafeEvidence);

    /// <summary>
    /// Builds a bounded outbound (callee) call tree rooted at the method identified by
    /// <paramref name="rootMethodToken"/>. Expansion stays within this assembly: callees that
    /// resolve to another assembly are recorded as <see cref="CallTreeStatus.External"/> leaves.
    /// A method is expanded at most once across the whole tree; later references (shared callees
    /// or cycles) are recorded as <see cref="CallTreeStatus.AlreadyShown"/> leaves. Expansion stops
    /// at <paramref name="maxDepth"/> levels and once <paramref name="maxNodes"/> total nodes exist.
    /// </summary>
    public CallTreeNode BuildCallTree(int rootMethodToken, int maxDepth = 3, int maxNodes = 25)
    {
        var root = Methods.FirstOrDefault(method => method.MetadataToken == rootMethodToken);
        var rootMember = root is { } identity
            ? new MemberRef(identity.DeclaringType, identity.Name, identity.ParameterTypes, identity.ReturnType, MemberKind.Method)
            : MemberRef.Unsupported($"method token 0x{rootMethodToken:X8}");

        var callsByCaller = DirectCalls
            .GroupBy(call => call.Caller.MetadataToken)
            .ToDictionary(group => group.Key, group => group.ToList());

        var methodMap = MethodDefinitionMap.Create(Methods);

        int budget = Math.Max(1, maxNodes);
        int created = 1;
        var expanded = new HashSet<int>();

        int ResolveCallee(DirectCall call)
            => methodMap.Resolve(call);

        var incomingCounts = DirectCalls
            .GroupBy(call => ResolveCallee(call))
            .Where(group => group.Key != 0)
            .ToDictionary(group => group.Key, group => group.Count(), EqualityComparer<int>.Default);

        CallTreeNode Build(MemberRef member, CallKind? kind, int token, int depth, bool inLoop = false)
        {
            var sig = token != 0 ? Signals.GetValueOrDefault(token, MethodSignals.None) : MethodSignals.None;
            if (token == 0 || !callsByCaller.TryGetValue(token, out var edges))
            {
                var leafStatus = token == 0 && depth > 0 ? CallTreeStatus.External : CallTreeStatus.Leaf;
                return new CallTreeNode(member, kind, leafStatus, [], new CallTreePerf(0, incomingCounts.TryGetValue(token, out var incoming) ? incoming : 0, 1, inLoop, inLoop ? "loop" : null, null, sig));
            }

            // True outbound degree (call sites), independent of how far the bounded
            // tree expanded. CallTreeStatus separately conveys why expansion stopped,
            // so depth-limited/already-shown/truncated nodes still report their real
            // fan-out instead of reading like leaves.
            var fanout = edges.Count;
            if (depth >= maxDepth)
                return new CallTreeNode(member, kind, CallTreeStatus.DepthLimited, [], new CallTreePerf(fanout, incomingCounts.TryGetValue(token, out var incomingDepth) ? incomingDepth : 0, 1, inLoop, inLoop ? "loop" : null, null, sig));

            if (!expanded.Add(token))
                return new CallTreeNode(member, kind, CallTreeStatus.AlreadyShown, [], new CallTreePerf(fanout, incomingCounts.TryGetValue(token, out var incomingShown) ? incomingShown : 0, 1, inLoop, inLoop ? "loop" : null, null, sig));

            var children = ImmutableArray.CreateBuilder<CallTreeNode>();
            bool truncated = false;
            foreach (var edge in edges)
            {
                if (created >= budget)
                {
                    truncated = true;
                    break;
                }
                created++;
                children.Add(Build(edge.Callee, edge.Kind, ResolveCallee(edge), depth + 1, edge.InLoop));
            }

            var status = truncated
                ? CallTreeStatus.Truncated
                : children.Count == 0 ? CallTreeStatus.Leaf : CallTreeStatus.Expanded;
            var maxTreeDepth = children.Count == 0 ? 1 : 1 + children.Max(child => child.Perf?.MaxDepth ?? 1);
            var fanin = incomingCounts.TryGetValue(token, out var count) ? count : 0;
            return new CallTreeNode(member, kind, status, children.ToImmutable(), new CallTreePerf(fanout, fanin, maxTreeDepth, inLoop, inLoop ? "loop" : null, null, sig));
        }

        return Build(rootMember, null, rootMethodToken, 0);
    }

    /// <summary>
    /// Builds a bounded reverse (caller) tree rooted at the method identified by
    /// <paramref name="rootMethodToken"/>. Nodes are the immediate callers of the
    /// selected method and their callers transitively, capped by depth and node budget.
    /// </summary>
    public CallTreeNode BuildCallerTree(int rootMethodToken, int maxDepth = 3, int maxNodes = 25)
    {
        var root = Methods.FirstOrDefault(method => method.MetadataToken == rootMethodToken);
        // When the selected method has no body of its own (abstract/interface/extern) it is
        // absent from Methods, but its callers reference it by operand token and carry the
        // resolved callee signature. Recover the root label from any such inbound edge so the
        // graph names the member instead of printing a bare token.
        var rootMember = root is { } identity
            ? new MemberRef(identity.DeclaringType, identity.Name, identity.ParameterTypes, identity.ReturnType, MemberKind.Method)
            : DirectCalls.FirstOrDefault(call => call.CalleeDefinitionToken == rootMethodToken
                && call.Callee.Kind != MemberKind.Unsupported) is { Callee: { } resolvedCallee }
                ? resolvedCallee
                : MemberRef.Unsupported($"method token 0x{rootMethodToken:X8}");

        var methodMap = MethodDefinitionMap.Create(Methods);

        int ResolveCalleeToken(DirectCall call)
        {
            // Direct callvirt/call edges to the selected method reference it by its own
            // MethodDef token (peeled from a MethodSpec for generic-method calls). Accept that
            // even when the selected method has no body of its own (abstract/interface/extern)
            // and so is absent from Methods, so a Caller Graph rooted at a bodiless member still
            // surfaces its real inbound callers.
            if (call.CalleeDefinitionToken == rootMethodToken)
                return rootMethodToken;
            if (methodMap.ContainsToken(call.CalleeDefinitionToken))
                return call.CalleeDefinitionToken;
            return methodMap.Resolve(call);
        }

        // Group inbound call edges by callee, then collapse to one edge per distinct caller
        // method (the section reports callers, not call sites). Preserve the in-loop signal:
        // if any call site from a caller hits the target inside a loop, keep that edge so the
        // loop annotation survives deduplication.
        var reverseEdges = DirectCalls
            .GroupBy(call => ResolveCalleeToken(call))
            .Where(group => group.Key != 0)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(call => call.Caller.MetadataToken)
                    .Select(callerGroup => callerGroup.FirstOrDefault(call => call.InLoop) ?? callerGroup.First())
                    .ToList(),
                EqualityComparer<int>.Default);

        int budget = Math.Max(1, maxNodes);
        int created = 1;
        var expanded = new HashSet<int>();

        CallTreeNode Build(MemberRef member, int token, int depth, bool inLoop)
        {
            // Reverse-graph semantics: the selected member is the target/sink, and the
            // entry points are the far callers — not the tree root. Label accordingly so
            // the target is not mistaken for the source of leverage.
            var classification = depth == 0
                ? "target"
                : member.Name is "Main" or "<Main>$" ? "entrypoint" : null;
            // A caller node's loop flag is an edge property: this caller invokes the node
            // toward the target inside a loop (not "this method is loop-heavy").
            var loopHint = inLoop ? "loop call" : null;
            var sig = token != 0 ? Signals.GetValueOrDefault(token, MethodSignals.None) : MethodSignals.None;
            if (token == 0 || !reverseEdges.TryGetValue(token, out var edges))
            {
                var leafStatus = token == 0 && depth > 0 ? CallTreeStatus.External : CallTreeStatus.Leaf;
                return new CallTreeNode(member, null, leafStatus, [], new CallTreePerf(0, 0, 1, inLoop, loopHint, classification, sig));
            }

            var fanin = edges.Count;
            if (depth >= maxDepth)
                return new CallTreeNode(member, null, CallTreeStatus.DepthLimited, [], new CallTreePerf(0, fanin, 1, inLoop, loopHint, classification, sig));

            if (!expanded.Add(token))
                return new CallTreeNode(member, null, CallTreeStatus.AlreadyShown, [], new CallTreePerf(0, fanin, 1, inLoop, loopHint, classification, sig));

            var children = ImmutableArray.CreateBuilder<CallTreeNode>();
            bool truncated = false;
            foreach (var edge in edges)
            {
                if (created >= budget)
                {
                    truncated = true;
                    break;
                }
                created++;
                var caller = edge.Caller;
                children.Add(Build(
                    new MemberRef(caller.DeclaringType, caller.Name, caller.ParameterTypes, caller.ReturnType, MemberKind.Method),
                    caller.MetadataToken,
                    depth + 1,
                    edge.InLoop));
            }

            var nodeStatus = truncated
                ? CallTreeStatus.Truncated
                : children.Count == 0 ? CallTreeStatus.Leaf : CallTreeStatus.Expanded;
            var maxTreeDepth = children.Count == 0 ? 1 : 1 + children.Max(child => child.Perf?.MaxDepth ?? 1);
            return new CallTreeNode(member, null, nodeStatus, children.ToImmutable(), new CallTreePerf(0, fanin, maxTreeDepth, inLoop, loopHint, classification, sig));
        }

        return Build(rootMember, rootMethodToken, 0, false);
    }

    readonly record struct ReverseCallerEdge(MethodIdentity Caller, string CallerKey, MethodSignals Signals, bool InLoop);

    /// <summary>
    /// Like <see cref="BuildCallerTree(int,int,int)"/>, but extends the bounded reverse graph
    /// across additional caller assemblies (the <c>--bin</c>/<c>--project</c>/
    /// <c>--caller-package</c> scope). The graph is keyed by structural member identity rather
    /// than assembly-local tokens, so a dependency member can surface the product entry points
    /// and callers that reach it. Nodes from an assembly other than the selected member's own
    /// carry their source assembly in <see cref="CallTreePerf.Source"/>. Falls back to the
    /// single-assembly builder when no scopes are supplied.
    /// </summary>
    public CallTreeNode BuildCallerTree(int rootMethodToken, IReadOnlyList<LibraryBodyIndex> callerScopes, int maxDepth = 3, int maxNodes = 25)
    {
        if (callerScopes is not { Count: > 0 })
            return BuildCallerTree(rootMethodToken, maxDepth, maxNodes);

        var rootIdentity = Methods.FirstOrDefault(method => method.MetadataToken == rootMethodToken);
        MemberRef rootMember;
        string rootKey;
        if (rootIdentity is { } identity)
        {
            rootMember = new MemberRef(identity.DeclaringType, identity.Name, identity.ParameterTypes, identity.ReturnType, MemberKind.Method);
            rootKey = CallerGraphKey(identity);
        }
        else
        {
            // Bodiless member (abstract/interface/extern): recover its label and key from any
            // inbound edge in this assembly so the graph still names the target.
            rootMember = DirectCalls.FirstOrDefault(call => call.CalleeDefinitionToken == rootMethodToken
                && call.Callee.Kind != MemberKind.Unsupported) is { Callee: { } resolved }
                ? resolved
                : MemberRef.Unsupported($"method token 0x{rootMethodToken:X8}");
            rootKey = rootMember.Kind != MemberKind.Unsupported
                ? CallerGraphKey(rootMember)
                : "";
        }

        string targetAssembly = rootIdentity?.AssemblyName
            ?? Methods.FirstOrDefault()?.AssemblyName
            ?? "";

        // Structural reverse map across the selected member's assembly plus the caller scopes:
        // callee structural key -> inbound caller edges (each carrying the caller's own-assembly
        // signals captured at index time, since signal tables are assembly-local by token).
        var reverse = new Dictionary<string, List<ReverseCallerEdge>>(StringComparer.Ordinal);
        void IndexAssembly(LibraryBodyIndex assembly)
        {
            var signals = assembly.Signals;
            foreach (var call in assembly.DirectCalls)
            {
                if (call.Callee.Kind == MemberKind.Unsupported)
                    continue;
                var calleeKey = CallerGraphKey(call.Callee);
                var caller = call.Caller;
                var callerKey = CallerGraphKey(caller);
                var edge = new ReverseCallerEdge(caller, callerKey, signals.GetValueOrDefault(caller.MetadataToken, MethodSignals.None), call.InLoop);
                if (reverse.TryGetValue(calleeKey, out var list))
                    list.Add(edge);
                else
                    reverse[calleeKey] = [edge];
            }
        }
        IndexAssembly(this);
        foreach (var scope in callerScopes)
            IndexAssembly(scope);

        int budget = Math.Max(1, maxNodes);
        int created = 1;
        var expanded = new HashSet<string>(StringComparer.Ordinal);

        CallTreeNode Build(MemberRef member, string key, string assembly, MethodSignals sig, int depth, bool inLoop)
        {
            bool external = assembly.Length > 0 && !string.Equals(assembly, targetAssembly, StringComparison.Ordinal);
            string? source = external ? assembly : null;
            string? classification = depth == 0
                ? "target"
                : member.Name is "Main" or "<Main>$" ? "entrypoint" : null;
            string? loopHint = inLoop ? "loop call" : null;

            if (key.Length == 0 || !reverse.TryGetValue(key, out var rawEdges))
            {
                var leafStatus = key.Length == 0 && depth > 0 ? CallTreeStatus.External : CallTreeStatus.Leaf;
                return new CallTreeNode(member, null, leafStatus, [], new CallTreePerf(0, 0, 1, inLoop, loopHint, classification, sig, source));
            }

            // One edge per distinct caller (the graph reports callers, not call sites);
            // preserve an in-loop edge if any call site from that caller hit a loop. Sort by
            // structural key so output is deterministic across assemblies.
            var edges = rawEdges
                .GroupBy(edge => edge.CallerKey, StringComparer.Ordinal)
                .Select(group => group.FirstOrDefault(edge => edge.InLoop, group.First()))
                .OrderBy(edge => edge.CallerKey, StringComparer.Ordinal)
                .ToList();

            var fanin = edges.Count;
            if (depth >= maxDepth)
                return new CallTreeNode(member, null, CallTreeStatus.DepthLimited, [], new CallTreePerf(0, fanin, 1, inLoop, loopHint, classification, sig, source));
            if (!expanded.Add(key))
                return new CallTreeNode(member, null, CallTreeStatus.AlreadyShown, [], new CallTreePerf(0, fanin, 1, inLoop, loopHint, classification, sig, source));

            var children = ImmutableArray.CreateBuilder<CallTreeNode>();
            bool truncated = false;
            foreach (var edge in edges)
            {
                if (created >= budget)
                {
                    truncated = true;
                    break;
                }
                created++;
                var caller = edge.Caller;
                children.Add(Build(
                    new MemberRef(caller.DeclaringType, caller.Name, caller.ParameterTypes, caller.ReturnType, MemberKind.Method),
                    edge.CallerKey,
                    caller.AssemblyName,
                    edge.Signals,
                    depth + 1,
                    edge.InLoop));
            }

            var nodeStatus = truncated
                ? CallTreeStatus.Truncated
                : children.Count == 0 ? CallTreeStatus.Leaf : CallTreeStatus.Expanded;
            var maxTreeDepth = children.Count == 0 ? 1 : 1 + children.Max(child => child.Perf?.MaxDepth ?? 1);
            return new CallTreeNode(member, null, nodeStatus, children.ToImmutable(), new CallTreePerf(0, fanin, maxTreeDepth, inLoop, loopHint, classification, sig, source));
        }

        var rootSignals = rootIdentity is { } rootId ? Signals.GetValueOrDefault(rootId.MetadataToken, MethodSignals.None) : MethodSignals.None;
        return Build(rootMember, rootKey, targetAssembly, rootSignals, 0, false);
    }

    // Cross-assembly caller-graph identity key. The multi-assembly reverse map matches members
    // structurally (tokens are assembly-local), so the key leads with the declaring type's
    // assembly and adds parameter arity and the return type to the qualified declaring type,
    // name, and parameters. The assembly prefix is required so that same-namespace/type/member
    // members in different assemblies do not match the selected target (callee side) and so that
    // identically-signed callers from different caller scopes are not collapsed into one node
    // (caller side) (#1579).
    //
    // Generic members are normalized to their open declaring definition + name + parameter arity
    // (#1339): a constructed call site (e.g. List<int>.Add / Id<int>) keys identically to its
    // open-definition target (List<T>.Add / Id<T>) instead of on the instantiation, so generic
    // dependency members surface their external callers. Both sides compute the same erased
    // identity from the data they carry — the open definition (MethodIdentity, whose signature
    // still mentions generic parameters) and the constructed call site (MemberRef, whose declaring
    // type is a GenericInstance or which carries method type arguments). See GenericMemberIdentity.
    //
    // Remaining limitation: type forwarding beyond the corelib facade canonicalization
    // (TypeRef.CanonicalAssembly): a type physically defined in assembly B but referenced through a
    // [TypeForwardedTo] facade A keys as B on the definition side and A at the referencing call
    // site, so such a forwarded member may not link its callers. This is an honest missed edge,
    // preferred over the prior false positive of attributing unrelated same-FQN callers.
    static string CallerGraphKey(MethodIdentity member)
        => CallerGraphKey(
            member.DeclaringType,
            member.Name,
            member.ParameterTypes,
            member.ParameterTypes,
            member.ReturnType,
            GenericMemberIdentity.ShouldErase(member.DeclaringType, member.ParameterTypes, member.ReturnType, []));

    static string CallerGraphKey(MemberRef member)
        => CallerGraphKey(
            member.DeclaringType,
            member.Name,
            member.ParameterTypes,
            member.OpenSignatureParameters,
            member.OpenSignatureReturn,
            GenericMemberIdentity.ShouldErase(member.DeclaringType, member.ParameterTypes, member.ReturnType, member.TypeArguments));

    static string CallerGraphKey(TypeRef declaringType, string name, ImmutableArray<TypeRef> parameterTypes, ImmutableArray<TypeRef> openParameterTypes, TypeRef openReturnType, bool eraseGenericSignature)
    {
        var openDeclaring = GenericMemberIdentity.OpenDeclaringType(declaringType);
        return eraseGenericSignature
            ? $"{GenericMemberIdentity.KeyFragment(openDeclaring)}|{name}|{parameterTypes.Length}|{GenericMemberIdentity.ErasedParameterShape(openParameterTypes)}|{GenericMemberIdentity.KeyFragment(openReturnType)}"
            : $"{GenericMemberIdentity.KeyFragment(openDeclaring)}|{name}|{parameterTypes.Length}|{string.Join(",", parameterTypes.Select(GenericMemberIdentity.KeyFragment))}|{GenericMemberIdentity.KeyFragment(openReturnType)}";
    }

    sealed class IndexBuilder
    {
        const ILOpCode NoPrefix = (ILOpCode)0xFE19;

        readonly string _path;
        readonly MetadataReader _reader;
        readonly PEReader _peReader;
        readonly string _assemblyName;
        readonly Guid _mvid;
        readonly bool _memorySafetyRulesEnabled;

        public IndexBuilder(string path, MetadataReader reader, PEReader peReader)
        {
            _path = path;
            _reader = reader;
            _peReader = peReader;
            _assemblyName = reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : System.IO.Path.GetFileNameWithoutExtension(path);
            _mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
            _memorySafetyRulesEnabled = DetectMemorySafetyRules();
        }

        // Roslyn's ModuleSymbol.UseUpdatedMemorySafetyRules: the module opted in
        // when MemorySafetyRulesAttribute is applied (emitted [module:], like
        // RefSafetyRulesAttribute). Check the module and assembly scopes.
        public bool MemorySafetyRulesEnabled => _memorySafetyRulesEnabled;

        bool DetectMemorySafetyRules()
        {
            const string ns = "System.Runtime.CompilerServices";
            if (HasAttributeNamed(_reader.GetModuleDefinition().GetCustomAttributes(), "MemorySafetyRulesAttribute", ns))
                return true;
            return _reader.IsAssembly
                && HasAttributeNamed(_reader.GetAssemblyDefinition().GetCustomAttributes(), "MemorySafetyRulesAttribute", ns);
        }

        public (ImmutableArray<MethodIdentity> Methods, ImmutableArray<DirectCall> DirectCalls, ImmutableArray<UnsafeEvidence> UnsafeEvidence, ImmutableArray<AnalysisDiagnostic> Diagnostics, ImmutableArray<OptimizationOpportunity> OptimizationOpportunities, ImmutableArray<MethodIdentity> UnsafeLeverageMethods, UnsafeModeBreakdown UnsafeModes, IReadOnlyDictionary<int, BodySignals> BodySignals, IReadOnlyDictionary<(string Namespace, string Name), bool> InAssemblyTypeIsException, IReadOnlySet<int> SuppressedOpportunityTokens, IReadOnlySet<string> ExceptionTypeNames, IReadOnlySet<int> NonHeapNewObjOperandTokens) Build()
        {
            var methods = ImmutableArray.CreateBuilder<MethodIdentity>();
            var unsafeLeverageMethods = ImmutableArray.CreateBuilder<MethodIdentity>();
            var calls = ImmutableArray.CreateBuilder<DirectCall>();
            var unsafeEvidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();
            var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
            var optimizationOpportunities = ImmutableArray.CreateBuilder<OptimizationOpportunity>();
            var bodySignals = new Dictionary<int, BodySignals>();
            var suppressedOpportunityTokens = new HashSet<int>();
            var exceptionTypeNames = ComputeExceptionTypeNames();
            int none = 0, impl = 0, expl = 0;

            foreach (var typeHandle in _reader.TypeDefinitions)
            {
                var typeDef = _reader.GetTypeDefinition(typeHandle);
                // Source-generated types (JSON/regex/etc. carry [GeneratedCode]) are not
                // actionable source-shape opportunities, so skip optimization-opportunity
                // collection for them (they are still indexed for calls/leverage/signals).
                bool typeSourceGenerated = HasGeneratedCodeAttribute(typeDef.GetCustomAttributes());
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    try
                    {
                        var methodDef = _reader.GetMethodDefinition(methodHandle);
                        var scope = CreateScope(typeDef, methodDef);
                        var caller = CreateMethodIdentity(typeHandle, methodHandle, methodDef, scope);
                        // Tally the unsafe mode for every method, including bodiless
                        // extern/abstract members (P/Invokes are a major source).
                        switch (caller.CallerUnsafeMode)
                        {
                            case CallerUnsafeMode.Explicit: expl++; break;
                            case CallerUnsafeMode.Implicit: impl++; break;
                            default: none++; break;
                        }
                        bool hasUnsafeApiMember = AddUnsafeApiMemberEvidence(caller, unsafeEvidence);
                        bool hasUnsafeSignature = AddUnsafeSignatureEvidence(caller, unsafeEvidence);
                        if (caller.CallerUnsafeMode != CallerUnsafeMode.None || hasUnsafeApiMember)
                            unsafeLeverageMethods.Add(caller);
                        if (methodDef.RelativeVirtualAddress == 0)
                            continue;

                        methods.Add(caller);
                        var body = _peReader.GetMethodBody(methodDef.RelativeVirtualAddress);
                        var il = body.GetILBytes() ?? [];
                        bool hasUnsafeLocals = ScanLocals(body, caller, scope, unsafeEvidence);
                        var loopRegions = CollectLoopRegions(il);
                        var methodAttributes = methodDef.GetCustomAttributes();
                        if (!typeSourceGenerated
                            && !HasGeneratedCodeAttribute(methodAttributes)
                            && !HasCompilerGeneratedAttribute(methodAttributes)
                            && !IsBlazorRenderMethod(caller))
                            optimizationOpportunities.AddRange(CollectOptimizationOpportunities(il, caller, scope, loopRegions));
                        else
                            suppressedOpportunityTokens.Add(caller.MetadataToken);
                        var signals = CollectBodySignals(il, body, scope, loopRegions);
                        if (signals.Newarr > 0 || signals.Throws > 0 || signals.Catches > 0 || signals.Finallys > 0 || signals.Boxes > 0)
                            bodySignals[caller.MetadataToken] = signals;
                        ScanBody(il, caller, scope, calls, unsafeEvidence,
                            includeIndirectOpcodes: hasUnsafeApiMember || hasUnsafeSignature || hasUnsafeLocals,
                            loopRegions);
                    }
                    catch (Exception ex) when (IsRecoverableMethodFailure(ex))
                    {
                        diagnostics.Add(new AnalysisDiagnostic(
                            MetadataTokens.GetToken(methodHandle),
                            MethodLabel(typeHandle, methodHandle),
                            $"{ex.GetType().Name}: {ex.Message}"));
                    }
                }
            }

            var directCalls = calls.ToImmutable();
            var nonHeapNewObjOperandTokens = ComputeNonHeapNewObjOperandTokens(directCalls);
            return (methods.ToImmutable(), directCalls, unsafeEvidence.ToImmutable(), diagnostics.ToImmutable(),
                optimizationOpportunities.ToImmutable(), unsafeLeverageMethods.ToImmutable(), new UnsafeModeBreakdown(none, impl, expl), bodySignals,
                BuildInAssemblyExceptionMap(), suppressedOpportunityTokens, exceptionTypeNames, nonHeapNewObjOperandTokens);
        }

        // The metadata operand tokens of `newobj` instructions that construct a VALUE TYPE
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
                if (IsNonHeapNewObj(call.OperandToken, call.Callee.DeclaringType))
                    set.Add(call.OperandToken);
            }
            return set;
        }

        // True when a `newobj` of this operand constructs a value type. Combines a name-based
        // FRAMEWORK fast path with an authoritative metadata resolution of the constructor's
        // declaring type (TypeDef base chain, or TypeSpec signature blob for constructed
        // generics) — the latter is what classifies in-assembly and cross-assembly structs.
        bool IsNonHeapNewObj(int operandToken, TypeRef declaringType)
        {
            if (IsNonHeapConstructionByName(declaringType))
                return true;
            try
            {
                var handle = MetadataTokens.EntityHandle(operandToken);
                EntityHandle typeHandle = handle.Kind switch
                {
                    HandleKind.MethodDefinition => _reader.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType(),
                    HandleKind.MemberReference => _reader.GetMemberReference((MemberReferenceHandle)handle).Parent,
                    _ => default,
                };
                if (typeHandle.Kind == HandleKind.TypeDefinition)
                    return IsValueTypeDefinition((TypeDefinitionHandle)typeHandle);
                if (typeHandle.Kind == HandleKind.TypeSpecification)
                    return IsValueTypeSpec((TypeSpecificationHandle)typeHandle);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
            {
                return false;
            }
            return false;
        }

        // Name-based recognition of FRAMEWORK value types whose `newobj` resolves to a bare
        // TypeRef the token dispatch cannot follow (a non-generic framework struct like DateTime
        // or Guid lives in an assembly this one does not load). The common generic framework
        // value types (Span/ReadOnlySpan/Memory/Nullable/ValueTuple`n) are constructed through a
        // TypeSpec and are resolved authoritatively by the signature blob, so they are listed
        // here only as a fast path. In-assembly and cross-assembly value types are NOT matched by
        // name — that is the operand-token metadata path's job — because a display name omits
        // assembly identity and would misclassify an external reference type that shares a
        // namespace+name with an in-assembly struct (#1804 review).
        static bool IsNonHeapConstructionByName(TypeRef type)
        {
            var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
            if (definition.Kind != TypeRefKind.Definition || !definition.TrustedFrameworkAssembly)
                return false;
            if (definition.Namespace == "System" && definition.Name is
                    "Span`1" or "ReadOnlySpan`1" or "Memory`1" or "ReadOnlyMemory`1" or "Nullable`1"
                    or "ValueTuple" or "ValueTuple`1" or "ValueTuple`2" or "ValueTuple`3" or "ValueTuple`4"
                    or "ValueTuple`5" or "ValueTuple`6" or "ValueTuple`7" or "ValueTuple`8")
                return true;
            return IsWellKnownValueType(definition.Namespace, definition.Name);
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

        MethodIdentity CreateMethodIdentity(TypeDefinitionHandle typeHandle, MethodDefinitionHandle methodHandle, MethodDefinition methodDef, GenericScope scope)
        {
            var declaringType = TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, typeHandle, 0);
            var signature = methodDef.DecodeSignature(TypeRefDecoder.Instance, scope);
            return new MethodIdentity(
                _assemblyName,
                _mvid,
                declaringType,
                _reader.GetString(methodDef.Name),
                signature.ParameterTypes,
                signature.ReturnType,
                MetadataTokens.GetToken(methodHandle),
                (methodDef.Attributes & MethodAttributes.Static) != 0,
                ComputeCallerUnsafeMode(typeHandle, methodDef, signature.ParameterTypes, signature.ReturnType));
        }

        // Mirrors Roslyn's PEMethodSymbol.CallerUnsafeMode: a member "requires
        // unsafe" when it carries RequiresUnsafeAttribute (the metadata form of
        // the `unsafe` modifier) or has a pointer/function pointer in its
        // signature; the mode is then gated on the module opting into the rules.
        CallerUnsafeMode ComputeCallerUnsafeMode(
            TypeDefinitionHandle typeHandle, MethodDefinition methodDef,
            ImmutableArray<TypeRef> parameterTypes, TypeRef returnType)
        {
            bool requiresUnsafe =
                HasRequiresUnsafe(methodDef.GetCustomAttributes())
                || HasRequiresUnsafe(_reader.GetTypeDefinition(typeHandle).GetCustomAttributes())
                || parameterTypes.Any(type => type.ContainsPointer())
                || returnType.ContainsPointer();

            if (!requiresUnsafe)
                return CallerUnsafeMode.None;
            return _memorySafetyRulesEnabled ? CallerUnsafeMode.Explicit : CallerUnsafeMode.Implicit;
        }

        // Read attributes straight from SRM — a simple has-attribute check needs
        // no shared decode/render machinery, so Analysis stays independent.
        bool HasRequiresUnsafe(CustomAttributeHandleCollection attributes)
            // Match the distinctive simple name: the implemented attribute is in
            // System.Diagnostics.CodeAnalysis, while the design doc says
            // System.Runtime.CompilerServices — tolerate the namespace churn.
            => HasAttributeNamed(attributes, "RequiresUnsafeAttribute",
                "System.Diagnostics.CodeAnalysis", "System.Runtime.CompilerServices");

        bool HasAttributeNamed(CustomAttributeHandleCollection attributes, string simpleName, params string[] namespaces)
        {
            foreach (var handle in attributes)
            {
                var (ns, name) = AttributeTypeName(_reader.GetCustomAttribute(handle).Constructor);
                if (name == simpleName && (namespaces.Length == 0 || Array.IndexOf(namespaces, ns) >= 0))
                    return true;
            }
            return false;
        }

        // True when the member/type is marked [System.CodeDom.Compiler.GeneratedCode] —
        // the universal source-generator signal (System.Text.Json, regex, etc.). Such code
        // has ordinary names (so the compiler-generated name heuristics miss it) but is not
        // an actionable source-shape optimization target.
        bool HasGeneratedCodeAttribute(CustomAttributeHandleCollection attributes)
            => HasAttributeNamed(attributes, "GeneratedCodeAttribute", "System.CodeDom.Compiler");

        // True when the method is marked [System.Runtime.CompilerServices.CompilerGenerated]
        // — record synthesized members (EqualityContract/PrintMembers/Equals/GetHashCode/
        // ToString), lambdas, iterators, and async state machines. These have ordinary names
        // (e.g. get_EqualityContract) that the angle-bracket name heuristics miss, yet none
        // are user-actionable source-shape rewrite targets, so exclude them from collection.
        bool HasCompilerGeneratedAttribute(CustomAttributeHandleCollection attributes)
            => HasAttributeNamed(attributes, "CompilerGeneratedAttribute", "System.Runtime.CompilerServices");

        // True when the method is Razor/Blazor-generated render plumbing: any method that
        // takes a Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder parameter
        // (the component BuildRenderTree override and the Create*_N render-fragment helpers
        // the Razor compiler emits). These are emitted from .razor markup, carry no
        // [GeneratedCode]/[CompilerGenerated] attribute, and their allocations (event-handler
        // delegates, EventCallback boxing, RenderFragment closures) are intrinsic to the
        // component model — not user-actionable source-shape fixes. Hand-written code
        // essentially never takes a RenderTreeBuilder, so the parameter is a precise signal.
        //
        // The match is trust-gated (public-key-token, #1708): only the real framework
        // RenderTreeBuilder counts, so a user-defined type that merely reuses the namespace and
        // name does not silently suppress that method's genuine allocation findings.
        static bool IsBlazorRenderMethod(MethodIdentity caller)
        {
            foreach (var parameter in caller.ParameterTypes)
            {
                if (FrameworkIdentity.IsKnownFrameworkType(
                        parameter,
                        "Microsoft.AspNetCore.Components",
                        "Microsoft.AspNetCore.Components.Rendering",
                        "RenderTreeBuilder"))
                    return true;
            }
            return false;
        }

        (string Namespace, string Name) AttributeTypeName(EntityHandle constructor)
        {
            if (constructor.Kind == HandleKind.MemberReference
                && _reader.GetMemberReference((MemberReferenceHandle)constructor).Parent is { Kind: HandleKind.TypeReference } parent)
            {
                var typeRef = _reader.GetTypeReference((TypeReferenceHandle)parent);
                return (_reader.GetString(typeRef.Namespace), _reader.GetString(typeRef.Name));
            }
            if (constructor.Kind == HandleKind.MethodDefinition)
            {
                var declType = _reader.GetTypeDefinition(_reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType());
                return (_reader.GetString(declType.Namespace), _reader.GetString(declType.Name));
            }
            return ("", "");
        }

        bool AddUnsafeApiMemberEvidence(MethodIdentity method, ImmutableArray<UnsafeEvidence>.Builder unsafeEvidence)
        {
            if (!IsUnsafeApi(method.DeclaringType))
                return false;

            unsafeEvidence.Add(new UnsafeEvidence(
                method,
                "Unsafe API member",
                FormatMethod(method),
                "api",
                ILOffset: null,
                OperandToken: null));
            return true;
        }

        bool AddUnsafeSignatureEvidence(MethodIdentity method, ImmutableArray<UnsafeEvidence>.Builder unsafeEvidence)
        {
            var unsafeTypes = method.ParameterTypes
                .Append(method.ReturnType)
                .Where(ContainsUnsafeType)
                .Select(t => t.ToQualifiedDisplayString())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (unsafeTypes.Count == 0)
                return false;

            unsafeEvidence.Add(new UnsafeEvidence(
                method,
                "Unsafe signature",
                string.Join(", ", unsafeTypes),
                "signature",
                ILOffset: null,
                OperandToken: null));
            return true;
        }

        bool ScanLocals(MethodBodyBlock body, MethodIdentity member, GenericScope scope, ImmutableArray<UnsafeEvidence>.Builder unsafeEvidence)
        {
            if (body.LocalSignature.IsNil)
                return false;

            bool found = false;
            var signature = _reader.GetStandaloneSignature(body.LocalSignature);
            var locals = signature.DecodeLocalSignature(TypeRefDecoder.Instance, scope);
            for (int i = 0; i < locals.Length; i++)
            {
                var local = locals[i];
                if (local.Kind == TypeRefKind.Pinned)
                {
                    unsafeEvidence.Add(new UnsafeEvidence(member, "Pinned local", $"V_{i}: {local.ToQualifiedDisplayString()}", "local", null, null));
                    found = true;
                    continue;
                }
                if (ContainsUnsafeType(local))
                {
                    unsafeEvidence.Add(new UnsafeEvidence(member, "Pointer local", $"V_{i}: {local.ToQualifiedDisplayString()}", "local", null, null));
                    found = true;
                }
            }
            return found;
        }

        ImmutableArray<OptimizationOpportunity> CollectOptimizationOpportunities(byte[] il, MethodIdentity caller, GenericScope callerScope, IReadOnlyList<(int Start, int End)> loopRegions)
        {
            var opportunities = ImmutableArray.CreateBuilder<OptimizationOpportunity>();
            int? pendingConstant = null;
            // Delegate creation is `<push target>; ldftn/ldvirtftn M; newobj DelegateCtor`.
            // Track the pending function-pointer load so a single row is emitted at the
            // newobj (one row per delegate allocation), classified by the target.
            int? pendingDelegateOffset = null;
            bool pendingDelegateCapturing = false;
            bool pendingDelegateInstanceGroup = false;
            // The opcode that loaded the delegate receiver (the instruction before ldftn).
            // A static method group loads `ldnull`; a real instance receiver is anything else.
            ILOpCode previousOpcode = default;
            // A `box` of a concrete value type is deferred until the next instruction so we can
            // see whether the boxed value escapes (into a ref array, a call, a field, or a
            // return) rather than being consumed locally (unbox round-trip / type test).
            int? pendingBoxOffset = null;
            TypeRef? pendingBoxType = null;
            bool pendingBoxInLoop = false;
            // Index (into opportunities) of a just-emitted delegate row awaiting its consumer:
            // if the delegate flows straight into a lazy LINQ operator, the obvious iterator
            // rewrite only moves the allocation, so we annotate that on the row.
            int? pendingDelegateOpportunityIndex = null;
            int position = 0;
            while (position < il.Length)
            {
                int offset = position;
                var opcode = ReadOpcode(il, ref position);
                switch (opcode)
                {
                    case ILOpCode.Ldc_i4_m1:
                    case ILOpCode.Ldc_i4_0:
                    case ILOpCode.Ldc_i4_1:
                    case ILOpCode.Ldc_i4_2:
                    case ILOpCode.Ldc_i4_3:
                    case ILOpCode.Ldc_i4_4:
                    case ILOpCode.Ldc_i4_5:
                    case ILOpCode.Ldc_i4_6:
                    case ILOpCode.Ldc_i4_7:
                    case ILOpCode.Ldc_i4_8:
                        pendingConstant = opcode switch
                        {
                            ILOpCode.Ldc_i4_m1 => -1,
                            ILOpCode.Ldc_i4_0 => 0,
                            ILOpCode.Ldc_i4_1 => 1,
                            ILOpCode.Ldc_i4_2 => 2,
                            ILOpCode.Ldc_i4_3 => 3,
                            ILOpCode.Ldc_i4_4 => 4,
                            ILOpCode.Ldc_i4_5 => 5,
                            ILOpCode.Ldc_i4_6 => 6,
                            ILOpCode.Ldc_i4_7 => 7,
                            _ => 8,
                        };
                        break;
                    case ILOpCode.Ldc_i4_s:
                        pendingConstant = (sbyte)ReadByte(il, ref position, offset);
                        break;
                    case ILOpCode.Ldc_i4:
                        pendingConstant = ReadInt32(il, ref position, offset);
                        break;
                    case ILOpCode.Newarr:
                    {
                        int elementToken = ReadInt32(il, ref position, offset);
                        if (pendingConstant is int length && length >= 0 && length <= 8)
                        {
                            // Promote to a confident stackalloc recommendation only when the
                            // array provably stays local AND its element type is stackalloc-
                            // eligible (an unmanaged primitive); otherwise keep the
                            // non-committal shape.
                            bool local = ArrayProvablyStaysLocal(il, position, caller)
                                && IsStackallocEligibleElement(ResolveTypeToken(elementToken, callerScope));
                            opportunities.Add(local
                                ? new OptimizationOpportunity(
                                    caller,
                                    "stackalloc-candidate",
                                    $"newarr with small constant length ({length}) that does not escape",
                                    "The array stays local, so a stackalloc span avoids the heap allocation.",
                                    "high",
                                    IsInLoopRegion(offset, loopRegions),
                                    offset,
                                    null)
                                : new OptimizationOpportunity(
                                    caller,
                                    "small-array",
                                    $"newarr with small constant length ({length})",
                                    "If the array does not escape, a span or stackalloc may avoid the allocation.",
                                    "medium",
                                    IsInLoopRegion(offset, loopRegions),
                                    offset,
                                    "Escape not analyzed; confirm the array stays local before replacing."));
                        }
                        pendingConstant = null;
                        break;
                    }
                    case ILOpCode.Newobj:
                    {
                        pendingConstant = null;
                        ReadInt32(il, ref position, offset);
                        if (pendingDelegateOffset is not null)
                        {
                            // A function pointer was just loaded, so this newobj is the delegate
                            // allocation. Two cases allocate a delegate per call and are worth
                            // reporting: a closure (captures locals/receiver) and an instance
                            // method group (binds the receiver). Non-capturing lambdas and static
                            // method groups are compiler-cached, so they are not reported.
                            if (pendingDelegateCapturing)
                            {
                                // Confidence tracks loop membership: an in-loop delegate is a
                                // repeated allocation (high); a one-shot delegate in a cold
                                // method is low-value, especially since .NET 10+ partially
                                // stack-allocates non-escaping ones (low).
                                var inLoop = IsInLoopRegion(offset, loopRegions);
                                pendingDelegateOpportunityIndex = opportunities.Count;
                                opportunities.Add(new OptimizationOpportunity(
                                    caller,
                                    "capturing-delegate",
                                    "delegate over a captured receiver or closure",
                                    "Each call allocates a closure delegate; a static local function with explicit state parameters avoids it.",
                                    inLoop ? "high" : "low",
                                    inLoop,
                                    offset,
                                    "On .NET 10+ the JIT can partially stack-allocate a non-escaping closure (~88 to ~36 bytes/call measured), reducing but not eliminating it; it stays a full heap allocation when the closure escapes the method — stored, returned, or passed to a callee that lets it escape."));
                            }
                            else if (pendingDelegateInstanceGroup)
                            {
                                var inLoop = IsInLoopRegion(offset, loopRegions);
                                pendingDelegateOpportunityIndex = opportunities.Count;
                                opportunities.Add(new OptimizationOpportunity(
                                    caller,
                                    "instance-method-group-delegate",
                                    "delegate over an instance method group (binds the receiver)",
                                    "Each call allocates a delegate that binds the receiver; cache it in a field when the receiver is stable, or use a static method with explicit state.",
                                    inLoop ? "high" : "low",
                                    inLoop,
                                    offset,
                                    "On .NET 10+ the JIT can partially stack-allocate a non-escaping delegate (~88 to ~36 bytes/call measured), reducing but not eliminating it; it stays a full heap allocation when it escapes the method — stored, returned, or passed to a callee that lets it escape."));
                            }
                            pendingDelegateOffset = null;
                        }
                        break;
                    }
                    case ILOpCode.Call:
                    case ILOpCode.Callvirt:
                    {
                        pendingConstant = null;
                        int token = ReadInt32(il, ref position, offset);
                        var callee = MemberResolver.ResolveMethod(_reader, MetadataTokens.EntityHandle(token), callerScope);
                        // When the delegate just allocated flows straight into a lazy LINQ
                        // operator (Where/Select/…), a static-local-function rewrite removes the
                        // closure but the LINQ call still allocates a deferred-query iterator per
                        // call — the allocation is reduced, not eliminated. Annotate the surfaced
                        // fix so a cleared closure shape is not read as a free win. (Eager
                        // membership terminals — Any/Count/… — allocate no iterator and are
                        // handled by the linq-scan-in-loop shape, so they are not annotated here.)
                        if (pendingDelegateOpportunityIndex is { } moveIndex
                            && LibraryBodyIndex.IsLinqLazyProducer(callee, out _))
                        {
                            var row = opportunities[moveIndex];
                            opportunities[moveIndex] = row with
                            {
                                SafeFixDirection = "Consumed by a lazy LINQ operator (Where/Select/…): a static local function removes this closure, but the LINQ call still allocates a deferred-query iterator per call — reduced, not eliminated. Replace the query with an explicit loop (or a precomputed index when used for lookups) to remove both.",
                                Caveat = "A delegate-only rewrite does not remove the allocation; the lazy LINQ call still allocates an iterator.",
                            };
                        }
                        if (IsBitConverterGetBytes(callee))
                        {
                            opportunities.Add(new OptimizationOpportunity(
                                caller,
                                "temporary-byte-array-copy",
                                $"{callee.DeclaringType.ToQualifiedDisplayString()}::{callee.Name}",
                                "Prefer BinaryPrimitives.Write* or a stackalloc span when byte order is known.",
                                "high",
                                IsInLoopRegion(offset, loopRegions),
                                offset,
                                null));
                        }
                        else if (IsSpanToArrayCopy(callee, out var copyReceiver))
                        {
                            opportunities.Add(new OptimizationOpportunity(
                                caller,
                                "span-to-array-copy",
                                copyReceiver,
                                "Let the span flow through to the consumer instead of materializing a copy when the array is not retained.",
                                "medium",
                                IsInLoopRegion(offset, loopRegions),
                                offset,
                                "The copy is required if the array escapes (returned, stored, or passed to an array-typed API)."));
                        }
                        else if (IsLinqMembershipScan(callee, out var scanOp) && IsInLoopRegion(offset, loopRegions))
                        {
                            // A membership/search LINQ terminal (Any, First, Count, Contains, …)
                            // that runs inside a loop re-scans its sequence on every iteration.
                            // If the scanned sequence scales with the loop this is O(n*m) — the
                            // canonical fix is to build a set/dictionary index once outside the loop.
                            opportunities.Add(new OptimizationOpportunity(
                                caller,
                                "linq-scan-in-loop",
                                $"Enumerable.{scanOp}(...) inside a loop",
                                "Linear LINQ scan per iteration; precompute a set/dictionary index (or hoist the result) once outside the loop.",
                                "medium",
                                true,
                                offset,
                                "Quadratic only if the scanned sequence grows with the loop; a small or constant sequence is fine."));
                        }
                        else if (IsStringConcat(callee) && IsInLoopRegion(offset, loopRegions)
                            && ConcatAccumulatesIntoSource(il, offset, position, callee.ParameterTypes.Length, callerScope))
                        {
                            // `s += …` inside a loop lowers to String.Concat(s, …) stored back to
                            // the same local/parameter. Each iteration copies the whole growing
                            // accumulator, so the loop is O(n^2) in the final length — the
                            // canonical StringBuilder fix. Only this self-accumulation shape is
                            // reported: a non-accumulating String.Concat/Format/Join in a loop
                            // (e.g. `list.Add($"{k}={v}")`, `return $"{a}-{b}"`) allocates one
                            // transient per iteration with no StringBuilder rewrite, so it is not
                            // flagged — that tier was measured to be essentially all false
                            // positives on real assemblies.
                            opportunities.Add(new OptimizationOpportunity(
                                caller,
                                "string-build-in-loop",
                                "string += in a loop (String.Concat onto a growing accumulator)",
                                "Repeated concatenation copies the whole accumulator each iteration (O(n^2)); build with a StringBuilder hoisted outside the loop and ToString() once after.",
                                "high",
                                true,
                                offset,
                                null));
                        }
                        else if (IsInterfaceEnumeratorAllocation(callee) && IsInLoopRegion(offset, loopRegions))
                        {
                            // foreach over an interface (IEnumerable/IEnumerable<T>) binds to a
                            // GetEnumerator returning the reference-type IEnumerator/IEnumerator<T>,
                            // whose implementation is a heap object — one allocation per foreach.
                            // foreach over a concrete type uses a struct enumerator and allocates
                            // nothing. Only the in-loop case is reported: a foreach inside a loop
                            // re-allocates the enumerator each outer iteration. A one-shot foreach
                            // allocates once and was measured to be essentially all noise.
                            opportunities.Add(new OptimizationOpportunity(
                                caller,
                                "enumerator-allocation",
                                $"foreach over an interface allocates a reference-type enumerator ({callee.ReturnType.ToQualifiedDisplayString()})",
                                "Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List<T>) uses a struct enumerator, or index/iterate it once outside the loop.",
                                "medium",
                                true,
                                offset,
                                "No allocation when the static type has a struct enumerator; worthwhile only if the concrete type is reachable at this call site."));
                        }
                        break;
                    }
                    case ILOpCode.Ldftn:
                    case ILOpCode.Ldvirtftn:
                    {
                        pendingConstant = null;
                        int token = ReadInt32(il, ref position, offset);
                        // Defer emission to the following newobj (de-dup). Capture is decided
                        // by the target method's declaring type: a lambda that closes over state
                        // is emitted on a compiler-generated display class. An instance method
                        // group binds a runtime receiver (never cached), so it allocates per call
                        // too; we recognize it as a target on an ordinary type (nested
                        // compiler-generated names contain "<>") whose receiver is a real
                        // instance (the preceding load is not `ldnull`). Non-capturing lambdas
                        // (`<>c` cache) and static method groups (`ldnull` receiver) are
                        // compiler-cached and not reported.
                        var ftnTarget = MemberResolver.ResolveMethod(_reader, MetadataTokens.EntityHandle(token), callerScope);
                        pendingDelegateOffset = offset;
                        pendingDelegateCapturing = IsClosureTarget(ftnTarget);
                        pendingDelegateInstanceGroup = !pendingDelegateCapturing
                            && ftnTarget.Kind != MemberKind.Unsupported
                            && !DeclaringTypeLeafName(ftnTarget.DeclaringType).Contains("<>", StringComparison.Ordinal)
                            && previousOpcode != ILOpCode.Ldnull;
                        break;
                    }
                    case ILOpCode.Ldarg_0:
                        pendingConstant = null;
                        break;
                    case ILOpCode.Ldarg:
                        pendingConstant = null;
                        ReadInt16(il, ref position, offset);
                        break;
                    case ILOpCode.Ldarg_s:
                        pendingConstant = null;
                        ReadByte(il, ref position, offset);
                        break;
                    case ILOpCode.Ldfld:
                    case ILOpCode.Ldflda:
                    case ILOpCode.Stfld:
                        pendingConstant = null;
                        ReadInt32(il, ref position, offset);
                        break;
                    case ILOpCode.Box:
                    {
                        pendingConstant = null;
                        int token = ReadInt32(il, ref position, offset);
                        var boxed = ResolveTypeToken(token, callerScope);
                        // ECMA-335 permits `box` on reference types (a no-op) and generic
                        // parameters (compiler-mandated, JIT-specialized), and `box Nullable<T>`
                        // allocates only when non-null. Flag only a positively-identified,
                        // unconditionally-allocating value type. Escape is decided at the
                        // consumer below.
                        var allocating = IsAllocatingValueTypeBox(token, boxed);
                        // A box that flows into a throw within a few instructions is an
                        // error-path allocation (an exception message: `throw new
                        // ArgumentException($"bad {x}")` lowers to box; Format; newobj; throw).
                        // It executes at most once before unwinding, not in steady state, so it
                        // is not pay-dirt — suppress it entirely (mirrors excluding exception
                        // construction from allocation density), not merely demote it off the
                        // hot-loop bit.
                        var feedsThrow = allocating && BoxFeedsThrowSoon(il, position);
                        pendingBoxOffset = allocating && !feedsThrow ? offset : null;
                        pendingBoxType = allocating && !feedsThrow ? boxed : null;
                        pendingBoxInLoop = pendingBoxOffset is not null
                            && IsInLoopRegion(offset, loopRegions);
                        break;
                    }
                    default:
                        pendingConstant = null;
                        SkipOperand(il, opcode, ref position, offset);
                        break;
                }

                // A bare ldftn not consumed by the next newobj does not allocate a delegate.
                // Stack-neutral nops between the ldftn and newobj (e.g. Debug IL) are skipped.
                if (opcode is not (ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Newobj or ILOpCode.Nop))
                    pendingDelegateOffset = null;

                // The "moved allocation" annotation only applies when the delegate flows
                // directly into its consuming call. Keep the pending index alive across the
                // delegate newobj and intervening nops; clear it once any other instruction
                // (including the consuming call, already handled above) is processed.
                if (opcode is not (ILOpCode.Newobj or ILOpCode.Nop))
                    pendingDelegateOpportunityIndex = null;

                // A boxed concrete value type that flows straight into an escaping consumer
                // (stored into a reference array, passed to a call/ctor, written to a field, or
                // returned) is a real heap allocation. A box consumed locally (unbox round-trip,
                // type test) does not escape and is not reported. Nops are skipped (Debug IL).
                if (opcode is not (ILOpCode.Box or ILOpCode.Nop))
                {
                    if (pendingBoxOffset is { } boxOffset && IsEscapingBoxConsumer(opcode))
                    {
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "box-value-type",
                            $"box {pendingBoxType?.ToQualifiedDisplayString() ?? "value type"}",
                            "Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it.",
                            pendingBoxInLoop ? "high" : "medium",
                            pendingBoxInLoop,
                            boxOffset,
                            pendingBoxInLoop ? null : "The JIT can elide some non-escaping boxing after inlining; confirm the box escapes (e.g. into a collection or object[])."));
                    }
                    pendingBoxOffset = null;
                    pendingBoxType = null;
                }

                // Remember the receiver-bearing instruction. Nops never carry the receiver, so
                // they do not overwrite it (Debug IL can interleave them before the ldftn).
                if (opcode != ILOpCode.Nop)
                    previousOpcode = opcode;
            }

            return opportunities.ToImmutable();
        }

        // True when a delegate's target method is a closure body emitted on a compiler-
        // generated display class (it closes over captured locals/parameters). The
        // non-capturing lambda cache type is named exactly <>c, and static/instance
        // method groups live on ordinary types, so none of those match.
        static bool IsClosureTarget(MemberRef target)
            => target.Kind != MemberKind.Unsupported
               && DeclaringTypeLeafName(target.DeclaringType)
                   .StartsWith("<>c__DisplayClass", StringComparison.Ordinal);

        static string DeclaringTypeLeafName(TypeRef type)
        {
            string name = type.Kind == TypeRefKind.GenericInstance ? type.ElementType?.Name ?? "" : type.Name;
            int nested = name.LastIndexOf('+');
            return nested < 0 ? name : name[(nested + 1)..];
        }

        // Opcodes that consume a boxed value in a way that makes it escape (so the box is a
        // real heap allocation): stored into a reference array, passed to a call/ctor, written
        // to a field, or returned. Local round-trips (unbox/unbox.any/isinst/castclass/pop) are
        // deliberately absent.
        static bool IsEscapingBoxConsumer(ILOpCode op)
            => op is ILOpCode.Stelem_ref or ILOpCode.Call or ILOpCode.Callvirt
                or ILOpCode.Newobj or ILOpCode.Stfld or ILOpCode.Stsfld or ILOpCode.Ret;

        // True when a boxed value flows straight into a throw within a short window — the classic
        // `box <enum>; call Format; newobj <Exception>; throw` exception-message pattern. Such a
        // box only allocates on the error path, so it is not hot pay-dirt and is not loop-promoted.
        bool BoxFeedsThrowSoon(byte[] il, int position)
        {
            for (int steps = 0; steps < 6 && position < il.Length; steps++)
            {
                int probeOffset = position;
                var op = ReadOpcode(il, ref position);
                if (op is ILOpCode.Throw or ILOpCode.Rethrow)
                    return true;
                if (IsControlFlowDivergent(op))
                    return false;
                SkipOperand(il, op, ref position, probeOffset);
            }
            return false;
        }

        // Opcodes after which straight-line flow no longer reliably reaches the next instruction
        // (branches, switch, returns, leave/endfinally), so a forward throw-probe must stop.
        static bool IsControlFlowDivergent(ILOpCode op)
            => op is ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s
                or ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Beq or ILOpCode.Beq_s
                or ILOpCode.Bne_un or ILOpCode.Bne_un_s or ILOpCode.Bge or ILOpCode.Bge_s
                or ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Ble or ILOpCode.Ble_s
                or ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Bge_un or ILOpCode.Bge_un_s
                or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s
                or ILOpCode.Blt_un or ILOpCode.Blt_un_s or ILOpCode.Switch
                or ILOpCode.Ret or ILOpCode.Leave or ILOpCode.Leave_s
                or ILOpCode.Endfinally or ILOpCode.Endfilter;

        // True only when a `box` operand is positively identified as a value type that
        // unconditionally allocates. ECMA-335 allows `box` on reference types (no allocation),
        // generic parameters (compiler-mandated / JIT-specialized), and `Nullable<T>` (no
        // allocation when null) — all excluded to avoid false positives. In-assembly types are
        // resolved authoritatively via their base type; external types are accepted only from a
        // curated set of well-known framework value types.
        bool IsAllocatingValueTypeBox(int token, TypeRef boxed)
        {
            // Nullable<T> boxing allocates only when HasValue; conservatively exclude.
            var leaf = boxed.Kind == TypeRefKind.GenericInstance ? boxed.ElementType ?? boxed : boxed;
            if (leaf.Kind == TypeRefKind.Definition && leaf.Namespace == "System" && leaf.Name == "Nullable`1")
                return false;

            try
            {
                var handle = MetadataTokens.EntityHandle(token);
                if (handle.Kind == HandleKind.TypeDefinition)
                    return IsValueTypeDefinition((TypeDefinitionHandle)handle);
                // A constructed generic type (e.g. Box<int>) is a TypeSpec whose signature blob
                // directly encodes value-type-ness (ELEMENT_TYPE_VALUETYPE vs ELEMENT_TYPE_CLASS),
                // so we don't need to resolve the definition. Covers in-assembly and external
                // generic structs alike; Nullable<T> is already excluded above.
                if (handle.Kind == HandleKind.TypeSpecification)
                    return IsValueTypeSpec((TypeSpecificationHandle)handle);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
            {
                return false;
            }

            return leaf.Kind == TypeRefKind.Definition
                && leaf.TrustedFrameworkAssembly
                && IsWellKnownValueType(leaf.Namespace, leaf.Name);
        }

        // Reads a TypeSpec signature blob to decide value-type-ness directly from metadata. The
        // signature is an ELEMENT_TYPE_* stream; a generic instance is GENERICINST followed by
        // VALUETYPE (0x11) or CLASS (0x12), and a bare value/class spec starts with that byte.
        bool IsValueTypeSpec(TypeSpecificationHandle handle)
        {
            const byte ElementTypeValueType = 0x11;
            const byte ElementTypeGenericInst = 0x15;
            var blob = _reader.GetBlobReader(_reader.GetTypeSpecification(handle).Signature);
            if (blob.RemainingBytes == 0)
                return false;
            byte code = blob.ReadByte();
            if (code == ElementTypeGenericInst)
            {
                if (blob.RemainingBytes == 0)
                    return false;
                code = blob.ReadByte();
            }
            // VALUETYPE (0x11) is a value type; CLASS (0x12) and everything else is not.
            return code == ElementTypeValueType;
        }

        // Authoritative in-assembly check: a value type extends System.ValueType or System.Enum.
        bool IsValueTypeDefinition(TypeDefinitionHandle handle)
        {
            var baseHandle = _reader.GetTypeDefinition(handle).BaseType;
            if (baseHandle.IsNil)
                return false;
            var (ns, name) = baseHandle.Kind switch
            {
                HandleKind.TypeReference => (_reader.GetString(_reader.GetTypeReference((TypeReferenceHandle)baseHandle).Namespace),
                    _reader.GetString(_reader.GetTypeReference((TypeReferenceHandle)baseHandle).Name)),
                HandleKind.TypeDefinition => (_reader.GetString(_reader.GetTypeDefinition((TypeDefinitionHandle)baseHandle).Namespace),
                    _reader.GetString(_reader.GetTypeDefinition((TypeDefinitionHandle)baseHandle).Name)),
                _ => ("", ""),
            };
            return ns == "System" && name is "ValueType" or "Enum";
        }

        static bool IsWellKnownValueType(string ns, string name)
            => (ns == "System" && name is "Boolean" or "Byte" or "SByte" or "Char"
                    or "Int16" or "UInt16" or "Int32" or "UInt32" or "Int64" or "UInt64"
                    or "Single" or "Double" or "IntPtr" or "UIntPtr" or "Decimal"
                    or "Half" or "Int128" or "UInt128"
                    or "DateTime" or "DateTimeOffset" or "TimeSpan" or "Guid")
               || (ns == "System.Numerics" && name is "BigInteger" or "Complex")
               || (ns == "System" && name.StartsWith("ValueTuple", StringComparison.Ordinal))
               || (ns == "System.Collections.Generic" && name == "KeyValuePair`2");

        // Resolves a metadata type token (TypeDef/TypeRef/TypeSpec) to a TypeRef, used to
        // inspect a newarr element type. Returns Unsupported on any malformed/unknown token.
        TypeRef ResolveTypeToken(int token, GenericScope scope)
        {
            try
            {
                var handle = MetadataTokens.EntityHandle(token);
                return handle.Kind switch
                {
                    HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, (TypeDefinitionHandle)handle, 0),
                    HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(_reader, (TypeReferenceHandle)handle, 0),
                    HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(_reader, scope, (TypeSpecificationHandle)handle, 0),
                    _ => TypeRef.Unsupported("newarr element"),
                };
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
            {
                return TypeRef.Unsupported("newarr element");
            }
        }

        // True only for the unmanaged primitive element types that C# stackalloc accepts.
        // Enums and unmanaged structs are also stackalloc-eligible but require resolving the
        // type's layout/base, so they are conservatively excluded (kept as small-array).
        static bool IsStackallocEligibleElement(TypeRef element)
            => element.Kind == TypeRefKind.Definition
               && element.Namespace == "System"
               && element.Name is "Boolean" or "Byte" or "SByte" or "Char"
                   or "Int16" or "UInt16" or "Int32" or "UInt32"
                   or "Int64" or "UInt64" or "Single" or "Double"
                   or "IntPtr" or "UIntPtr";

        // Conservative, sound local-escape check for a freshly created array. Returns true
        // only when the array is stored straight into a local (`newarr; stloc.X`) whose every
        // load is an in-place element access / length read — never returned, stored to a
        // field, address-taken, or passed to a call. Any shape we cannot prove local returns
        // false (keep the non-committal `small-array`), so a false positive is impossible.
        bool ArrayProvablyStaysLocal(byte[] il, int positionAfterNewarr, MethodIdentity caller)
        {
            try
            {
                int slot = ReadStoreLocalSlot(il, positionAfterNewarr);
                if (slot < 0)
                    return false;
                return !LocalArrayEscapes(il, slot);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
            {
                return false;
            }
        }

        // If the next instruction stores to a local, returns its slot; otherwise -1.
        static int ReadStoreLocalSlot(byte[] il, int position)
        {
            if (position >= il.Length)
                return -1;
            int probe = position;
            var opcode = ReadOpcode(il, ref probe);
            return opcode switch
            {
                ILOpCode.Stloc_0 => 0,
                ILOpCode.Stloc_1 => 1,
                ILOpCode.Stloc_2 => 2,
                ILOpCode.Stloc_3 => 3,
                ILOpCode.Stloc_s => il[probe],
                ILOpCode.Stloc => BinaryPrimitives.ReadUInt16LittleEndian(il.AsSpan(probe)),
                _ => -1,
            };
        }

        // Scans the whole body for uses of the array local. The array escapes if its address
        // is taken (`ldloca`) or any load is not consumed in place by an element access /
        // length read. Conservative: any unrecognized use counts as an escape.
        bool LocalArrayEscapes(byte[] il, int slot)
        {
            int position = 0;
            while (position < il.Length)
            {
                int offset = position;
                var opcode = ReadOpcode(il, ref position);
                if (IsLoadLocalAddress(il, opcode, ref position, slot, out bool addressOfSlot))
                {
                    if (addressOfSlot)
                        return true; // address taken -> may escape
                    continue;
                }
                if (IsLoadLocal(il, opcode, ref position, slot, out bool loadsSlot))
                {
                    if (loadsSlot && ArrayLoadEscapes(il, position))
                        return true;
                    continue;
                }
                SkipOperand(il, opcode, ref position, offset);
            }
            return false;
        }

        static bool IsLoadLocalAddress(byte[] il, ILOpCode opcode, ref int position, int slot, out bool matchesSlot)
        {
            matchesSlot = false;
            switch (opcode)
            {
                case ILOpCode.Ldloca_s:
                    matchesSlot = il[position] == slot;
                    position += 1;
                    return true;
                case ILOpCode.Ldloca:
                    matchesSlot = BinaryPrimitives.ReadUInt16LittleEndian(il.AsSpan(position)) == slot;
                    position += 2;
                    return true;
                default:
                    return false;
            }
        }

        static bool IsLoadLocal(byte[] il, ILOpCode opcode, ref int position, int slot, out bool matchesSlot)
        {
            matchesSlot = false;
            switch (opcode)
            {
                case ILOpCode.Ldloc_0: matchesSlot = slot == 0; return true;
                case ILOpCode.Ldloc_1: matchesSlot = slot == 1; return true;
                case ILOpCode.Ldloc_2: matchesSlot = slot == 2; return true;
                case ILOpCode.Ldloc_3: matchesSlot = slot == 3; return true;
                case ILOpCode.Ldloc_s:
                    matchesSlot = il[position] == slot;
                    position += 1;
                    return true;
                case ILOpCode.Ldloc:
                    matchesSlot = BinaryPrimitives.ReadUInt16LittleEndian(il.AsSpan(position)) == slot;
                    position += 2;
                    return true;
                default:
                    return false;
            }
        }

        // Given the array reference freshly loaded onto the stack, decide whether this use
        // keeps it local. Walks forward tracking how many extra slots sit above the array;
        // an element access / length read that consumes the array at the right depth is local,
        // anything else (return, store, call argument, ambiguous stack shape) is an escape.
        bool ArrayLoadEscapes(byte[] il, int position)
        {
            int extra = 0; // stack slots pushed above the array reference
            while (position < il.Length)
            {
                int offset = position;
                var opcode = ReadOpcode(il, ref position);
                switch (opcode)
                {
                    // Simple single pushes (indices, values) layered above the array.
                    case ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                        or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6
                        or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or ILOpCode.Ldnull:
                        extra++;
                        break;
                    case ILOpCode.Ldc_i4_s:
                        extra++;
                        position += 1;
                        break;
                    case ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4:
                        extra++;
                        position += 4;
                        break;
                    case ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8:
                        extra++;
                        position += 8;
                        break;
                    case ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3
                        or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3:
                        extra++;
                        break;
                    case ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Ldarg_s or ILOpCode.Ldarga_s:
                        extra++;
                        position += 1;
                        break;
                    // Length read: pops the array. Local only when the array is on top.
                    case ILOpCode.Ldlen:
                        return extra != 0;
                    // Element read: pops index + array. Local when exactly the index is above.
                    case ILOpCode.Ldelem or ILOpCode.Ldelem_i or ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2
                        or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8
                        or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4 or ILOpCode.Ldelem_ref:
                        return extra != 1;
                    // Element store: pops value + index + array. Local when index+value are above.
                    case ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2
                        or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8
                        or ILOpCode.Stelem_ref:
                        return extra != 2;
                    default:
                        // Anything else consuming the array (ret, stfld, call, box, element
                        // address, dup-aliasing, branch) is treated as an escape.
                        return true;
                }
            }
            return true;
        }


        void ScanBody(byte[] il, MethodIdentity caller, GenericScope callerScope,
            ImmutableArray<DirectCall>.Builder calls,
            ImmutableArray<UnsafeEvidence>.Builder unsafeEvidence,
            bool includeIndirectOpcodes,
            IReadOnlyList<(int Start, int End)> loopRegions)
        {
            int position = 0;
            while (position < il.Length)
            {
                int offset = position;
                var opcode = ReadOpcode(il, ref position);
                switch (opcode)
                {
                    case ILOpCode.Call:
                    case ILOpCode.Callvirt:
                    case ILOpCode.Newobj:
                    case ILOpCode.Ldftn:
                    case ILOpCode.Ldvirtftn:
                    {
                        int token = ReadInt32(il, ref position, offset);
                        var callee = MemberResolver.ResolveMethod(_reader, MetadataTokens.EntityHandle(token), callerScope);
                        bool inLoop = IsInLoopRegion(offset, loopRegions);
                        calls.Add(new DirectCall(caller, callee, offset, token, PeelToDefinitionToken(token), ToCallKind(opcode), inLoop));
                        if (IsUnsafeCall(callee))
                        {
                            unsafeEvidence.Add(new UnsafeEvidence(
                                caller,
                                "Unsafe call",
                                FormatMember(callee),
                                FormatCallKind(ToCallKind(opcode)),
                                offset,
                                token));
                        }
                        break;
                    }
                    case ILOpCode.Calli:
                    {
                        int token = ReadInt32(il, ref position, offset);
                        calls.Add(new DirectCall(caller, MemberRef.Unsupported($"calli signature token 0x{token:X8}"), offset, token, token, CallKind.CallIndirect, IsInLoopRegion(offset, loopRegions)));
                        unsafeEvidence.Add(new UnsafeEvidence(caller, "Unsafe operation", "calli", "calli", offset, token));
                        break;
                    }
                    default:
                        if (UnsafeOpcodeName(opcode, includeIndirectOpcodes) is { } unsafeOpcode)
                            unsafeEvidence.Add(new UnsafeEvidence(caller, "Unsafe operation", unsafeOpcode, "opcode", offset, null));
                        SkipOperand(il, opcode, ref position, offset);
                        break;
                }
            }
        }

        // Peel a generic-method call operand (MethodSpec) to the underlying MethodDef
        // token in this assembly, so a call to G<int> is attributed to G's definition.
        // Returns the token unchanged when it is not a same-assembly MethodSpec instantiation.
        int PeelToDefinitionToken(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind == HandleKind.MethodSpecification)
            {
                var spec = _reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                if (spec.Method.Kind == HandleKind.MethodDefinition)
                    return MetadataTokens.GetToken(spec.Method);
            }
            return token;
        }

        IReadOnlyList<(int Start, int End)> CollectLoopRegions(byte[] il)
        {
            try
            {
                var regions = new List<(int Start, int End)>();
                int position = 0;
                while (position < il.Length)
                {
                    int offset = position;
                    var opcode = ReadOpcode(il, ref position);
                    // TryReadBranchTarget already advances past a branch operand when it
                    // returns true; only non-branch opcodes still need SkipOperand. Calling
                    // SkipOperand after a (forward) branch would double-advance and desync the scan.
                    if (TryReadBranchTarget(opcode, il, ref position, offset, out int target))
                    {
                        if (target < offset)
                            regions.Add((target, offset));
                    }
                    else
                    {
                        SkipOperand(il, opcode, ref position, offset);
                    }
                }
                return regions;
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
            {
                return [];
            }
        }

        static bool IsInLoopRegion(int offset, IReadOnlyList<(int Start, int End)> regions)
            => regions.Any(region => offset >= region.Start && offset <= region.End);

        // Body-scan signals the call index cannot see: array allocations (newarr),
        // throw/rethrow sites, and exception-handling clauses. Mirrors the loop-region
        // scan's defensive structure — a malformed body yields empty signals, never a
        // failed index build.
        BodySignals CollectBodySignals(byte[] il, MethodBodyBlock body, GenericScope scope, IReadOnlyList<(int Start, int End)> loopRegions)
        {
            int newarr = 0, throws = 0, boxes = 0;
            bool allocInLoop = false;
            var arrayOffsets = ImmutableArray.CreateBuilder<int>();
            var throwOffsets = ImmutableArray.CreateBuilder<int>();
            var boxOffsets = ImmutableArray.CreateBuilder<int>();
            var throwPathNewObjectOffsets = ImmutableArray.CreateBuilder<int>();
            try
            {
                int position = 0;
                while (position < il.Length)
                {
                    int offset = position;
                    var opcode = ReadOpcode(il, ref position);
                    switch (opcode)
                    {
                        case ILOpCode.Newarr:
                            newarr++;
                            arrayOffsets.Add(offset);
                            allocInLoop |= IsInLoopRegion(offset, loopRegions);
                            break;
                        case ILOpCode.Throw or ILOpCode.Rethrow:
                            throws++;
                            throwOffsets.Add(offset);
                            break;
                        case ILOpCode.Newobj:
                        {
                            int afterNewobj = position;
                            SkipOperand(il, opcode, ref afterNewobj, offset);
                            if (NewObjectFeedsThrowSoon(il, afterNewobj))
                                throwPathNewObjectOffsets.Add(offset);
                            break;
                        }
                        case ILOpCode.Box:
                        {
                            // Boxing a genuinely-allocating value type is a heap allocation, like
                            // newobj/newarr. Counted unconditionally (the box-value-type opportunity
                            // adds the escape gate separately for actionable triage).
                            int boxStart = position;
                            int token = ReadInt32(il, ref position, offset);
                            if (IsAllocatingValueTypeBox(token, ResolveTypeToken(token, scope)))
                            {
                                boxes++;
                                boxOffsets.Add(offset);
                                allocInLoop |= IsInLoopRegion(offset, loopRegions);
                            }
                            position = boxStart; // let the shared SkipOperand advance past the token
                            break;
                        }
                    }
                    SkipOperand(il, opcode, ref position, offset);
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
            {
                // Fall through with whatever was collected before the malformed instruction.
            }

            int catches = 0, finallys = 0;
            foreach (var region in body.ExceptionRegions)
            {
                switch (region.Kind)
                {
                    case ExceptionRegionKind.Catch or ExceptionRegionKind.Filter:
                        catches++;
                        break;
                    case ExceptionRegionKind.Finally or ExceptionRegionKind.Fault:
                        finallys++;
                        break;
                }
            }

            return new BodySignals(
                newarr,
                throws,
                catches,
                finallys,
                arrayOffsets.ToImmutable(),
                throwOffsets.ToImmutable(),
                boxes,
                boxOffsets.ToImmutable(),
                allocInLoop,
                throwPathNewObjectOffsets.ToImmutable());
        }

        bool NewObjectFeedsThrowSoon(byte[] il, int position)
        {
            var visitedOffsets = new HashSet<int>();
            for (int steps = 0; steps < 8 && position < il.Length; steps++)
            {
                int probeOffset = position;
                if (!visitedOffsets.Add(probeOffset))
                    return false;

                var op = ReadOpcode(il, ref position);
                if (op is ILOpCode.Throw or ILOpCode.Rethrow)
                    return true;
                if (op is ILOpCode.Br or ILOpCode.Br_s)
                {
                    if (!TryReadBranchTarget(op, il, ref position, probeOffset, out int target)
                        || target < 0
                        || target >= il.Length)
                    {
                        return false;
                    }
                    position = target;
                    continue;
                }
                if (IsControlFlowDivergent(op))
                    return false;

                SkipOperand(il, op, ref position, probeOffset);
            }
            return false;
        }

        bool TryReadBranchTarget(ILOpCode opcode, byte[] il, ref int position, int offset, out int target)
        {
            target = -1;
            switch (opcode)
            {
                case ILOpCode.Br_s:
                    target = offset + 2 + (sbyte)ReadByte(il, ref position, offset);
                    return true;
                case ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s
                    or ILOpCode.Bge_s or ILOpCode.Bgt_s or ILOpCode.Ble_s or ILOpCode.Blt_s
                    or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s
                    or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s or ILOpCode.Leave_s:
                    target = offset + 2 + (sbyte)ReadByte(il, ref position, offset);
                    return true;
                case ILOpCode.Br:
                    target = offset + 5 + ReadInt32(il, ref position, offset);
                    return true;
                case ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq
                    or ILOpCode.Bge or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt
                    or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un
                    or ILOpCode.Ble_un or ILOpCode.Blt_un or ILOpCode.Leave:
                    target = offset + 5 + ReadInt32(il, ref position, offset);
                    return true;
                default:
                    return false;
            }
        }

        static bool IsUnsafeCall(MemberRef member)
            => IsUnsafeApi(member) || member.ParameterTypes.Append(member.ReturnType).Any(ContainsUnsafeType);

        static bool IsBitConverterGetBytes(MemberRef member)
            => member.Kind != MemberKind.Unsupported
                && FrameworkIdentity.IsCoreLibraryType(member.DeclaringType, "System", "BitConverter")
                && member.Name == "GetBytes";

        // A `ToArray()` call that copies a span into a freshly allocated array. ReadOnlySpan<T>
        // and Span<T> are single-argument corelib generic value types, so the receiver is a
        // GenericInstance over the corelib definition; requiring that exact identity (assembly,
        // namespace, arity) avoids matching a user type that happens to be named System.Span
        // with its own ToArray. The definition name carries arity (e.g. "ReadOnlySpan`1"), so
        // compare on the name before the backtick.
        //
        // Scoped to spans deliberately: ReadOnlySpan<T>/Span<T> exist to avoid allocation, so
        // materializing one back into an array is a high-signal, low-volume copy. List<T>.
        // ToArray() is far more common and usually a legitimate snapshot, so promoting it
        // without escape/usage analysis would flood the section — left to a follow-up.
        static bool IsSpanToArrayCopy(MemberRef member, out string receiver)
        {
            receiver = "";
            if (member.Kind == MemberKind.Unsupported || member.Name != "ToArray")
                return false;
            var declaring = member.DeclaringType;
            if (declaring.Kind != TypeRefKind.GenericInstance || declaring.TypeArguments.Length != 1)
                return false;
            var definition = declaring.ElementType;
            if (definition is null
                || !definition.TrustedFrameworkAssembly
                || definition.Assembly != TypeRef.CoreLibrary
                || definition.Namespace != "System")
                return false;
            var name = StripGenericArity(definition.Name);
            if (name is not ("ReadOnlySpan" or "Span"))
                return false;
            receiver = $"System.{name}<T>::ToArray";
            return true;
        }

        static string StripGenericArity(string name)
        {
            int tick = name.IndexOf('`');
            return tick < 0 ? name : name[..tick];
        }

        static bool IsUnsafeApi(MemberRef member) => IsUnsafeApi(member.DeclaringType);

        static bool IsUnsafeApi(TypeRef type)
            => FrameworkIdentity.IsCoreLibraryType(type, "System.Runtime.CompilerServices", "Unsafe")
                || FrameworkIdentity.IsKnownFrameworkType(type, "System.Runtime.CompilerServices.Unsafe", "System.Runtime.CompilerServices", "Unsafe");

        static bool ContainsUnsafeType(TypeRef type)
        {
            if (type.Kind is TypeRefKind.Pointer or TypeRefKind.Pinned)
                return true;
            if (type.Kind == TypeRefKind.Unsupported
                && type.UnsupportedReason.Contains("function pointer", StringComparison.OrdinalIgnoreCase))
                return true;
            if (type.ElementType is not null && ContainsUnsafeType(type.ElementType))
                return true;
            return type.TypeArguments.Any(ContainsUnsafeType);
        }

        static string FormatMember(MemberRef member)
        {
            if (member.Kind == MemberKind.Unsupported)
                return member.DeclaringType.ToDisplayString();

            string name = member.Name;
            if (member.TypeArguments.Length > 0)
                name += $"<{string.Join(", ", member.TypeArguments.Select(t => t.ToQualifiedDisplayString()))}>";
            return $"{member.DeclaringType.ToQualifiedDisplayString()}.{name}({string.Join(", ", member.ParameterTypes.Select(p => p.ToQualifiedDisplayString()))})";
        }

        static string FormatMethod(MethodIdentity method)
            => $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}({string.Join(", ", method.ParameterTypes.Select(p => p.ToQualifiedDisplayString()))})";

        static string FormatCallKind(CallKind kind) => kind switch
        {
            CallKind.Call => "call",
            CallKind.CallVirtual => "callvirt",
            CallKind.NewObject => "newobj",
            CallKind.LoadFunction => "ldftn",
            CallKind.LoadVirtualFunction => "ldvirtftn",
            _ => "calli",
        };

        static string? UnsafeOpcodeName(ILOpCode opcode, bool includeIndirectOpcodes) => opcode switch
        {
            ILOpCode.Localloc => "localloc",
            ILOpCode.Cpblk => "cpblk",
            ILOpCode.Initblk => "initblk",
            ILOpCode.Ldind_i1 when includeIndirectOpcodes => "ldind.i1",
            ILOpCode.Ldind_u1 when includeIndirectOpcodes => "ldind.u1",
            ILOpCode.Ldind_i2 when includeIndirectOpcodes => "ldind.i2",
            ILOpCode.Ldind_u2 when includeIndirectOpcodes => "ldind.u2",
            ILOpCode.Ldind_i4 when includeIndirectOpcodes => "ldind.i4",
            ILOpCode.Ldind_u4 when includeIndirectOpcodes => "ldind.u4",
            ILOpCode.Ldind_i8 when includeIndirectOpcodes => "ldind.i8",
            ILOpCode.Ldind_i when includeIndirectOpcodes => "ldind.i",
            ILOpCode.Ldind_r4 when includeIndirectOpcodes => "ldind.r4",
            ILOpCode.Ldind_r8 when includeIndirectOpcodes => "ldind.r8",
            ILOpCode.Ldind_ref when includeIndirectOpcodes => "ldind.ref",
            ILOpCode.Stind_ref when includeIndirectOpcodes => "stind.ref",
            ILOpCode.Stind_i1 when includeIndirectOpcodes => "stind.i1",
            ILOpCode.Stind_i2 when includeIndirectOpcodes => "stind.i2",
            ILOpCode.Stind_i4 when includeIndirectOpcodes => "stind.i4",
            ILOpCode.Stind_i8 when includeIndirectOpcodes => "stind.i8",
            ILOpCode.Stind_i when includeIndirectOpcodes => "stind.i",
            ILOpCode.Stind_r4 when includeIndirectOpcodes => "stind.r4",
            ILOpCode.Stind_r8 when includeIndirectOpcodes => "stind.r8",
            _ => null,
        };

        static CallKind ToCallKind(ILOpCode opcode) => opcode switch
        {
            ILOpCode.Call => CallKind.Call,
            ILOpCode.Callvirt => CallKind.CallVirtual,
            ILOpCode.Newobj => CallKind.NewObject,
            ILOpCode.Ldftn => CallKind.LoadFunction,
            _ => CallKind.LoadVirtualFunction,
        };

        GenericScope CreateScope(TypeDefinition typeDef, MethodDefinition methodDef)
            => new(GenericParameterNames(typeDef.GetGenericParameters()), GenericParameterNames(methodDef.GetGenericParameters()));

        ImmutableArray<string> GenericParameterNames(GenericParameterHandleCollection handles)
        {
            if (handles.Count == 0)
                return [];
            var names = ImmutableArray.CreateBuilder<string>(handles.Count);
            foreach (var handle in handles)
                names.Add(_reader.GetString(_reader.GetGenericParameter(handle).Name));
            return names.MoveToImmutable();
        }

        static ILOpCode ReadOpcode(byte[] il, ref int position)
        {
            byte first = ReadByte(il, ref position, position);
            if (first != 0xFE)
                return (ILOpCode)first;
            byte second = ReadByte(il, ref position, position - 1);
            return (ILOpCode)(0xFE00 | second);
        }

        void SkipOperand(byte[] il, ILOpCode opcode, ref int position, int offset)
        {
            switch (opcode)
            {
                case ILOpCode.Switch:
                {
                    int count = ReadInt32(il, ref position, offset);
                    if (count < 0)
                        throw new BadImageFormatException($"Malformed switch at IL_{offset:X4} in method body from {_path}");
                    long targetPosition = (long)position + (long)count * 4;
                    if (targetPosition < position || targetPosition > il.Length)
                        throw new BadImageFormatException($"Malformed switch at IL_{offset:X4} in method body from {_path}");
                    position = (int)targetPosition;
                    break;
                }
                case ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s
                    or ILOpCode.Bge_s or ILOpCode.Bgt_s or ILOpCode.Ble_s or ILOpCode.Blt_s
                    or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s
                    or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s or ILOpCode.Leave_s
                    or ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s
                    or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Stloc_s
                    or ILOpCode.Ldc_i4_s or ILOpCode.Unaligned:
                    Advance(il, ref position, 1, offset);
                    break;
                case ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg
                    or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc:
                    Advance(il, ref position, 2, offset);
                    break;
                case ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq
                    or ILOpCode.Bge or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt
                    or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un
                    or ILOpCode.Ble_un or ILOpCode.Blt_un or ILOpCode.Leave
                    or ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4
                    or ILOpCode.Jmp or ILOpCode.Ldstr
                    or ILOpCode.Call or ILOpCode.Calli or ILOpCode.Callvirt or ILOpCode.Newobj
                    or ILOpCode.Ldftn or ILOpCode.Ldvirtftn
                    or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld
                    or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld
                    or ILOpCode.Cpobj or ILOpCode.Ldobj or ILOpCode.Castclass
                    or ILOpCode.Isinst or ILOpCode.Unbox or ILOpCode.Stobj
                    or ILOpCode.Box or ILOpCode.Newarr or ILOpCode.Ldelema
                    or ILOpCode.Ldelem or ILOpCode.Stelem or ILOpCode.Unbox_any
                    or ILOpCode.Refanyval or ILOpCode.Mkrefany or ILOpCode.Initobj
                    or ILOpCode.Constrained or ILOpCode.Sizeof or ILOpCode.Ldtoken:
                    Advance(il, ref position, 4, offset);
                    break;
                case ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8:
                    Advance(il, ref position, 8, offset);
                    break;
                case NoPrefix:
                    Advance(il, ref position, 1, offset);
                    break;
            }
        }

        static void Advance(byte[] il, ref int position, int bytes, int offset)
        {
            long targetPosition = (long)position + bytes;
            if (targetPosition < position || targetPosition > il.Length)
                throw new BadImageFormatException($"Malformed IL operand at IL_{offset:X4}");
            position = (int)targetPosition;
        }

        // True when the String.Concat at `concatOffset` — whose result is stored by the
        // instruction at `storeOffset` — accumulates into one of its own arguments, i.e.
        // `s = String.Concat(s, …)` (the `s += …` lowering). Each iteration copies the whole
        // growing accumulator: the canonical O(n^2) StringBuilder anti-pattern.
        //
        // Soundness is by stack-aware simulation, not a flat load window. It replays the eval
        // stack across the basic block that produces the concat's arguments, tagging each stack
        // slot with one bit — "produced by a bare load of the destination slot" — and reports
        // accumulation only when a value actually consumed by the concat carries that bit. So a
        // prior unrelated use of the slot (`Foo(s); s = a + b;`) is not misread: that load is
        // popped by its own consumer before the concat. A derived accumulator
        // (`s = s.Substring(..) + x`) is conservatively not matched (the bit does not survive the
        // intermediate call). Anything the small stack model does not understand makes the probe
        // bail, so it never reports a false positive on unmodeled IL.
        public bool ConcatAccumulatesIntoSource(byte[] il, int concatOffset, int storeOffset, int concatArgCount, GenericScope callerScope)
        {
            const int ArgSlotBias = 1 << 20;
            try
            {
                if (storeOffset < 0 || storeOffset >= il.Length || concatOffset < 0 || concatArgCount <= 0)
                    return false;

                // The concat result must be stored straight back into a local/parameter slot.
                int sp = storeOffset;
                var storeOp = ReadOpcode(il, ref sp);
                if (!TryReadLocalSlot(il, storeOp, ref sp, storeOffset, out bool storeIsStore, out bool storeIsArg, out int storeSlot)
                    || !storeIsStore)
                    return false;
                int storeKey = (storeIsArg ? ArgSlotBias : 0) | storeSlot;

                // Pass 1: find the start of the basic block that feeds the concat — the point
                // after the most recent store/branch/terminator before it. (Calls are not block
                // boundaries.)
                int blockStart = 0;
                int p = 0;
                while (p < concatOffset)
                {
                    int o = p;
                    var op = ReadOpcode(il, ref p);
                    bool isLocal = TryReadLocalSlot(il, op, ref p, o, out bool localStore, out _, out _);
                    if (!isLocal)
                        SkipOperandStatic(il, op, ref p, o);
                    if (p <= concatOffset && ((isLocal && localStore) || EndsConcatArgumentBlock(op)))
                        blockStart = p;
                }

                // Pass 2: simulate the eval stack from the block start to the concat. Each slot
                // carries a bit: was it produced by a bare load of the destination slot?
                var stack = new List<bool>();
                p = blockStart;
                while (p < concatOffset)
                {
                    int o = p;
                    var op = ReadOpcode(il, ref p);
                    if (TryReadLocalSlot(il, op, ref p, o, out bool isStore, out bool isArg, out int slot))
                    {
                        if (isStore)
                            return false; // a store starts a new block; model desync -> bail
                        int key = (isArg ? ArgSlotBias : 0) | slot;
                        stack.Add(key == storeKey);
                        continue;
                    }
                    if (!ApplyConcatBlockStackEffect(il, op, ref p, o, stack, callerScope))
                        return false; // unmodeled opcode or stack underflow -> conservative bail
                }

                // The concat's arguments are the top `concatArgCount` slots. Accumulation iff any
                // of them came from a bare load of the destination slot.
                if (stack.Count < concatArgCount)
                    return false;
                for (int i = stack.Count - concatArgCount; i < stack.Count; i++)
                    if (stack[i])
                        return true;
                return false;
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
            {
                return false;
            }
        }

        // Applies one instruction's eval-stack effect during the concat-argument replay, pushing
        // `false` for any value that is not a bare load of the accumulator slot. Returns false to
        // bail the whole probe on a stack underflow or any opcode the model does not cover, so an
        // incomplete model can only lose a detection, never invent one.
        bool ApplyConcatBlockStackEffect(byte[] il, ILOpCode opcode, ref int position, int offset, List<bool> stack, GenericScope callerScope)
        {
            switch (opcode)
            {
                case ILOpCode.Nop:
                    return true;
                // Pure pushes of a non-accumulator value.
                case ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                    or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6
                    or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or ILOpCode.Ldnull:
                    stack.Add(false);
                    return true;
                case ILOpCode.Ldc_i4_s:
                    Advance(il, ref position, 1, offset);
                    stack.Add(false);
                    return true;
                case ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4 or ILOpCode.Ldstr or ILOpCode.Ldsfld
                    or ILOpCode.Ldsflda or ILOpCode.Ldtoken or ILOpCode.Ldftn or ILOpCode.Sizeof:
                    Advance(il, ref position, 4, offset);
                    stack.Add(false);
                    return true;
                case ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8:
                    Advance(il, ref position, 8, offset);
                    stack.Add(false);
                    return true;
                case ILOpCode.Ldloca_s or ILOpCode.Ldarga_s:
                    Advance(il, ref position, 1, offset);
                    stack.Add(false);
                    return true;
                case ILOpCode.Ldloca or ILOpCode.Ldarga:
                    Advance(il, ref position, 2, offset);
                    stack.Add(false);
                    return true;
                // pop 1, push 1 (a derived value — never the bare accumulator).
                case ILOpCode.Conv_i1 or ILOpCode.Conv_i2 or ILOpCode.Conv_i4 or ILOpCode.Conv_i8
                    or ILOpCode.Conv_r4 or ILOpCode.Conv_r8 or ILOpCode.Conv_u4 or ILOpCode.Conv_u8
                    or ILOpCode.Conv_u2 or ILOpCode.Conv_u1 or ILOpCode.Conv_i or ILOpCode.Conv_u
                    or ILOpCode.Conv_r_un or ILOpCode.Neg or ILOpCode.Not or ILOpCode.Ldlen
                    or ILOpCode.Ldind_i1 or ILOpCode.Ldind_u1 or ILOpCode.Ldind_i2 or ILOpCode.Ldind_u2
                    or ILOpCode.Ldind_i4 or ILOpCode.Ldind_u4 or ILOpCode.Ldind_i8 or ILOpCode.Ldind_i
                    or ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8 or ILOpCode.Ldind_ref:
                    return Pop(stack, 1) && Push(stack);
                case ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Ldobj or ILOpCode.Castclass
                    or ILOpCode.Isinst or ILOpCode.Unbox or ILOpCode.Unbox_any or ILOpCode.Box:
                    Advance(il, ref position, 4, offset);
                    return Pop(stack, 1) && Push(stack);
                // pop 2, push 1.
                case ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or ILOpCode.Div_un
                    or ILOpCode.Rem or ILOpCode.Rem_un or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor
                    or ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un or ILOpCode.Ceq or ILOpCode.Cgt
                    or ILOpCode.Cgt_un or ILOpCode.Clt or ILOpCode.Clt_un or ILOpCode.Ldelem_i1
                    or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_i4
                    or ILOpCode.Ldelem_u4 or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_i or ILOpCode.Ldelem_r4
                    or ILOpCode.Ldelem_r8 or ILOpCode.Ldelem_ref:
                    return Pop(stack, 2) && Push(stack);
                case ILOpCode.Ldelem or ILOpCode.Ldelema:
                    Advance(il, ref position, 4, offset);
                    return Pop(stack, 2) && Push(stack);
                case ILOpCode.Dup:
                    if (stack.Count == 0)
                        return false;
                    stack.Add(stack[^1]);
                    return true;
                case ILOpCode.Pop:
                    return Pop(stack, 1);
                case ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj:
                {
                    int token = ReadInt32(il, ref position, offset);
                    var callee = MemberResolver.ResolveMethod(_reader, MetadataTokens.EntityHandle(token), callerScope);
                    if (callee.Kind == MemberKind.Unsupported)
                        return false;
                    int pops = callee.ParameterTypes.Length + (opcode != ILOpCode.Newobj && callee.HasThis ? 1 : 0);
                    if (!Pop(stack, pops))
                        return false;
                    if (opcode == ILOpCode.Newobj || callee.ReturnType.Name != "Void")
                        stack.Add(false);
                    return true;
                }
                default:
                    return false; // unmodeled opcode -> bail (no false positive)
            }
        }

        static bool Pop(List<bool> stack, int count)
        {
            if (stack.Count < count)
                return false;
            stack.RemoveRange(stack.Count - count, count);
            return true;
        }

        static bool Push(List<bool> stack)
        {
            stack.Add(false);
            return true;
        }

        // Decodes a load/store of a local or argument slot, advancing past any inline operand.
        // Returns false for any other opcode (operand left for the caller to skip).
        static bool TryReadLocalSlot(byte[] il, ILOpCode opcode, ref int position, int offset, out bool isStore, out bool isArg, out int slot)
        {
            isStore = false;
            isArg = false;
            slot = -1;
            switch (opcode)
            {
                case ILOpCode.Ldloc_0: slot = 0; return true;
                case ILOpCode.Ldloc_1: slot = 1; return true;
                case ILOpCode.Ldloc_2: slot = 2; return true;
                case ILOpCode.Ldloc_3: slot = 3; return true;
                case ILOpCode.Ldloc_s: slot = ReadByte(il, ref position, offset); return true;
                case ILOpCode.Ldloc: slot = (ushort)ReadInt16(il, ref position, offset); return true;
                case ILOpCode.Stloc_0: isStore = true; slot = 0; return true;
                case ILOpCode.Stloc_1: isStore = true; slot = 1; return true;
                case ILOpCode.Stloc_2: isStore = true; slot = 2; return true;
                case ILOpCode.Stloc_3: isStore = true; slot = 3; return true;
                case ILOpCode.Stloc_s: isStore = true; slot = ReadByte(il, ref position, offset); return true;
                case ILOpCode.Stloc: isStore = true; slot = (ushort)ReadInt16(il, ref position, offset); return true;
                case ILOpCode.Ldarg_0: isArg = true; slot = 0; return true;
                case ILOpCode.Ldarg_1: isArg = true; slot = 1; return true;
                case ILOpCode.Ldarg_2: isArg = true; slot = 2; return true;
                case ILOpCode.Ldarg_3: isArg = true; slot = 3; return true;
                case ILOpCode.Ldarg_s: isArg = true; slot = ReadByte(il, ref position, offset); return true;
                case ILOpCode.Ldarg: isArg = true; slot = (ushort)ReadInt16(il, ref position, offset); return true;
                case ILOpCode.Starg_s: isStore = true; isArg = true; slot = ReadByte(il, ref position, offset); return true;
                case ILOpCode.Starg: isStore = true; isArg = true; slot = (ushort)ReadInt16(il, ref position, offset); return true;
                default:
                    return false;
            }
        }

        // A store/branch/terminator that ends the basic block feeding a concat: it bounds the
        // straight-line region whose eval-stack the accumulation probe replays. Calls are NOT
        // boundaries — a sub-expression call (`s += x.Name`, `s = s + list[i]`) leaves the
        // earlier accumulator value live beneath its arguments. (Store-to-local/parameter is
        // handled separately by the caller via the local-slot decoder.)
        static bool EndsConcatArgumentBlock(ILOpCode opcode)
            => opcode is ILOpCode.Stfld or ILOpCode.Stsfld or ILOpCode.Stobj
                or ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2
                or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8
                or ILOpCode.Stelem_ref or ILOpCode.Stind_i or ILOpCode.Stind_i1 or ILOpCode.Stind_i2
                or ILOpCode.Stind_i4 or ILOpCode.Stind_i8 or ILOpCode.Stind_r4 or ILOpCode.Stind_r8
                or ILOpCode.Stind_ref
                or ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow or ILOpCode.Leave or ILOpCode.Leave_s
                or ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s
                or ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Beq or ILOpCode.Beq_s
                or ILOpCode.Bne_un or ILOpCode.Bne_un_s or ILOpCode.Bge or ILOpCode.Bge_s
                or ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Ble or ILOpCode.Ble_s
                or ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Bge_un or ILOpCode.Bge_un_s
                or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s
                or ILOpCode.Blt_un or ILOpCode.Blt_un_s or ILOpCode.Switch;

        // Operand-length skip for opcodes other than the local load/store ops handled by
        // TryReadLocalSlot. Mirrors SkipOperand but is static and self-contained so the
        // accumulation probe needs no instance state.
        static void SkipOperandStatic(byte[] il, ILOpCode opcode, ref int position, int offset)
        {
            switch (opcode)
            {
                case ILOpCode.Switch:
                {
                    int count = ReadInt32(il, ref position, offset);
                    if (count < 0)
                        throw new BadImageFormatException($"Malformed switch at IL_{offset:X4}");
                    long target = (long)position + (long)count * 4;
                    if (target < position || target > il.Length)
                        throw new BadImageFormatException($"Malformed switch at IL_{offset:X4}");
                    position = (int)target;
                    break;
                }
                case ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s
                    or ILOpCode.Bge_s or ILOpCode.Bgt_s or ILOpCode.Ble_s or ILOpCode.Blt_s
                    or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s
                    or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s or ILOpCode.Leave_s
                    or ILOpCode.Ldarga_s or ILOpCode.Ldloca_s
                    or ILOpCode.Ldc_i4_s or ILOpCode.Unaligned:
                    Advance(il, ref position, 1, offset);
                    break;
                case ILOpCode.Ldarga or ILOpCode.Ldloca:
                    Advance(il, ref position, 2, offset);
                    break;
                case ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq
                    or ILOpCode.Bge or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt
                    or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un
                    or ILOpCode.Ble_un or ILOpCode.Blt_un or ILOpCode.Leave
                    or ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4
                    or ILOpCode.Jmp or ILOpCode.Ldstr
                    or ILOpCode.Call or ILOpCode.Calli or ILOpCode.Callvirt or ILOpCode.Newobj
                    or ILOpCode.Ldftn or ILOpCode.Ldvirtftn
                    or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld
                    or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld
                    or ILOpCode.Cpobj or ILOpCode.Ldobj or ILOpCode.Castclass
                    or ILOpCode.Isinst or ILOpCode.Unbox or ILOpCode.Stobj
                    or ILOpCode.Box or ILOpCode.Newarr or ILOpCode.Ldelema
                    or ILOpCode.Ldelem or ILOpCode.Stelem or ILOpCode.Unbox_any
                    or ILOpCode.Refanyval or ILOpCode.Mkrefany or ILOpCode.Initobj
                    or ILOpCode.Constrained or ILOpCode.Sizeof or ILOpCode.Ldtoken:
                    Advance(il, ref position, 4, offset);
                    break;
                case ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8:
                    Advance(il, ref position, 8, offset);
                    break;
            }
        }

        static byte ReadByte(byte[] il, ref int position, int offset)
        {
            if (position >= il.Length)
                throw new BadImageFormatException($"Malformed IL at IL_{offset:X4}");
            return il[position++];
        }

        static short ReadInt16(byte[] il, ref int position, int offset)
        {
            if (position + 2 > il.Length)
                throw new BadImageFormatException($"Malformed IL operand at IL_{offset:X4}");
            short value = BinaryPrimitives.ReadInt16LittleEndian(il.AsSpan(position));
            position += 2;
            return value;
        }

        static int ReadInt32(byte[] il, ref int position, int offset)
        {
            if (position + 4 > il.Length)
                throw new BadImageFormatException($"Malformed IL operand at IL_{offset:X4}");
            int value = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(position));
            position += 4;
            return value;
        }

        string MethodLabel(TypeDefinitionHandle typeHandle, MethodDefinitionHandle methodHandle)
        {
            try
            {
                var typeDef = _reader.GetTypeDefinition(typeHandle);
                string ns = _reader.GetString(typeDef.Namespace);
                string typeName = _reader.GetString(typeDef.Name);
                string methodName = _reader.GetString(_reader.GetMethodDefinition(methodHandle).Name);
                string fullTypeName = ns.Length == 0 ? typeName : $"{ns}.{typeName}";
                return $"{fullTypeName}::{methodName}";
            }
            catch (Exception ex) when (IsRecoverableMethodFailure(ex))
            {
                return $"0x{MetadataTokens.GetToken(methodHandle):X8}";
            }
        }

        static bool IsRecoverableMethodFailure(Exception ex)
            => ex is BadImageFormatException or InvalidOperationException or ArgumentException
                or ArgumentOutOfRangeException or IndexOutOfRangeException;
    }
}
