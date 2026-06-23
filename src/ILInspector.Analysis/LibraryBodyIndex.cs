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
        bool memorySafetyRulesEnabled,
        UnsafeModeBreakdown unsafeModes)
    {
        Path = path;
        Methods = methods;
        DirectCalls = directCalls;
        UnsafeEvidence = unsafeEvidence;
        Diagnostics = diagnostics;
        MemorySafetyRulesEnabled = memorySafetyRulesEnabled;
        UnsafeModes = unsafeModes;
    }

    public string Path { get; }
    public ImmutableArray<MethodIdentity> Methods { get; }
    public ImmutableArray<DirectCall> DirectCalls { get; }
    public ImmutableArray<UnsafeEvidence> UnsafeEvidence { get; }
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Whether the module opted into the updated memory-safety rules via
    /// <c>MemorySafetyRulesAttribute</c> (Roslyn's <c>UseUpdatedMemorySafetyRules</c>).
    /// When false, every requires-unsafe member is <see cref="CallerUnsafeMode.Implicit"/>.
    /// </summary>
    public bool MemorySafetyRulesEnabled { get; }

    /// <summary>Per-<see cref="CallerUnsafeMode"/> method counts across the whole assembly.</summary>
    public UnsafeModeBreakdown UnsafeModes { get; }

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
            builder.MemorySafetyRulesEnabled, index.UnsafeModes);
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
            if (methodTokens.Contains(call.OperandToken))
                return call.OperandToken;
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
            if (token == 0 || !callsByCaller.TryGetValue(token, out var edges))
            {
                var leafStatus = token == 0 && depth > 0 ? CallTreeStatus.External : CallTreeStatus.Leaf;
                return new CallTreeNode(member, kind, leafStatus, [], new CallTreePerf(0, incomingCounts.TryGetValue(token, out var incoming) ? incoming : 0, 1, inLoop, inLoop ? "loop" : null, null));
            }

            var fanout = edges.Count;
            var fanin = incomingCounts.TryGetValue(token, out var count) ? count : 0;

            if (depth >= maxDepth)
                return new CallTreeNode(member, kind, CallTreeStatus.DepthLimited, [], new CallTreePerf(fanout, fanin, 1, inLoop, inLoop ? "loop" : null, null));

            if (!expanded.Add(token))
                return new CallTreeNode(member, kind, CallTreeStatus.AlreadyShown, [], new CallTreePerf(fanout, fanin, 1, inLoop, inLoop ? "loop" : null, null));

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

            var nodeStatus = truncated
                ? CallTreeStatus.Truncated
                : children.Count == 0 ? CallTreeStatus.Leaf : CallTreeStatus.Expanded;
            var maxTreeDepth = children.Count == 0 ? 1 : 1 + children.Max(child => child.Perf?.MaxDepth ?? 1);
            return new CallTreeNode(member, kind, nodeStatus, children.ToImmutable(), new CallTreePerf(fanout, fanin, maxTreeDepth, inLoop, inLoop ? "loop" : null, null));
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
        var rootMember = root is { } identity
            ? new MemberRef(identity.DeclaringType, identity.Name, identity.ParameterTypes, identity.ReturnType, MemberKind.Method)
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
            if (methodTokens.Contains(call.OperandToken))
                return call.OperandToken;
            if (call.Callee.Kind == MemberKind.Unsupported)
                return 0;
            return tokenByKey.TryGetValue(MethodKey(call.Callee.DeclaringType, call.Callee.Name, call.Callee.ParameterTypes), out int token)
                ? token
                : 0;
        }

        var reverseEdges = DirectCalls
            .GroupBy(call => ResolveCalleeToken(call))
            .Where(group => group.Key != 0)
            .ToDictionary(group => group.Key, group => group.ToList(), EqualityComparer<int>.Default);

        int budget = Math.Max(1, maxNodes);
        int created = 1;
        var expanded = new HashSet<int>();

        CallTreeNode Build(MemberRef member, int token, int depth, string? rootKind)
        {
            if (token == 0 || !reverseEdges.TryGetValue(token, out var edges))
            {
                var leafStatus = token == 0 && depth > 0 ? CallTreeStatus.External : CallTreeStatus.Leaf;
                return new CallTreeNode(member, null, leafStatus, [], new CallTreePerf(0, 0, 1, false, null, rootKind));
            }

            var fanin = edges.Count;
            if (depth >= maxDepth)
                return new CallTreeNode(member, null, CallTreeStatus.DepthLimited, [], new CallTreePerf(0, fanin, 1, false, null, rootKind));

            if (!expanded.Add(token))
                return new CallTreeNode(member, null, CallTreeStatus.AlreadyShown, [], new CallTreePerf(0, fanin, 1, false, null, rootKind));

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
                    null));
            }

            var nodeStatus = truncated
                ? CallTreeStatus.Truncated
                : children.Count == 0 ? CallTreeStatus.Leaf : CallTreeStatus.Expanded;
            var maxTreeDepth = children.Count == 0 ? 1 : 1 + children.Max(child => child.Perf?.MaxDepth ?? 1);
            return new CallTreeNode(member, null, nodeStatus, children.ToImmutable(), new CallTreePerf(0, fanin, maxTreeDepth, false, null, rootKind));
        }

        return Build(rootMember, rootMethodToken, 0, root is { } ? (IsEntrypoint(root) ? "entrypoint" : "root") : null);
    }

    static bool IsEntrypoint(MethodIdentity method)
        => string.Equals(method.Name, "Main", StringComparison.Ordinal)
           || string.Equals(method.Name, "<Main>$", StringComparison.Ordinal);

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

        public (ImmutableArray<MethodIdentity> Methods, ImmutableArray<DirectCall> DirectCalls, ImmutableArray<UnsafeEvidence> UnsafeEvidence, ImmutableArray<AnalysisDiagnostic> Diagnostics, UnsafeModeBreakdown UnsafeModes) Build()
        {
            var methods = ImmutableArray.CreateBuilder<MethodIdentity>();
            var calls = ImmutableArray.CreateBuilder<DirectCall>();
            var unsafeEvidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();
            var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
            int none = 0, impl = 0, expl = 0;

            foreach (var typeHandle in _reader.TypeDefinitions)
            {
                var typeDef = _reader.GetTypeDefinition(typeHandle);
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
                new UnsafeModeBreakdown(none, impl, expl));
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
                        calls.Add(new DirectCall(caller, callee, offset, token, ToCallKind(opcode), inLoop));
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
                        calls.Add(new DirectCall(caller, MemberRef.Unsupported($"calli signature token 0x{token:X8}"), offset, token, CallKind.CallIndirect, IsInLoopRegion(offset, loopRegions)));
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
                    if (TryReadBranchTarget(opcode, il, ref position, offset, out int target) && target < offset)
                        regions.Add((target, offset));
                    else
                        SkipOperand(il, opcode, ref position, offset);
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
