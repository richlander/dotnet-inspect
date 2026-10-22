using System.Buffers;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// The unsafe member census rule's role scan
/// (<c>docs/design/unsafe-member-findings.md#role-scan</c>). It decides,
/// without materializing a body, that the full rule finds no instruction role
/// in it and cannot fail it; every other body takes the full rule.
/// </summary>
internal static class UnsafeMemberRoleScan
{
    /// <summary>
    /// Whether the body decodes completely, has no occurrence opcode, and makes
    /// no call the full rule could resolve differently or find explicit.
    /// </summary>
    internal static bool ProvablyRoleFree(scoped MethodDefinitionView view)
    {
        MethodBodyData body = view.GetBodyData();
        var scan = new BodyScan();
        try
        {
            if (InstructionDecoder.VisitBody(body, ref scan)
                != MethodBodyVisitOutcome.Complete)
            {
                return false;
            }
            if (scan.Count == 0)
                return true;

            UnsafeMemberRoleScanFacts facts = view.GetUnsafeMemberRoleScanFacts();
            var scope = new CallerArity(
                view.TypeDefinition.GetGenericParameters().Count,
                view.MethodDefinition.GetGenericParameters().Count);
            for (int i = 0; i < scan.Count; i++)
            {
                if (facts.Defers(view, scope, scan.Opcodes[i], scan.Tokens[i]))
                    return false;
            }
            return true;
        }
        catch (BadImageFormatException)
        {
            // The full rule's decode fails this body too.
            return false;
        }
        finally
        {
            scan.Return();
        }
    }

    // The body's call operands in IL order, in pooled storage; an occurrence
    // opcode stops the visit.
    struct BodyScan : IInstructionVisitor
    {
        public ILOpCode[] Opcodes;
        public int[] Tokens;
        public int Count;

        public bool Visit(ILOpCode opcode, int operandToken, int encodedLength)
        {
            if (MethodSafetyAnalysis.IsOccurrenceOpcode(opcode))
                return false;
            if (opcode is ILOpCode.Call
                or ILOpCode.Callvirt
                or ILOpCode.Newobj
                or ILOpCode.Ldftn
                or ILOpCode.Ldvirtftn)
            {
                Add(opcode, operandToken);
            }
            return true;
        }

        public void Add(ILOpCode opcode, int token)
        {
            if (Opcodes is null || Count == Opcodes.Length)
                Grow();
            Opcodes![Count] = opcode;
            Tokens[Count++] = token;
        }

        void Grow()
        {
            int size = Opcodes is null ? 16 : Opcodes.Length * 2;
            ILOpCode[] opcodes = ArrayPool<ILOpCode>.Shared.Rent(size);
            int[] tokens = ArrayPool<int>.Shared.Rent(size);
            if (Opcodes is not null)
            {
                Opcodes.AsSpan(0, Count).CopyTo(opcodes);
                Tokens.AsSpan(0, Count).CopyTo(tokens);
                Return();
            }
            Opcodes = opcodes;
            Tokens = tokens;
        }

        public readonly void Return()
        {
            if (Opcodes is null)
                return;
            ArrayPool<ILOpCode>.Shared.Return(Opcodes);
            ArrayPool<int>.Shared.Return(Tokens);
        }
    }
}

/// <summary>The generic arities that scope a caller's operand resolution.</summary>
internal readonly record struct CallerArity(int TypeArity, int MethodArity);

/// <summary>
/// The role scan's call-operand facts for one execution, owned by its
/// memory-safety contract support. A call operand's fact depends only on the
/// operand, the caller's generic arities, and whether the operand is called
/// or loaded, so one fact serves every caller with that key.
/// </summary>
internal sealed class UnsafeMemberRoleScanFacts(MetadataReader reader)
{
    readonly Dictionary<(int Token, CallerArity Scope, bool Loads), bool> _defers = [];
    bool? _namesContractAttribute;
    SameImageSignatureComparer? _comparer;

    /// <summary>
    /// Whether a body with this call operand must take the full rule: the
    /// full rule's resolution of it can fail, or it can be an explicit-contract
    /// call.
    /// </summary>
    public bool Defers(
        scoped MethodDefinitionView view,
        CallerArity scope,
        ILOpCode opcode,
        int token)
    {
        bool loads = opcode is ILOpCode.Ldftn or ILOpCode.Ldvirtftn;
        if (!_defers.TryGetValue((token, scope, loads), out bool defers))
        {
            defers = Classify(view, opcode, token, loads);
            _defers[(token, scope, loads)] = defers;
        }
        return defers;
    }

    bool Classify(
        scoped MethodDefinitionView view,
        ILOpCode opcode,
        int token,
        bool loads)
    {
        EntityHandle handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind is not (
                HandleKind.MethodDefinition
                or HandleKind.MemberReference
                or HandleKind.MethodSpecification))
        {
            return true;
        }

        // The full rule resolves the callee and its target for every call
        // site, a loaded function included, so either failure fails it.
        MemberRef callee;
        MethodCallTarget target;
        try
        {
            callee = view.GetCallRecoveryResolver().ResolveMember(token);
            target = view.ResolveCallTarget(KindOf(opcode), token);
        }
        catch (Exception ex) when (AnalysisMethodFailures.IsRecoverable(ex))
        {
            return true;
        }

        if (loads)
            return false;
        if (PlatformCallerUnsafeContracts.Embedded.Contains(callee))
            return true;

        // A same-image target is explicit only through a RequiresUnsafe
        // attribute row, whose constructor names a TypeDef or TypeRef of that
        // name; an image with no such row has no explicit target.
        _namesContractAttribute ??= NamesContractAttribute(reader);
        if (_namesContractAttribute != true)
            return false;

        if (target is MethodCallTarget.CurrentModule current)
            return MayBeExplicit(view, current.Method);

        // A caller-scope check can leave a same-image target unresolved for
        // this caller while another caller with the same key resolves it.
        EntityHandle method = handle.Kind == HandleKind.MethodSpecification
            ? reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method
            : handle;
        EntityHandle definition = MetadataTokens.EntityHandle(
            MemberResolver.DefinitionToken(reader, MetadataTokens.GetToken(method)));
        if (definition.Kind == HandleKind.MethodDefinition)
            return MayBeExplicit(view, definition);

        _comparer ??= new SameImageSignatureComparer(
            reader.IsAssembly
                ? AssemblyReferenceIdentity.FromAssemblyDefinition(reader)
                : null,
            reader.GetString(reader.GetModuleDefinition().Name));
        return _comparer.CanResolveToCurrentModule(callee.DeclaringType);
    }

    static bool MayBeExplicit(
        scoped MethodDefinitionView view,
        EntityHandle definition) =>
        view.GetMemorySafetyContract(definition)
            is not (MemorySafetyMemberContractResult.None
                or MemorySafetyMemberContractResult.Implicit);

    static CallKind KindOf(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Call => CallKind.Call,
        ILOpCode.Callvirt => CallKind.CallVirtual,
        ILOpCode.Newobj => CallKind.NewObject,
        ILOpCode.Ldftn => CallKind.LoadFunction,
        _ => CallKind.LoadVirtualFunction,
    };

    static bool NamesContractAttribute(MetadataReader reader)
    {
        const string Name = "RequiresUnsafeAttribute";
        try
        {
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                if (reader.StringComparer.Equals(reader.GetTypeDefinition(handle).Name, Name))
                    return true;
            }
            foreach (TypeReferenceHandle handle in reader.TypeReferences)
            {
                if (reader.StringComparer.Equals(reader.GetTypeReference(handle).Name, Name))
                    return true;
            }
            return false;
        }
        catch (BadImageFormatException)
        {
            return true;
        }
    }
}
