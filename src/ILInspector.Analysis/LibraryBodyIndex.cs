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
        bool memorySafetyRulesEnabled,
        UnsafeModeBreakdown unsafeModes,
        IReadOnlyDictionary<int, BodySignals> bodySignals)
    {
        Path = path;
        Methods = methods;
        DirectCalls = directCalls;
        UnsafeEvidence = unsafeEvidence;
        Diagnostics = diagnostics;
        _rawOpportunities = optimizationOpportunities;
        MemorySafetyRulesEnabled = memorySafetyRulesEnabled;
        UnsafeModes = unsafeModes;
        _bodySignals = bodySignals;
    }

    public string Path { get; }
    public ImmutableArray<MethodIdentity> Methods { get; }
    public ImmutableArray<DirectCall> DirectCalls { get; }
    public ImmutableArray<UnsafeEvidence> UnsafeEvidence { get; }
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    readonly ImmutableArray<OptimizationOpportunity> _rawOpportunities;
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
                        reachByToken.TryGetValue(opportunity.Method.MetadataToken, out int reach) && reach != opportunity.RootReach
                            ? opportunity with { RootReach = reach }
                            : opportunity)
                ];
            }
            return _opportunities;
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

    /// <summary>
    /// Per-method analysis signals (allocations, copies, unsafe, reflection,
    /// throw/catch/finally, evidence offsets), keyed by metadata token. Computed once
    /// from the call index and the body-scan signals, reused by the call-graph builders.
    /// </summary>
    Dictionary<int, MethodSignals> Signals => _signals ??= MethodSignalAnalysis.Collect(DirectCalls, UnsafeEvidence, _bodySignals);

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
            index.OptimizationOpportunities, builder.MemorySafetyRulesEnabled, index.UnsafeModes,
            index.BodySignals);
    }

    public ImmutableArray<DirectCall> FindCalls(MemberPattern pattern)
        => [.. DirectCalls.Where(call => pattern.Matches(call.Callee))];

    /// <summary>
    /// The most-leveraged requires-unsafe methods, ranked by distinct direct
    /// callers — the highest-value targets for `unsafe` marking, since marking
    /// them propagates the requirement to the most callers.
    /// </summary>
    public ImmutableArray<UnsafeMethodLeverage> TopUnsafeLeverage(int count = 6)
        => UnsafeLeverage.Top(DirectCalls, Methods, count);

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

        var tokenByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var methodTokens = new HashSet<int>();
        foreach (var method in Methods)
        {
            methodTokens.Add(method.MetadataToken);
            tokenByKey.TryAdd(MethodKey(method.DeclaringType, method.Name, method.ParameterTypes), method.MetadataToken);
        }

        int budget = Math.Max(1, maxNodes);
        int created = 1;
        var expanded = new HashSet<int>();

        int ResolveCallee(DirectCall call)
        {
            if (methodTokens.Contains(call.CalleeDefinitionToken))
                return call.CalleeDefinitionToken;
            if (call.Callee.Kind == MemberKind.Unsupported)
                return 0;
            return tokenByKey.TryGetValue(MethodKey(call.Callee.DeclaringType, call.Callee.Name, call.Callee.ParameterTypes), out int token)
                ? token
                : 0;
        }

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

        var tokenByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var methodTokens = new HashSet<int>();
        foreach (var method in Methods)
        {
            methodTokens.Add(method.MetadataToken);
            tokenByKey.TryAdd(MethodKey(method.DeclaringType, method.Name, method.ParameterTypes), method.MetadataToken);
        }

        int ResolveCalleeToken(DirectCall call)
        {
            // Direct callvirt/call edges to the selected method reference it by its own
            // MethodDef token (peeled from a MethodSpec for generic-method calls). Accept that
            // even when the selected method has no body of its own (abstract/interface/extern)
            // and so is absent from Methods, so a Caller Graph rooted at a bodiless member still
            // surfaces its real inbound callers.
            if (call.CalleeDefinitionToken == rootMethodToken)
                return rootMethodToken;
            if (methodTokens.Contains(call.CalleeDefinitionToken))
                return call.CalleeDefinitionToken;
            if (call.Callee.Kind == MemberKind.Unsupported)
                return 0;
            return tokenByKey.TryGetValue(MethodKey(call.Callee.DeclaringType, call.Callee.Name, call.Callee.ParameterTypes), out int token)
                ? token
                : 0;
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

    static string MethodKey(TypeRef declaringType, string name, ImmutableArray<TypeRef> parameterTypes)
        => $"{declaringType.ToQualifiedDisplayString()}|{name}|{string.Join(",", parameterTypes.Select(type => type.ToQualifiedDisplayString()))}";

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

        public (ImmutableArray<MethodIdentity> Methods, ImmutableArray<DirectCall> DirectCalls, ImmutableArray<UnsafeEvidence> UnsafeEvidence, ImmutableArray<AnalysisDiagnostic> Diagnostics, ImmutableArray<OptimizationOpportunity> OptimizationOpportunities, UnsafeModeBreakdown UnsafeModes, IReadOnlyDictionary<int, BodySignals> BodySignals) Build()
        {
            var methods = ImmutableArray.CreateBuilder<MethodIdentity>();
            var calls = ImmutableArray.CreateBuilder<DirectCall>();
            var unsafeEvidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();
            var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
            var optimizationOpportunities = ImmutableArray.CreateBuilder<OptimizationOpportunity>();
            var bodySignals = new Dictionary<int, BodySignals>();
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
                            && !HasCompilerGeneratedAttribute(methodAttributes))
                            optimizationOpportunities.AddRange(CollectOptimizationOpportunities(il, caller, scope, loopRegions));
                        var signals = CollectBodySignals(il, body);
                        if (signals.Newarr > 0 || signals.Throws > 0 || signals.Catches > 0 || signals.Finallys > 0)
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

            return (methods.ToImmutable(), calls.ToImmutable(), unsafeEvidence.ToImmutable(), diagnostics.ToImmutable(),
                optimizationOpportunities.ToImmutable(), new UnsafeModeBreakdown(none, impl, expl), bodySignals);
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
            bool hasThisAccess = false;
            bool hasInstanceStateAccess = false;
            // Delegate creation is `<push target>; ldftn/ldvirtftn M; newobj DelegateCtor`.
            // Track the pending function-pointer load so a single row is emitted at the
            // newobj (one row per delegate allocation), classified by the target.
            int? pendingDelegateOffset = null;
            bool pendingDelegateCapturing = false;
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
                        if (pendingDelegateOffset is { } ldftnOffset)
                        {
                            // A function pointer was just loaded, so this newobj is the delegate
                            // allocation. Emit one row, classifying capture from the target.
                            opportunities.Add(pendingDelegateCapturing
                                ? new OptimizationOpportunity(
                                    caller,
                                    "capturing-delegate",
                                    "delegate over a captured receiver or closure",
                                    "Each call allocates a closure delegate; a static local function with explicit state parameters avoids it.",
                                    "high",
                                    IsInLoopRegion(offset, loopRegions),
                                    offset,
                                    null)
                                : new OptimizationOpportunity(
                                    caller,
                                    "delegate-allocation",
                                    "delegate over a static method or cached lambda",
                                    "If invoked repeatedly, a cached or static delegate avoids re-allocating it.",
                                    "medium",
                                    IsInLoopRegion(offset, loopRegions),
                                    offset,
                                    "Non-capturing; the compiler may already cache it."));
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
                        break;
                    }
                    case ILOpCode.Ldftn:
                    case ILOpCode.Ldvirtftn:
                    {
                        pendingConstant = null;
                        int token = ReadInt32(il, ref position, offset);
                        // Defer emission to the following newobj (de-dup). Capture is decided
                        // by the target method's declaring type: a lambda that closes over state
                        // is emitted on a compiler-generated display class; a non-capturing
                        // lambda (the `<>c` cache), a static method group, or an instance method
                        // group is not.
                        var ftnTarget = MemberResolver.ResolveMethod(_reader, MetadataTokens.EntityHandle(token), callerScope);
                        pendingDelegateOffset = offset;
                        pendingDelegateCapturing = IsClosureTarget(ftnTarget);
                        break;
                    }
                    case ILOpCode.Ldarg_0:
                        pendingConstant = null;
                        hasThisAccess = true;
                        break;
                    case ILOpCode.Ldarg:
                        pendingConstant = null;
                        if (ReadInt16(il, ref position, offset) == 0)
                            hasThisAccess = true;
                        break;
                    case ILOpCode.Ldarg_s:
                        pendingConstant = null;
                        if (ReadByte(il, ref position, offset) == 0)
                            hasThisAccess = true;
                        break;
                    case ILOpCode.Ldfld:
                    case ILOpCode.Ldflda:
                    case ILOpCode.Stfld:
                        pendingConstant = null;
                        ReadInt32(il, ref position, offset);
                        hasInstanceStateAccess = true;
                        break;
                    default:
                        pendingConstant = null;
                        SkipOperand(il, opcode, ref position, offset);
                        break;
                }

                // A bare ldftn not consumed by the next newobj does not allocate a delegate.
                // Stack-neutral nops between the ldftn and newobj (e.g. Debug IL) are skipped.
                if (opcode is not (ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Newobj or ILOpCode.Nop))
                    pendingDelegateOffset = null;
            }

            if (!caller.IsStatic && !hasThisAccess && !hasInstanceStateAccess && caller.Name != ".ctor" && caller.Name != ".cctor")
            {
                opportunities.Add(new OptimizationOpportunity(
                    caller,
                    "instance-method-no-state",
                    "Instance method with no this-state access",
                    "Consider making the method static if it does not rely on instance state.",
                    "medium",
                    false,
                    null,
                    "Keep public API compatibility in mind."));
            }

            return opportunities.ToImmutable();
        }

        // True when a delegate's target method is a closure body emitted on a compiler-
        // generated display class (it closes over captured locals/parameters). The
        // non-capturing lambda cache type is named exactly <>c (no "DisplayClass"), and
        // static/instance method groups live on ordinary types, so none of those match.
        static bool IsClosureTarget(MemberRef target)
            => target.Kind != MemberKind.Unsupported
               && target.DeclaringType.Name.Contains("DisplayClass", StringComparison.Ordinal);

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
        BodySignals CollectBodySignals(byte[] il, MethodBodyBlock body)
        {
            int newarr = 0, throws = 0;
            var arrayOffsets = ImmutableArray.CreateBuilder<int>();
            var throwOffsets = ImmutableArray.CreateBuilder<int>();
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
                            break;
                        case ILOpCode.Throw or ILOpCode.Rethrow:
                            throws++;
                            throwOffsets.Add(offset);
                            break;
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

            return new BodySignals(newarr, throws, catches, finallys, arrayOffsets.ToImmutable(), throwOffsets.ToImmutable());
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
                && member.DeclaringType.Namespace == "System"
                && member.DeclaringType.Name == "BitConverter"
                && member.Name == "GetBytes";

        // A `ToArray()` call that copies a span into a freshly allocated array. The receiver
        // of a generic instance (ReadOnlySpan<T>/Span<T>) is wrapped in a GenericInstance
        // whose ElementType is the open definition, so unwrap it; the definition name carries
        // arity (e.g. "ReadOnlySpan`1"), so compare on the name before the backtick.
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
            var definition = declaring.Kind == TypeRefKind.GenericInstance ? declaring.ElementType : declaring;
            if (definition is null || definition.Namespace != "System")
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
            => type is { Namespace: "System.Runtime.CompilerServices", Name: "Unsafe" };

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
