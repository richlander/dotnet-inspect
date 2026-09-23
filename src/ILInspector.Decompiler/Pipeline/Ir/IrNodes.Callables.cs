using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Instructions;

using Inverse = ILInspector.Decompiler.Pipeline.InverseArchitecture;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// <c>ldftn</c>/<c>ldvirtftn</c>: a method's entry-point address as a native
/// int. C# has no spelling for a bare function-pointer load, so this only
/// reaches print as a comment; the dominant case — feeding a delegate
/// constructor — is raised to <see cref="DelegateCreation"/> by a pass.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.None,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "ldftn·ldvirtftn (method-group / function-pointer load)",
    precondition: "result is `System.IntPtr` — the raw method-address native int; `IsVirtual` selects `ldvirtftn` (dispatched on the receiver) over `ldftn`",
    witness: "function-pointer fixtures; corpus compile-back")]
public sealed class LoadFunctionPointer : IrExpression
{
    public LoadFunctionPointer(MethodRef method, bool isVirtual, IrExpression? instance)
    {
        Method = method;
        IsVirtual = isVirtual;
        if (instance is not null)
            AddChild(instance);
    }

    public MethodRef Method { get; private set; }
    public bool IsVirtual { get; }

    /// <inheritdoc cref="Call.MarkLocalFunctionRaise"/>
    internal void MarkLocalFunctionRaise(LocalFunctionRaiseState state)
        => Method = Method with { LocalFunctionRaise = state };

    /// <summary>The receiver dispatched on for ldvirtftn; null for ldftn.</summary>
    public IrExpression? Instance => Children.Count > 0 ? (IrExpression)Children[0] : null;
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "IntPtr");
    public override IEnumerable<TypeRef> DirectTypes
        => Method.ParameterTypes.Append(Method.DeclaringType).Append(Method.ReturnType);

    public override string Describe()
        => $"{(IsVirtual ? "LoadVirtualFunctionPointer" : "LoadFunctionPointer")} {Method.DeclaringType.ToDisplayString()}.{Method.Name}";
}

/// <summary>
/// <c>&amp;Method</c> — the address of a static method as a function pointer.
/// The renderable form of a static <c>ldftn</c> that did not feed a delegate
/// constructor: it feeds a <c>calli</c>, a native-callback argument, or a
/// <c>delegate*</c>-typed field. Raised from a surviving
/// <see cref="LoadFunctionPointer"/> by <see cref="MethodAddressPass"/>; its
/// result type is the contextual function-pointer type when available, falling
/// back to the managed function-pointer type of the method's signature.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundFunctionPointerLoad,
    naming: Inverse.NameProvenance.Native,
    forwardName: "&method (BoundFunctionPointerLoad) / ldftn",
    precondition: "result is the contextual function-pointer type only when the address is stored to a local or field or returned (`MethodAddressPass` recovers the `delegate*` target's type for those parents); otherwise the managed function-pointer type built from the method's own signature",
    witness: "function-pointer fixtures; corpus compile-back")]
public sealed class AddressOfMethod : IrExpression
{
    public AddressOfMethod(MethodRef method, TypeRef? functionPointerType = null)
    {
        Method = method;
        FunctionPointerType = functionPointerType;
    }

    public MethodRef Method { get; private set; }
    public TypeRef? FunctionPointerType { get; }

    /// <inheritdoc cref="Call.MarkLocalFunctionRaise"/>
    internal void MarkLocalFunctionRaise(LocalFunctionRaiseState state)
        => Method = Method with { LocalFunctionRaise = state };

    public override TypeRef? ResultType
        => FunctionPointerType ?? TypeRef.FunctionPointer(Method.ReturnType, Method.ParameterTypes, "");
    public override IEnumerable<TypeRef> DirectTypes
        => Method.ParameterTypes.Append(Method.DeclaringType).Append(Method.ReturnType).Concat(Method.TypeArguments).Concat(FunctionPointerType is null ? [] : [FunctionPointerType]);

    public override string Describe()
        => $"AddressOfMethod {Method.DeclaringType.ToDisplayString()}.{Method.Name}";
}

/// <summary>
/// A delegate instance from a method group — the inverse of the compiler's
/// <c>ldftn; newobj DelegateType::.ctor(object, native int)</c> lowering. The
/// target is the receiver object (a null constant for a static method group).
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundDelegateCreationExpression,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundDelegateCreationExpression / newobj + ldftn·ldvirtftn",
    precondition: "result is the delegate type `DelegateType` (the `newobj` delegate constructor over a method-group `ldftn`/`ldvirtftn`)",
    witness: "function-pointer/delegate fixtures; corpus compile-back")]
public sealed class DelegateCreation : IrExpression
{
    public DelegateCreation(TypeRef delegateType, MethodRef method, bool isVirtual, IrExpression target,
        MethodRef? constructor = null)
    {
        DelegateType = delegateType;
        Method = method;
        IsVirtual = isVirtual;
        Constructor = constructor;
        AddChild(target);
    }

    public TypeRef DelegateType { get; }
    public MethodRef? Constructor { get; }
    public MethodRef Method { get; private set; }
    public bool IsVirtual { get; }

    /// <summary>
    /// The compiler's lazy static cache was collapsed around this creation.
    /// A target-typed method-group conversion can therefore regenerate the
    /// cache; an ordinary explicit construction must retain <c>new D(...)</c>.
    /// </summary>
    public bool HasCollapsedCompilerCache { get; private set; }

    internal void MarkCollapsedCompilerCache() => HasCollapsedCompilerCache = true;

    /// <inheritdoc cref="Call.MarkLocalFunctionRaise"/>
    internal void MarkLocalFunctionRaise(LocalFunctionRaiseState state)
        => Method = Method with { LocalFunctionRaise = state };

    public IrExpression Target => (IrExpression)Children[0];
    public override TypeRef? ResultType => DelegateType;
    public override IEnumerable<TypeRef> DirectTypes
        => Method.ParameterTypes.Append(Method.DeclaringType).Append(Method.ReturnType).Append(DelegateType);

    public override string Describe()
        => $"DelegateCreation {DelegateType.ToDisplayString()} <- {Method.DeclaringType.ToDisplayString()}.{Method.Name}";
}

/// <summary>
/// A lambda expression <c>(params) =&gt; body</c> recovered from the compiler's
/// closure lowering — the inverse of the delegate-over-synthesized-method shape
/// ClosureConversion emits. Carries the lambda's parameters and its raised body
/// (the synthesized method's block container, imported and run through the
/// pipeline). The result type is the delegate type the lambda is converted to.
///
/// <para>Non-capturing bodies may carry their own locals; the printer switches
/// to this nested scope when needed. Capturing bodies still print in the outer
/// function scope after capture substitution.</para>
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundLambda,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundLambda / closure lowering (synthesized method + delegate)",
    precondition: "result is the delegate type `DelegateType` the lambda is converted to; raised from the compiler's closure/display-class lowering",
    witness: "lambda/closure fixtures; corpus compile-back")]
public sealed class Lambda : IrExpression
{
    ImmutableArray<string> _capturedBinderNames = [];

    public Lambda(
        TypeRef delegateType,
        ImmutableArray<Parameter> parameters,
        ImmutableArray<TypeRef> locals,
        ImmutableArray<string?> localNames,
        bool usesUpdatedMemorySafetyRules,
        bool skipLocalsInit,
        BlockContainer body)
    {
        DelegateType = delegateType;
        Parameters = parameters;
        Locals = locals;
        LocalNames = localNames;
        UsesUpdatedMemorySafetyRules = usesUpdatedMemorySafetyRules;
        SkipLocalsInit = skipLocalsInit;
        AddChild(body);
    }

    public TypeRef DelegateType { get; }
    internal bool NeedsIsolatedLocalScope => !Locals.IsEmpty
        || Body.Descendants.Any(node => node is LoadStackSlot or StoreStackSlot);
    public ImmutableArray<Parameter> Parameters { get; }
    /// <summary>
    /// Ref-kind evidence for explicitly typed lambda parameters. Empty when no
    /// parameter is by-ref. <c>LambdaRaisingPassTests</c> gates preservation.
    /// </summary>
    public ImmutableArray<ArgumentRefKind> ParameterRefKinds { get; init; } = [];
    public ImmutableArray<TypeRef> Locals { get; }
    public ImmutableArray<string?> LocalNames { get; }
    public ImmutableArray<string?> SynthesizedLocalNames { get; init; } = [];
    public ImmutableArray<bool> LocalDeclaredInNestedScope { get; init; } = [];
    public ImmutableArray<PdbLocalDeclaration?> LocalDeclarationBindings { get; init; } = [];
    public ImmutableArray<string?> PdbLocalNameCandidates { get; init; } = [];
    public ImmutableArray<DecompilerFidelityCause> LocalNameImportCauses { get; init; } = [];
    internal ImmutableDictionary<int, MaterializedStackSlotLocal>
        MaterializedStackSlotLocals { get; init; } =
            ImmutableDictionary<int, MaterializedStackSlotLocal>.Empty;
    /// <summary>
    /// Enclosing binders that the final raised body references after
    /// capture substitution. Explicit non-parameter capture evidence is combined
    /// with parameter-owned argument references from the transplanted body.
    /// </summary>
    public ImmutableArray<string> CapturedBinderNames
    {
        get => [
            .. _capturedBinderNames
                .Concat(CSharpSpellability.ExternalArgumentNamesInScope(Body, Parameters))
                .Distinct(StringComparer.Ordinal),
        ];
        init => _capturedBinderNames = value;
    }
    public bool UsesUpdatedMemorySafetyRules { get; }
    public bool SkipLocalsInit { get; }
    public BlockContainer Body => (BlockContainer)Children[0];
    public override TypeRef? ResultType => DelegateType;

    /// <summary>
    /// The lambda was recovered from a <c>System.Linq.Expressions</c> factory
    /// graph (<see cref="ExpressionTreeLambdaRaisingPass"/>), so it is an
    /// <em>expression-tree</em> lambda: the C# compiler at the consuming site
    /// builds an <see cref="System.Linq.Expressions.Expression{TDelegate}"/> from
    /// it under that project's own overflow-checking default. The matched graph
    /// used the unchecked <c>Expression.Add/Subtract/Multiply</c> factories, so the
    /// printer must spell overflow-prone arithmetic inside an explicit
    /// <c>unchecked(...)</c> — otherwise a project compiled with
    /// <c>CheckForOverflowUnderflow</c> would rebuild the tree with the checked
    /// <c>AddChecked</c> node, silently changing the tree identity the rewrite
    /// claims to preserve. False for ordinary delegate lambdas, whose printing is
    /// unaffected.
    /// </summary>
    public bool IsExpressionTree { get; init; }

    /// <summary>
    /// The synthesized method this lambda was recovered from returns
    /// <c>System.Void</c>. This is explicit because custom delegate types do not
    /// expose their Invoke signature through <see cref="DelegateType"/> alone.
    /// </summary>
    public bool ReturnsVoid { get; init; }

    public override IEnumerable<TypeRef> DirectTypes
        => Parameters.Select(p => p.Type).Append(DelegateType);

    public void ResetBody(BlockContainer body)
    {
        DetachChildren();
        AddChild(body);
    }

    /// <summary>
    /// The single returned expression, or the single side-effect expression of
    /// a void body after its implicit trailing return has been removed — the
    /// expression-bodied form <c>p =&gt; expr</c>. Null when the body needs the
    /// block form <c>p =&gt; { ... }</c>.
    /// </summary>
    public IrExpression? ExpressionBody
        => Body.Blocks switch
        {
            [{ Children: [Return { Value: { } value }] }] => value,
            [{ Children: [ExpressionStatement { Expression: var expression }] }]
                when ReturnsVoid && expression.ResultType is { Namespace: "System", Name: "Void" } => expression,
            _ => null,
        };

    public override string Describe()
        => $"Lambda {DelegateType.ToDisplayString()} ({Parameters.Length} params)";
}

/// <summary>
/// A recovered local-function declaration — the inverse of the compiler lowering
/// a <c>Name(...) { ... }</c> local function to a synthesized
/// <c>&lt;Enclosing&gt;g__Name|N_M</c> method. Holds the source name, signature,
/// and raised body; the printer emits it as a nested method declaration in the
/// host body (call sites become <see cref="LocalFunctionInvocation"/>).
/// </summary>
public sealed class LocalFunctionStatement : IrNode
{
    ImmutableArray<string> _capturedBinderNames = [];

    internal MetadataMethodAddress? SourceMethodAddress { get; init; }

    public LocalFunctionStatement(
        string name,
        TypeRef returnType,
        ImmutableArray<Parameter> parameters,
        bool isStatic,
        ImmutableArray<TypeRef> locals,
        ImmutableArray<string?> localNames,
        bool usesUpdatedMemorySafetyRules,
        bool skipLocalsInit,
        BlockContainer body,
        bool requiresUnsafe = false)
        : this(
            name,
            returnType,
            parameters,
            [],
            isStatic,
            locals,
            localNames,
            usesUpdatedMemorySafetyRules,
            skipLocalsInit,
            body,
            requiresUnsafe)
    {
    }

    public LocalFunctionStatement(
        string name,
        TypeRef returnType,
        ImmutableArray<Parameter> parameters,
        ImmutableArray<ArgumentRefKind> parameterRefKinds,
        bool isStatic,
        ImmutableArray<TypeRef> locals,
        ImmutableArray<string?> localNames,
        bool usesUpdatedMemorySafetyRules,
        bool skipLocalsInit,
        BlockContainer body,
        bool requiresUnsafe = false)
    {
        Name = name;
        ReturnType = returnType;
        Parameters = parameters;
        ParameterRefKinds = parameterRefKinds;
        IsStatic = isStatic;
        Locals = locals;
        LocalNames = localNames;
        UsesUpdatedMemorySafetyRules = usesUpdatedMemorySafetyRules;
        SkipLocalsInit = skipLocalsInit;
        RequiresUnsafe = requiresUnsafe;
        AddChild(body);
    }

    public string Name { get; }
    public TypeRef ReturnType { get; }
    internal bool NeedsIsolatedLocalScope => !Locals.IsEmpty
        || Body.Descendants.Any(node => node is LoadStackSlot or StoreStackSlot);
    public ImmutableArray<Parameter> Parameters { get; }
    public ImmutableArray<ArgumentRefKind> ParameterRefKinds { get; }
    public bool IsStatic { get; }
    public ImmutableArray<TypeRef> Locals { get; }
    public ImmutableArray<string?> LocalNames { get; }
    public ImmutableArray<string?> SynthesizedLocalNames { get; init; } = [];
    public ImmutableArray<bool> LocalDeclaredInNestedScope { get; init; } = [];
    public ImmutableArray<PdbLocalDeclaration?> LocalDeclarationBindings { get; init; } = [];
    public ImmutableArray<string?> PdbLocalNameCandidates { get; init; } = [];
    public ImmutableArray<DecompilerFidelityCause> LocalNameImportCauses { get; init; } = [];
    internal ImmutableDictionary<int, MaterializedStackSlotLocal>
        MaterializedStackSlotLocals { get; init; } =
            ImmutableDictionary<int, MaterializedStackSlotLocal>.Empty;
    /// <summary>
    /// Enclosing binders that the final raised body references after
    /// capture substitution. Explicit non-parameter capture evidence is combined
    /// with parameter-owned argument references from the transplanted body.
    /// </summary>
    public ImmutableArray<string> CapturedBinderNames
    {
        get => [
            .. _capturedBinderNames
                .Concat(CSharpSpellability.ExternalArgumentNamesInScope(Body, Parameters))
                .Distinct(StringComparer.Ordinal),
        ];
        init => _capturedBinderNames = value;
    }
    public bool UsesUpdatedMemorySafetyRules { get; }
    public bool SkipLocalsInit { get; }
    public bool RequiresUnsafe { get; }
    public BlockContainer Body => (BlockContainer)Children[0];

    public override IEnumerable<TypeRef> DirectTypes => Parameters.Select(p => p.Type).Append(ReturnType);

    public void ResetBody(BlockContainer body)
    {
        DetachChildren();
        AddChild(body);
    }

    /// <summary>The single returned expression when the body is one block ending in a bare <c>return expr;</c>.</summary>
    public IrExpression? ExpressionBody
        => Body.Blocks is [{ Children: [Return { Value: { } value }] }] ? value : null;

    public override string Describe() => $"LocalFunctionStatement {Name} ({Parameters.Length} params)";
}

/// <summary>
/// A call to a recovered local function — rendered as the unqualified
/// <c>Name(args)</c>, the source spelling, rather than the synthesized
/// <c>Enclosing.&lt;Outer&gt;g__Name|N_M(args)</c> the call site lowered to.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundCall,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundCall (local function) / call",
    precondition: "result is the local function's return type `ReturnType` (a `call` to a compiler-lowered local-function method, recognized and rendered as a local-function call)",
    witness: "local-function fixtures; corpus compile-back")]
public sealed class LocalFunctionInvocation : IrExpression
{
    public LocalFunctionInvocation(string name, TypeRef returnType, IEnumerable<IrExpression> arguments)
        : this(name, returnType, arguments, [], [], requiresUnsafe: false)
    {
    }

    public LocalFunctionInvocation(
        string name,
        TypeRef returnType,
        IEnumerable<IrExpression> arguments,
        ImmutableArray<TypeRef> parameterTypes,
        ImmutableArray<ArgumentRefKind> parameterRefKinds,
        bool requiresUnsafe = false)
    {
        Name = name;
        ReturnType = returnType;
        ParameterTypes = parameterTypes;
        ParameterRefKinds = parameterRefKinds;
        RequiresUnsafe = requiresUnsafe;
        foreach (var argument in arguments)
            AddChild(argument);
    }

    public string Name { get; }
    public TypeRef ReturnType { get; }
    public ImmutableArray<TypeRef> ParameterTypes { get; }
    public ImmutableArray<ArgumentRefKind> ParameterRefKinds { get; }
    public bool RequiresUnsafe { get; }
    public IReadOnlyList<IrExpression> Arguments => Children.Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => ReturnType;
    public override IEnumerable<TypeRef> DirectTypes => ParameterTypes.Append(ReturnType);

    public override string Describe() => $"LocalFunctionInvocation {Name}";
}
