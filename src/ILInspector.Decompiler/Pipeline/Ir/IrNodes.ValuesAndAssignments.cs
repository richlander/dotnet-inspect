using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Instructions;

using Inverse = ILInspector.Decompiler.Pipeline.InverseArchitecture;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>An expression evaluated for its side effects (void call, popped value).</summary>
public sealed class ExpressionStatement : IrNode
{
    public ExpressionStatement(IrExpression expression) => AddChild(expression);

    public IrExpression Expression => (IrExpression)Children[0];

    public override string Describe() => "ExpressionStatement";
}

/// <summary>A method argument (or <c>this</c>) read — the <c>ldarg</c> family.</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundParameter,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundParameter / ldarg",
    precondition: "type is the argument's declared type (parameter signature; declaring type for `this`)",
    witness: "corpus compile-back")]
public sealed class LoadArgument : IrExpression
{
    readonly string _name = "";

    public LoadArgument(int index, string name, TypeRef type)
    {
        Index = index;
        _name = name;
        Type = type;
    }

    internal LoadArgument(int index, string name, TypeRef type, Parameter? parameter)
        : this(index, name, type)
    {
        Parameter = parameter;
    }

    public LoadArgument(int index, Parameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        Index = index;
        _name = parameter.Name;
        Parameter = parameter;
        Type = parameter.Type;
    }

    public int Index { get; }
    internal Parameter? Parameter { get; }
    public string Name => Parameter?.DisplayName ?? _name;
    public TypeRef Type { get; }

    /// <summary>
    /// True when this parameter's top-level type was authored as <c>dynamic</c>
    /// (a <c>System.Object</c> position carrying <c>[DynamicAttribute]</c>). The
    /// printer uses this to drop a redundant <c>(dynamic)</c> cast when this load
    /// is the receiver of a raised dynamic member access.
    /// </summary>
    public bool IsDynamic { get; init; }
    public MetadataFactState ArrayElementIsDynamic { get; init; } = MetadataFactState.Unknown;
    public override TypeRef? ResultType => Type;

    public override string Describe() => $"LoadArgument {Index} ({Type.ToDisplayString()} {Name})";
}

public sealed class StoreArgument : ScalarStore
{
    readonly string _name = "";

    public StoreArgument(int index, string name, TypeRef type, IrExpression value)
    {
        Index = index;
        _name = name;
        Type = type;
        AddChild(value);
    }

    internal StoreArgument(
        int index,
        string name,
        TypeRef type,
        IrExpression value,
        Parameter? parameter)
        : this(index, name, type, value)
    {
        Parameter = parameter;
    }

    public StoreArgument(int index, Parameter parameter, IrExpression value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        Index = index;
        _name = parameter.Name;
        Parameter = parameter;
        Type = parameter.Type;
        AddChild(value);
    }

    public int Index { get; }
    internal Parameter? Parameter { get; }
    public string Name => Parameter?.DisplayName ?? _name;
    public TypeRef Type { get; }
    public override IrExpression Value => (IrExpression)Children[0];
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"StoreArgument {Index} ({Type.ToDisplayString()} {Name}){UpdateDescription}";
}

/// <summary>A local variable read — the <c>ldloc</c> family.</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundLocal,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundLocal / ldloc",
    precondition: "type is the local's declared type (local variable signature)",
    witness: "corpus compile-back")]
public sealed class LoadLocal : IrExpression
{
    public LoadLocal(int index, TypeRef type)
    {
        Index = index;
        Type = type;
    }

    public int Index { get; }
    public TypeRef Type { get; }
    public override TypeRef? ResultType => Type;

    public override string Describe() => $"LoadLocal {Index} ({Type.ToDisplayString()})";
}

/// <summary>
/// Owner-issued result of the carrier-to-logical-local storage proof. Final
/// declaration planning consumes this provenance without repeating that proof.
/// </summary>
internal sealed record PdbScopeEntryLocalProjection(
    int CarrierIndex,
    PdbLocalDeclaration Declaration);

public sealed class StoreLocal : ScalarStore
{
    public StoreLocal(int index, TypeRef type, IrExpression value)
    {
        Index = index;
        Type = type;
        AddChild(value);
    }

    public int Index { get; }
    public TypeRef Type { get; }
    internal PdbScopeEntryLocalProjection? PdbScopeEntryProjection { get; init; }
    public override IrExpression Value => (IrExpression)Children[0];
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"StoreLocal {Index} ({Type.ToDisplayString()}){UpdateDescription}";
}

[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundLiteral,
    naming: Inverse.NameProvenance.Inherited,
    oracle: Inverse.Oracle.RyuJitStackNormalization,
    forwardName: "BoundLiteral / ldc.i4·ldc.i8·ldc.r4·ldc.r8·ldstr·ldnull",
    precondition: "`Type` is the IL constant's stack type, or the sink-reconciled semantic type within the same stack family (bool/char/enum identity recovery — TypedConstantsPass; the storage width of a bool/char constant is never its semantic type); the value bits are preserved exactly",
    witness: "TypedConstantsPassTests, EnumCastPrinterTests, corpus render-text A/B")]
public sealed class Constant : IrExpression
{
    public Constant(object? value, TypeRef type)
    {
        Value = value;
        Type = type;
    }

    public object? Value { get; }
    public TypeRef Type { get; }
    public override TypeRef? ResultType => Type;

    public override string Describe() => Value switch
    {
        null => "Constant null",
        string s => $"Constant \"{s}\" (string)",
        _ => $"Constant {Value} ({Type.ToDisplayString()})",
    };
}

public enum BinaryKind { Add, Subtract, Multiply, Divide, Remainder, And, Or, Xor, ShiftLeft, ShiftRight }

/// <summary>An arithmetic, bitwise, or shift operator (<c>add</c>/<c>sub</c>/<c>and</c>/<c>shl</c> …).</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundBinaryOperator,
    naming: Inverse.NameProvenance.Inherited,
    oracle: Inverse.Oracle.RyuJitImporter,
    forwardName: "BoundBinaryOperator (arithmetic/bitwise/shift)",
    precondition: "result type is the ECMA binary-numeric result of the operand stack types",
    witness: "corpus compile-back")]
public sealed class Binary : IrExpression
{
    public Binary(BinaryKind kind, bool isChecked, bool isUnsigned, IrExpression left, IrExpression right)
    {
        Kind = kind;
        IsChecked = isChecked;
        IsUnsigned = isUnsigned;
        AddChild(left);
        AddChild(right);
    }

    public BinaryKind Kind { get; }
    public bool IsChecked { get; }
    public bool IsUnsigned { get; }
    public IrExpression Left => (IrExpression)Children[0];
    public IrExpression Right => (IrExpression)Children[1];

    /// <summary>ECMA-335 III.1.5 binary numeric promotion: the wider operand wins (see <see cref="TypeFamilies.BinaryResult"/>).</summary>
    public override TypeRef? ResultType => TypeFamilies.BinaryResult(Left.ResultType, Right.ResultType);

    public override string Describe()
        => $"Binary.{Kind}{(IsChecked ? " checked" : "")}{(IsUnsigned ? " unsigned" : "")}";
}

[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundCall,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundCall / call·callvirt",
    precondition: "result is the callee's return type (method signature); `IsVirtual` selects `callvirt`, `ConstrainedTo` carries a `constrained.` prefix",
    witness: "corpus compile-back")]
public sealed class Call : IrExpression
{
    public Call(MethodRef callee, bool isVirtual, IEnumerable<IrExpression> arguments)
    {
        Callee = callee;
        IsVirtual = isVirtual;
        foreach (var argument in arguments)
            AddChild(argument);
    }

    public MethodRef Callee { get; private set; }
    public bool IsVirtual { get; }

    /// <summary>
    /// Stamps <see cref="MethodRef.LocalFunctionRaise"/> on this call's callee.
    /// Only <see cref="ILInspector.Decompiler.Pipeline.LocalFunctionRaisingPass"/> may
    /// call this, once it has run and left the call unraised.
    /// </summary>
    internal void MarkLocalFunctionRaise(LocalFunctionRaiseState state)
        => Callee = Callee with { LocalFunctionRaise = state };

    /// <summary>The constrained. prefix type for constrained callvirt; null otherwise.</summary>
    public TypeRef? ConstrainedTo { get; init; }
    /// <summary>Arguments including the receiver for instance calls.</summary>
    public IReadOnlyList<IrExpression> Arguments => Children.Cast<IrExpression>().ToList();

    /// <summary>
    /// Whether a same-named member in the receiver hierarchy makes reduced
    /// extension syntax unsafe without full C# binding.
    /// </summary>
    /// <remarks>
    /// Gated by the conflict cases in <c>ExtensionMethodCallTests</c>.
    /// </remarks>
    public MetadataFactState ExtensionSyntaxConflict { get; init; }

    /// <summary>
    /// A by-ref argument is forwarded against an unknown call-site ref-kind —
    /// the printer spells a keyword it cannot verify. Lowers fidelity. See
    /// <see cref="MethodRef.HasUnverifiableByRefArgument"/>.
    /// </summary>
    public bool HasUnverifiedByRefArgument
        => Callee.HasUnverifiableByRefArgument(Callee.HasThis ? [.. Arguments.Skip(1)] : Arguments);

    public override TypeRef? ResultType => Callee.ReturnType;
    public override IEnumerable<TypeRef> DirectTypes
        => Callee.ParameterTypes.Concat(Callee.TypeArguments).Append(Callee.DeclaringType).Append(Callee.ReturnType)
            .Concat(ConstrainedTo is null ? [] : [ConstrainedTo]);

    public override string Describe()
        => $"{(IsVirtual ? "CallVirt" : "Call")} {Callee.DeclaringType.ToDisplayString()}.{Callee.Name}";
}

/// <summary>
/// <c>calli</c>: an indirect call through a function-pointer value. The
/// pointer (a <c>delegate*&lt;...&gt;</c>-typed expression) is the first child;
/// the arguments follow. The standalone call-site signature supplies the
/// return and parameter types, so the node is self-describing without the
/// pointer's own type. Renders as a C# function-pointer invocation
/// <c>pointer(args)</c>.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundFunctionPointerInvocation,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundFunctionPointerInvocation / calli",
    precondition: "result is the `calli` standalone signature's return type; parameter types and calling convention come from that signature, not a resolved method (`IsInstance` marks a receiver absent from the parameter list)",
    witness: "function-pointer fixtures; corpus compile-back")]
public sealed class CallIndirect : IrExpression
{
    public CallIndirect(IrExpression pointer, IEnumerable<IrExpression> arguments, TypeRef returnType, ImmutableArray<TypeRef> parameterTypes)
    {
        AddChild(pointer);
        foreach (var argument in arguments)
            AddChild(argument);
        ReturnType = returnType;
        ParameterTypes = parameterTypes;
        ParameterRefKinds = TypeRef.FunctionPointerParameterRefKindsFor(parameterTypes);
    }

    public TypeRef ReturnType { get; }
    public ImmutableArray<TypeRef> ParameterTypes { get; }
    public ImmutableArray<ArgumentRefKind> ParameterRefKinds { get; }

    /// <summary>
    /// The C# calling-convention spelling carried by the <c>calli</c> standalone
    /// signature (empty = managed, else <c>unmanaged</c>/<c>unmanaged[Cdecl]</c>…),
    /// in the same form as <see cref="TypeRef.CallingConvention"/> on a
    /// <c>delegate*</c> type, so the two can be compared for a spellable invocation.
    /// </summary>
    public string CallingConvention { get; init; } = "";

    /// <summary>
    /// Whether the call-site signature is an instance function pointer (the
    /// receiver is in <see cref="Arguments"/> but absent from
    /// <see cref="ParameterTypes"/>). C# has no instance <c>delegate*</c> spelling.
    /// </summary>
    public bool IsInstance { get; init; }

    /// <summary>The function-pointer value being invoked.</summary>
    public IrExpression Pointer => (IrExpression)Children[0];
    /// <summary>Call arguments (the function pointer's own parameters, receiver included when the signature carries one).</summary>
    public IReadOnlyList<IrExpression> Arguments => Children.Skip(1).Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => ReturnType;
    public override IEnumerable<TypeRef> DirectTypes => ParameterTypes.Append(ReturnType);

    public override string Describe() => $"CallIndirect {ReturnType.ToDisplayString()}";
}

/// <summary>Object construction: <c>newobj</c> with the constructor's MethodRef (receiver excluded from arguments).</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundObjectCreationExpression,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundObjectCreationExpression / newobj",
    precondition: "result is the constructed type (the `newobj` constructor's declaring type)",
    witness: "corpus compile-back")]
public sealed class NewObject : IrExpression
{
    public NewObject(MethodRef constructor, IEnumerable<IrExpression> arguments)
    {
        Constructor = constructor;
        foreach (var argument in arguments)
            AddChild(argument);
    }

    public MethodRef Constructor { get; }
    public IReadOnlyList<IrExpression> Arguments => Children.Cast<IrExpression>().ToList();

    /// <summary>
    /// For a constructor of a compiler-generated anonymous type
    /// (<c>&lt;&gt;f__AnonymousType*</c>), the property names in argument order —
    /// the metadata the importer captures so <see cref="AnonymousObjectPass"/> can
    /// raise the call to a <c>new { Name = value, ... }</c> literal. Empty for
    /// every ordinary constructor; a pass keys off non-emptiness.
    /// </summary>
    public ImmutableArray<string> AnonymousPropertyNames { get; init; } = [];

    /// <summary>
    /// A by-ref constructor argument forwarded against an unknown call-site
    /// ref-kind. Lowers fidelity. See
    /// <see cref="MethodRef.HasUnverifiableByRefArgument"/>.
    /// </summary>
    public bool HasUnverifiedByRefArgument => Constructor.HasUnverifiableByRefArgument(Arguments);

    public override TypeRef? ResultType => Constructor.DeclaringType;
    public override IEnumerable<TypeRef> DirectTypes
        => Constructor.ParameterTypes.Append(Constructor.DeclaringType);

    public override string Describe() => $"NewObject {Constructor.DeclaringType.ToDisplayString()}";
}

/// <summary>
/// A raised C# anonymous-object creation — <c>new { a = x, b = y }</c> — produced
/// by <see cref="AnonymousObjectPass"/> from the compiler's lowering of an
/// anonymous-type construction to <c>new &lt;&gt;f__AnonymousType0&lt;...&gt;(x, y)</c>.
/// The child slots are the member value expressions; <see cref="PropertyNames"/>
/// is the parallel name list (argument order), carried as metadata rather than
/// child nodes since names are not expressions.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundAnonymousObjectCreationExpression,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundAnonymousObjectCreationExpression (new { a = x })",
    precondition: "`Type` is the compiler's <>f__AnonymousType construction the raise replaced; `PropertyNames` parallels the constructor argument order",
    witness: "AnonymousObjectPassTests, corpus compile-back")]
public sealed class AnonymousObject : IrExpression
{
    public AnonymousObject(TypeRef type, ImmutableArray<string> propertyNames, IEnumerable<IrExpression> values,
        MethodRef? constructor = null)
    {
        Type = type;
        PropertyNames = propertyNames;
        Constructor = constructor;
        foreach (var value in values)
            AddChild(value);
    }

    public TypeRef Type { get; }
    public ImmutableArray<string> PropertyNames { get; }
    public MethodRef? Constructor { get; }
    public IReadOnlyList<IrExpression> Values => Children.Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => Type;
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"AnonymousObject ({Children.Count} properties)";
}

/// <summary>
/// The alignment and/or format clause of an interpolated-string hole — the
/// <c>,alignment</c> and <c>:format</c> suffixes recovered from the
/// <c>AppendFormatted</c> overload's extra arguments. <see cref="HasAlignment"/>
/// distinguishes an absent alignment from a present zero, so the recovered hole
/// round-trips to the same handler overload.
/// </summary>
public sealed record InterpolationFormat(int Alignment, bool HasAlignment, string? FormatString);

/// <summary>One segment in a raised interpolated string: either literal text or a formatted-expression child by index, with an optional alignment/format clause.</summary>
public sealed record InterpolatedStringPart(string? Literal, int ExpressionIndex, InterpolationFormat? Format = null)
{
    public static InterpolatedStringPart LiteralText(string text) => new(text, -1);
    public static InterpolatedStringPart FormattedValue(int expressionIndex, InterpolationFormat? format = null) => new(null, expressionIndex, format);
    public bool IsLiteral => Literal is not null;
}

/// <summary>
/// A raised C# interpolated string. Produced by
/// <see cref="StringInterpolationPass"/> from csc's straight-line
/// <c>DefaultInterpolatedStringHandler</c> lowering.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundInterpolatedString,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundInterpolatedString ($\"...\")",
    precondition: "result is `System.String`; raised from csc's straight-line DefaultInterpolatedStringHandler lowering; `HasAlignment` distinguishes an absent alignment from a present zero so each hole round-trips to the same AppendFormatted overload",
    witness: "StringInterpolationPassTests, corpus compile-back")]
public sealed class InterpolatedStringExpression : IrExpression
{
    public InterpolatedStringExpression(IEnumerable<InterpolatedStringPart> parts,
        IEnumerable<IrExpression> formattedValues, ImmutableArray<MethodRef> consumedMemberRefs = default)
    {
        Parts = [.. parts];
        ConsumedMemberRefs = consumedMemberRefs.IsDefault ? [] : consumedMemberRefs;
        foreach (var value in formattedValues)
            AddChild(value);
    }

    public ImmutableArray<InterpolatedStringPart> Parts { get; }
    /// <summary>The constructor, one append per part, and final conversion, in source order.</summary>
    public ImmutableArray<MethodRef> ConsumedMemberRefs { get; }
    public IReadOnlyList<IrExpression> FormattedValues => Children.Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "String");

    public override string Describe() => $"InterpolatedString ({Parts.Length} parts)";
}

/// <summary>
/// A raised C# tuple literal, produced by <see cref="TupleCreationPass"/> from
/// a direct <c>System.ValueTuple&lt;...&gt;</c> constructor call. The binary only
/// records the element values and the underlying ValueTuple type; tuple element
/// names are a signature/custom-attribute concern and are not recovered here.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundTupleLiteral,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundConvertedTupleLiteral ((a, b, ...))",
    precondition: "result is the underlying `System.ValueTuple<...>` constructed type of the constructor call the raise replaced; element names are a signature/attribute concern and are not recovered here",
    witness: "TupleCreationPassTests, corpus compile-back")]
public sealed class TupleExpression : IrExpression
{
    public TupleExpression(TypeRef tupleType, IEnumerable<IrExpression> elements)
    {
        TupleType = tupleType;
        foreach (var element in elements)
            AddChild(element);
    }

    public TypeRef TupleType { get; }
    public IReadOnlyList<IrExpression> Elements => Children.Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => TupleType;
    public override IEnumerable<TypeRef> DirectTypes => [TupleType];

    public override string Describe() => $"TupleExpression ({Children.Count} elements)";
}

/// <summary>
/// A raised C# tuple binary operator, produced from csc's hidden ValueTuple
/// operand spills and element-wise comparison lowering.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundTupleBinaryOperator,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundTupleBinaryOperator (tuple ==/!=)",
    precondition: "result is `System.Boolean`; raised only from csc's hidden ValueTuple operand spills plus element-wise comparison lowering; `TupleType` is the operands' ValueTuple type",
    witness: "TupleBinaryOperatorPassTests, corpus compile-back")]
public sealed class TupleBinaryExpression : IrExpression
{
    public TupleBinaryExpression(bool isEquality, TypeRef tupleType, IrExpression left, IrExpression right)
    {
        IsEquality = isEquality;
        TupleType = tupleType;
        AddChild(left);
        AddChild(right);
    }

    public bool IsEquality { get; }
    public TypeRef TupleType { get; }
    public IrExpression Left => (IrExpression)Children[0];
    public IrExpression Right => (IrExpression)Children[1];
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Boolean");
    public override IEnumerable<TypeRef> DirectTypes => [TupleType];

    public override string Describe() => IsEquality ? "TupleBinary ==" : "TupleBinary !=";
}

public enum DeconstructionTargetKind
{
    Local,
    Property,
    Argument,
    Field,
}

/// <summary>A target inside a raised tuple deconstruction.</summary>
public sealed class DeconstructionTarget : IrNode
{
    string _argumentName = "";

    DeconstructionTarget(DeconstructionTargetKind kind, TypeRef type)
    {
        Kind = kind;
        Type = type;
    }

    public static DeconstructionTarget Local(int index, TypeRef type, bool isDeclared)
        => new(DeconstructionTargetKind.Local, type)
        {
            LocalIndex = index,
            IsDeclared = isDeclared,
        };

    public static DeconstructionTarget Argument(int index, string name, TypeRef type)
        => new(DeconstructionTargetKind.Argument, type)
        {
            ArgumentIndex = index,
            ArgumentName = name,
        };

    internal static DeconstructionTarget Argument(
        int index,
        string name,
        TypeRef type,
        Parameter? parameter)
        => new(DeconstructionTargetKind.Argument, type)
        {
            ArgumentIndex = index,
            ArgumentName = name,
            ArgumentParameter = parameter,
        };

    public static DeconstructionTarget Argument(int index, Parameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return new(DeconstructionTargetKind.Argument, parameter.Type)
        {
            ArgumentIndex = index,
            ArgumentParameter = parameter,
        };
    }

    public static DeconstructionTarget FieldTarget(FieldRef field, bool isThisInstance)
        => new(DeconstructionTargetKind.Field, field.Type)
        {
            Field = field,
            IsThisInstance = isThisInstance,
        };

    public static DeconstructionTarget Property(MethodRef accessor, IrExpression? instance, IReadOnlyList<IrExpression> indexArguments, bool isVirtual)
    {
        var valueType = accessor.ParameterTypes.Length > 0
            ? accessor.ParameterTypes[^1]
            : TypeRef.CoreLib("System", "Void");
        var target = new DeconstructionTarget(DeconstructionTargetKind.Property, valueType)
        {
            Accessor = accessor,
            HasInstance = instance is not null,
            IsVirtual = isVirtual,
        };
        if (instance is not null)
            target.AddChild(instance);
        foreach (var argument in indexArguments)
            target.AddChild(argument);
        return target;
    }

    public DeconstructionTargetKind Kind { get; }
    public TypeRef Type { get; }
    public int LocalIndex { get; private init; } = -1;
    public bool IsDeclared { get; private init; }
    public MethodRef? Accessor { get; private init; }
    public bool HasInstance { get; private init; }
    public bool IsVirtual { get; private init; }
    public int ArgumentIndex { get; private init; } = -1;
    internal Parameter? ArgumentParameter { get; private init; }
    public string ArgumentName
    {
        get => ArgumentParameter?.DisplayName ?? _argumentName;
        private init => _argumentName = value;
    }
    public FieldRef? Field { get; private init; }
    public bool IsThisInstance { get; private init; }
    public string PropertyName => Accessor?.Name["set_".Length..] ?? "";
    public IrExpression? Instance => Kind == DeconstructionTargetKind.Property && HasInstance ? (IrExpression)Children[0] : null;
    public IReadOnlyList<IrExpression> IndexArguments
        => Kind == DeconstructionTargetKind.Property
            ? Children.Skip(HasInstance ? 1 : 0).Cast<IrExpression>().ToList()
            : [];
    public override IEnumerable<TypeRef> DirectTypes
        => Kind == DeconstructionTargetKind.Property && Accessor is { } accessor
            ? accessor.ParameterTypes.Append(accessor.DeclaringType)
            : Kind == DeconstructionTargetKind.Field && Field is { } fieldRef
                ? [fieldRef.DeclaringType, fieldRef.Type]
            : [Type];

    public override string Describe() => Kind switch
    {
        DeconstructionTargetKind.Local => IsDeclared
            ? $"DeconstructionTarget local declaration {LocalIndex}"
            : $"DeconstructionTarget local assignment {LocalIndex}",
        DeconstructionTargetKind.Property => $"DeconstructionTarget property {Accessor!.DeclaringType.ToDisplayString()}.{PropertyName}",
        DeconstructionTargetKind.Argument => $"DeconstructionTarget argument {ArgumentName}",
        DeconstructionTargetKind.Field => $"DeconstructionTarget field {Field!.DeclaringType.ToDisplayString()}.{Field.Name}",
        _ => "DeconstructionTarget",
    };
}

/// <summary>
/// A raised tuple deconstruction, produced by
/// <see cref="DeconstructionAssignmentPass"/> from the compiler's
/// <c>ValueTuple</c> receiver spill followed by sequential <c>ItemN</c> stores.
/// Local targets may be declarations or assignments; property, parameter, and
/// field targets are assignments into an existing place.
/// </summary>
public sealed class DeconstructionAssignment : IrNode
{
    public DeconstructionAssignment(
        ImmutableArray<int> localIndices,
        ImmutableArray<TypeRef> localTypes,
        IrExpression source,
        ImmutableArray<bool> isDeclared,
        MethodRef? consumedDeconstructMethod = null)
        : this([.. localIndices.Select((index, i) => DeconstructionTarget.Local(index, localTypes[i], isDeclared[i]))], source, consumedDeconstructMethod)
    {
    }

    public DeconstructionAssignment(ImmutableArray<DeconstructionTarget> targets, IrExpression source, MethodRef? consumedDeconstructMethod = null)
    {
        ConsumedDeconstructMethod = consumedDeconstructMethod;
        AddChild(source);
        foreach (var target in targets)
            AddChild(target);
    }

    public MethodRef? ConsumedDeconstructMethod { get; }

    public ImmutableArray<DeconstructionTarget> Targets
        => [.. Children.Skip(1).Cast<DeconstructionTarget>()];

    public ImmutableArray<int> LocalIndices
        => [.. Targets.Where(target => target.Kind == DeconstructionTargetKind.Local).Select(target => target.LocalIndex)];

    public ImmutableArray<TypeRef> LocalTypes
        => [.. Targets.Where(target => target.Kind == DeconstructionTargetKind.Local).Select(target => target.Type)];

    /// <summary>
    /// Per-local declaration flags, parallel to <see cref="LocalIndices"/>: a
    /// target is <c>true</c> when it is a fresh local introduced here (rendered
    /// with its declared type) and <c>false</c> when it assigns into a pre-existing
    /// local (rendered as a bare name). A run may mix the two — <c>(int x, y) =</c>.
    /// </summary>
    public ImmutableArray<bool> IsDeclared
        => [.. Targets.Where(target => target.Kind == DeconstructionTargetKind.Local).Select(target => target.IsDeclared)];

    /// <summary>True when every target is a fresh declaration (the uniform-declaration form).</summary>
    public bool IsDeclaration => Targets.Length > 0 && Targets.All(target => target.Kind == DeconstructionTargetKind.Local && target.IsDeclared);

    public IrExpression Source => (IrExpression)Children[0];
    public override IEnumerable<TypeRef> DirectTypes => Targets.SelectMany(target => target.DirectTypes);

    public override string Describe()
    {
        string shape = IsDeclaration ? "declaration"
            : IsDeclared.Any(d => d) ? "mixed"
            : "assignment";
        return $"DeconstructionAssignment ({Targets.Length} targets, {shape})";
    }
}

/// <summary>The kind of lvalue a <see cref="ChainedAssignmentTarget"/> writes.</summary>
public enum ChainedAssignmentTargetKind
{
    /// <summary>A static property setter (<c>Type.Name = …</c>).</summary>
    StaticProperty,

    /// <summary>A static field (<c>Type.Name = …</c>).</summary>
    StaticField,
}

/// <summary>
/// One lvalue of a raised chained assignment (<c>A = B = C = value</c>). The
/// first slice covers receiver-free targets — static properties and static
/// fields — where the dup'd value is written directly with no receiver or index
/// to re-evaluate. Payload only (no child expressions), so the enclosing
/// <see cref="ChainedAssignment"/> carries a single value child.
/// </summary>
public sealed record ChainedAssignmentTarget
{
    ChainedAssignmentTarget(ChainedAssignmentTargetKind kind, TypeRef targetType)
    {
        Kind = kind;
        TargetType = targetType;
    }

    public static ChainedAssignmentTarget StaticProperty(MethodRef accessor, bool isVirtual)
        => new(ChainedAssignmentTargetKind.StaticProperty, accessor.ParameterTypes[^1])
        {
            Accessor = accessor,
            IsVirtual = isVirtual,
        };

    public static ChainedAssignmentTarget StaticField(FieldRef field)
        => new(ChainedAssignmentTargetKind.StaticField, field.Type)
        {
            Field = field,
        };

    public ChainedAssignmentTargetKind Kind { get; }

    /// <summary>The lvalue's type — the setter parameter type or the field type.</summary>
    public TypeRef TargetType { get; }
    public MethodRef? Accessor { get; private init; }
    public bool IsVirtual { get; private init; }
    public FieldRef? Field { get; private init; }
    public string PropertyName => Accessor?.Name["set_".Length..] ?? "";

    public IEnumerable<TypeRef> DirectTypes => Kind switch
    {
        ChainedAssignmentTargetKind.StaticProperty => Accessor!.ParameterTypes.Append(Accessor.DeclaringType),
        ChainedAssignmentTargetKind.StaticField => [Field!.DeclaringType, Field.Type],
        _ => [TargetType],
    };
}

/// <summary>
/// A raised C# chained (embedded) assignment: <c>A = B = C = value;</c>. The
/// compiler lowers the chain to a dup-of-value idiom — the rvalue is evaluated
/// once and <c>dup</c>'d to each successive sink, right to left — so the
/// importer sees a run of independent stores of the same slot-carried value.
/// <see cref="ChainedAssignmentPass"/> recomposes that run, keying on the shared
/// dup slot (real evidence, not equal-value coincidence, so genuinely separate
/// statements <c>A = v; B = v;</c> are never collapsed). <see cref="Targets"/>
/// are in source order (outermost first); the single value child is the rvalue,
/// typed at the innermost (rightmost) target so a bool/char/enum literal is
/// recovered there and the outer assignments' implicit conversions stay implicit.
/// </summary>
public sealed class ChainedAssignment : IrNode
{
    public ChainedAssignment(IReadOnlyList<ChainedAssignmentTarget> targets, IrExpression value)
    {
        Targets = [.. targets];
        AddChild(value);
    }

    /// <summary>Targets in source order (<c>A</c>, then <c>B</c>, then <c>C</c> for <c>A = B = C = v</c>).</summary>
    public ImmutableArray<ChainedAssignmentTarget> Targets { get; }

    public IrExpression Value => (IrExpression)Children[0];

    /// <summary>The rightmost (innermost) target's type — the type the shared value flows into first.</summary>
    public TypeRef InnermostTargetType => Targets[^1].TargetType;

    public override IEnumerable<TypeRef> DirectTypes => Targets.SelectMany(target => target.DirectTypes);

    public override string Describe() => $"ChainedAssignment ({Targets.Length} targets)";
}

/// <summary>
/// A raised C# object or collection initializer, produced by
/// <see cref="ObjectInitializerPass"/> from the compiler's lowering of
/// <c>new T { X = a, ... }</c> / <c>new C { e0, e1, ... }</c> — a constructor
/// call whose result is threaded through a dup chain, mutated by a run of member
/// stores (object form) or <c>Add</c> calls (collection form), then consumed
/// once. Child 0 is the <see cref="NewObject"/> creation; the remaining children
/// are the entry values, parallel to <see cref="Members"/> (a member name for the
/// object form, <c>null</c> for a collection element).
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundObjectInitializerExpression,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundObjectInitializerExpression / BoundCollectionInitializerExpression (new T { ... })",
    precondition: "result is the creation's type; raised only when the constructor result is threaded through a dup chain, mutated by a run of member stores or Add calls, and consumed exactly once; entry order preserves the IL store order",
    witness: "ObjectInitializerPassTests, ObjectInitializerContiguityTests, corpus compile-back")]
public sealed class ObjectInitializerExpression : IrExpression
{
    public ObjectInitializerExpression(NewObject creation, bool isCollection, IEnumerable<InitializerEntry> entries)
    {
        IsCollection = isCollection;
        AddChild(creation);
        var members = ImmutableArray.CreateBuilder<string?>();
        var argumentCounts = ImmutableArray.CreateBuilder<int>();
        var consumedMethods = ImmutableArray.CreateBuilder<MethodRef?>();
        var consumedFields = ImmutableArray.CreateBuilder<FieldRef?>();
        var consumedMethodsAreVirtual = ImmutableArray.CreateBuilder<bool>();
        foreach (var entry in entries)
        {
            members.Add(entry.Member);
            argumentCounts.Add(entry.Arguments.Count);
            consumedMethods.Add(entry.ConsumedMethod);
            consumedFields.Add(entry.ConsumedField);
            consumedMethodsAreVirtual.Add(entry.ConsumedMethodIsVirtual);
            foreach (var argument in entry.Arguments)
                AddChild(argument);
        }
        Members = members.ToImmutable();
        ArgumentCounts = argumentCounts.ToImmutable();
        ConsumedMethods = consumedMethods.ToImmutable();
        ConsumedFields = consumedFields.ToImmutable();
        ConsumedMethodsAreVirtual = consumedMethodsAreVirtual.ToImmutable();
    }

    /// <summary>Collection-initializer (<c>{ e0, e1 }</c> / <c>{ {k, v} }</c> via <c>Add</c>) vs object-initializer (<c>{ X = a }</c> / <c>{ [k] = v }</c> via member or indexer stores).</summary>
    public bool IsCollection { get; }

    /// <summary>The <c>new T(...)</c> creation the initializer decorates.</summary>
    public NewObject Creation => (NewObject)Children[0];

    /// <summary>Target member name per entry; <c>null</c> for a collection element or an indexer member (<c>[k] = v</c>).</summary>
    public ImmutableArray<string?> Members { get; }

    /// <summary>The number of child expressions each entry owns, parallel to <see cref="Members"/>.</summary>
    public ImmutableArray<int> ArgumentCounts { get; }

    /// <summary>Consumed setter/Add method evidence per entry, when a raised initializer entry came from a method call.</summary>
    public ImmutableArray<MethodRef?> ConsumedMethods { get; }

    /// <summary>Consumed field evidence per entry, when a raised initializer entry came from a field store.</summary>
    public ImmutableArray<FieldRef?> ConsumedFields { get; }

    /// <summary>
    /// Whether each entry's consumed setter/<c>Add</c> call dispatched
    /// virtually, parallel to <see cref="ConsumedMethods"/>. The fact is
    /// retained rather than re-derived from the member reference, which does
    /// not carry call-site dispatch.
    /// </summary>
    public ImmutableArray<bool> ConsumedMethodsAreVirtual { get; }

    /// <summary>The entries, in source order, each carrying its own argument expressions.</summary>
    public IReadOnlyList<InitializerEntry> Entries => InitializerEntry.Slice(Children, 1, Members, ArgumentCounts, ConsumedMethods, ConsumedFields, ConsumedMethodsAreVirtual);

    public override TypeRef? ResultType => Creation.ResultType;

    public override string Describe()
        => $"ObjectInitializer {Creation.Constructor.DeclaringType.ToDisplayString()} ({Members.Length} {(IsCollection ? "elements" : "members")})";
}

/// <summary>
/// A raised C# record nondestructive mutation expression:
/// <c>receiver with { X = value, ... }</c>. Produced from the compiler's
/// clone-then-member-set lowering when the synthesized record clone and the
/// single non-escaping mutation target are proven. The consumed clone member
/// and dispatch remain attached as semantic evidence.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundWithExpression,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundWithExpression (receiver with { X = value })",
    precondition: "result is the receiver's record type; raised only when the compiler-synthesized clone call, its faithful dispatch, and the single non-escaping mutation target are proven; every entry names a member and carries exactly one value",
    witness: "WithExpressionPassTests, corpus compile-back")]
public sealed class WithExpression : IrExpression
{
    public WithExpression(
        IrExpression receiver,
        IEnumerable<InitializerEntry> entries,
        MethodRef? consumedCloneMethod = null,
        bool consumedCloneIsVirtual = false)
    {
        if (consumedCloneMethod is null && consumedCloneIsVirtual)
        {
            throw new ArgumentException(
                "Clone dispatch requires a consumed clone method.",
                nameof(consumedCloneIsVirtual));
        }
        AddChild(receiver);
        var members = ImmutableArray.CreateBuilder<string>();
        var consumedMethods = ImmutableArray.CreateBuilder<MethodRef?>();
        var consumedFields = ImmutableArray.CreateBuilder<FieldRef?>();
        var consumedMethodsAreVirtual = ImmutableArray.CreateBuilder<bool>();
        foreach (var entry in entries)
        {
            if (entry.Member is null)
                throw new ArgumentException("With-expression entries must name a member.", nameof(entries));
            if (entry.Arguments.Count != 1)
                throw new ArgumentException("With-expression entries must contain exactly one value.", nameof(entries));

            members.Add(entry.Member);
            consumedMethods.Add(entry.ConsumedMethod);
            consumedFields.Add(entry.ConsumedField);
            consumedMethodsAreVirtual.Add(entry.ConsumedMethodIsVirtual);
            AddChild(entry.Arguments[0]);
        }
        Members = members.ToImmutable();
        ConsumedMethods = consumedMethods.ToImmutable();
        ConsumedFields = consumedFields.ToImmutable();
        ConsumedMethodsAreVirtual = consumedMethodsAreVirtual.ToImmutable();
        ConsumedCloneMethod = consumedCloneMethod;
        ConsumedCloneIsVirtual = consumedCloneIsVirtual;
    }

    public IrExpression Receiver => (IrExpression)Children[0];
    public MethodRef? CloneMethod => ConsumedCloneMethod;
    public ImmutableArray<string> Members { get; }
    public ImmutableArray<MethodRef?> ConsumedMethods { get; }
    public ImmutableArray<FieldRef?> ConsumedFields { get; }
    public MethodRef? ConsumedCloneMethod { get; }
    public bool ConsumedCloneIsVirtual { get; }

    /// <summary>
    /// Whether each entry's consumed setter dispatched virtually, parallel to
    /// <see cref="ConsumedMethods"/>. <c>receiver with { X = v }</c> spells a
    /// virtual setter call, so a raise is faithful only when the consumed call
    /// was virtual.
    /// </summary>
    public ImmutableArray<bool> ConsumedMethodsAreVirtual { get; }
    public IReadOnlyList<InitializerEntry> Entries
        => InitializerEntry.Slice(
            Children,
            1,
            [.. Members.Select(m => (string?)m)],
            [.. Members.Select(_ => 1)],
            ConsumedMethods,
            ConsumedFields,
            ConsumedMethodsAreVirtual);
    public override TypeRef? ResultType => Receiver.ResultType;
    public override string Describe()
        => $"WithExpression ({Members.Length} members)";
}

/// <summary>
/// A nested initializer body — the brace group on the right of a nested member
/// entry (<c>Inner = { X = a }</c> / <c>Items = { e0, e1 }</c>), produced by
/// <see cref="ObjectInitializerPass"/> from member/<c>Add</c> stores rooted at a
/// member read off the threaded reference rather than the reference itself. It is
/// an <see cref="ObjectInitializerExpression"/> without the <c>new T(...)</c>
/// creation: the member is initialized in place, not reconstructed. It only
/// appears as the value of a parent <see cref="InitializerEntry"/>, so the IR tree
/// carries arbitrary nesting depth without any special-casing here.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundObjectInitializerExpression,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundObjectInitializerExpression member (Inner = { X = a } / Items = { e0, e1 })",
    precondition: "an ObjectInitializerExpression body without the creation — the member is initialized in place, not reconstructed; appears only as the value of a parent InitializerEntry, raised from member/Add stores rooted at a member read off the threaded reference",
    witness: "ObjectInitializerPassTests, ObjectInitializerContiguityTests, corpus compile-back")]
public sealed class InitializerBlock : IrExpression
{
    public InitializerBlock(bool isCollection, IEnumerable<InitializerEntry> entries)
    {
        IsCollection = isCollection;
        var members = ImmutableArray.CreateBuilder<string?>();
        var argumentCounts = ImmutableArray.CreateBuilder<int>();
        var consumedMethods = ImmutableArray.CreateBuilder<MethodRef?>();
        var consumedFields = ImmutableArray.CreateBuilder<FieldRef?>();
        var consumedMethodsAreVirtual = ImmutableArray.CreateBuilder<bool>();
        foreach (var entry in entries)
        {
            members.Add(entry.Member);
            argumentCounts.Add(entry.Arguments.Count);
            consumedMethods.Add(entry.ConsumedMethod);
            consumedFields.Add(entry.ConsumedField);
            consumedMethodsAreVirtual.Add(entry.ConsumedMethodIsVirtual);
            foreach (var argument in entry.Arguments)
                AddChild(argument);
        }
        Members = members.ToImmutable();
        ArgumentCounts = argumentCounts.ToImmutable();
        ConsumedMethods = consumedMethods.ToImmutable();
        ConsumedFields = consumedFields.ToImmutable();
        ConsumedMethodsAreVirtual = consumedMethodsAreVirtual.ToImmutable();
    }

    /// <summary>Collection body (<c>{ e0, e1 }</c> via <c>Add</c>) vs object body (<c>{ X = a }</c> via member stores).</summary>
    public bool IsCollection { get; }

    /// <summary>Target member name per entry; <c>null</c> for a collection element or an indexer member.</summary>
    public ImmutableArray<string?> Members { get; }

    /// <summary>The number of child expressions each entry owns, parallel to <see cref="Members"/>.</summary>
    public ImmutableArray<int> ArgumentCounts { get; }

    /// <summary>Consumed setter/Add method evidence per entry, when a raised initializer entry came from a method call.</summary>
    public ImmutableArray<MethodRef?> ConsumedMethods { get; }

    /// <summary>Consumed field evidence per entry, when a raised initializer entry came from a field store.</summary>
    public ImmutableArray<FieldRef?> ConsumedFields { get; }

    /// <summary>
    /// Whether each entry's consumed setter/<c>Add</c> call dispatched
    /// virtually, parallel to <see cref="ConsumedMethods"/>. The fact is
    /// retained rather than re-derived from the member reference, which does
    /// not carry call-site dispatch.
    /// </summary>
    public ImmutableArray<bool> ConsumedMethodsAreVirtual { get; }

    /// <summary>The entries, in source order, each carrying its own argument expressions.</summary>
    public IReadOnlyList<InitializerEntry> Entries => InitializerEntry.Slice(Children, 0, Members, ArgumentCounts, ConsumedMethods, ConsumedFields, ConsumedMethodsAreVirtual);

    /// <summary>A nested body initializes an existing member in place; it has no standalone result type.</summary>
    public override TypeRef? ResultType => null;

    public override string Describe()
        => $"InitializerBlock ({Members.Length} {(IsCollection ? "elements" : "members")})";
}

/// <summary>
/// One entry of a raised object/collection initializer. The interpretation of
/// <see cref="Arguments"/> depends on the parent's <see cref="ObjectInitializerExpression.IsCollection"/>
/// flag and <see cref="Member"/>:
/// <list type="bullet">
/// <item>object form, named member (<c>X = v</c>): <see cref="Member"/> set, one argument (the value);</item>
/// <item>object form, indexer member (<c>[k0, k1] = v</c>): <see cref="Member"/> null, the trailing argument is the value and the rest are the index keys;</item>
/// <item>collection form (<c>v</c> or <c>{ k, v }</c>): <see cref="Member"/> null, the arguments are the <c>Add</c> call's value arguments.</item>
/// </list>
/// A nested member entry (<c>Inner = { ... }</c>) is a named member whose single
/// argument is an <see cref="InitializerBlock"/>.
/// </summary>
public sealed record InitializerEntry(
    string? Member,
    IReadOnlyList<IrExpression> Arguments,
    MethodRef? ConsumedMethod = null,
    FieldRef? ConsumedField = null,
    bool ConsumedMethodIsVirtual = false)
{
    /// <summary>
    /// Reconstructs the entries from a node's flat children: the run starting at
    /// <paramref name="start"/> is partitioned by <paramref name="argumentCounts"/>,
    /// each slice paired with its <paramref name="members"/> name. Shared by
    /// <see cref="ObjectInitializerExpression"/> (start 1, after the creation) and
    /// <see cref="InitializerBlock"/> (start 0).
    /// </summary>
    internal static IReadOnlyList<InitializerEntry> Slice(
        IReadOnlyList<IrNode> children,
        int start,
        ImmutableArray<string?> members,
        ImmutableArray<int> argumentCounts,
        ImmutableArray<MethodRef?> consumedMethods,
        ImmutableArray<FieldRef?> consumedFields,
        ImmutableArray<bool> consumedMethodsAreVirtual)
    {
        var entries = new List<InitializerEntry>(members.Length);
        int index = start;
        for (int e = 0; e < members.Length; e++)
        {
            int count = argumentCounts[e];
            var arguments = new IrExpression[count];
            for (int j = 0; j < count; j++)
                arguments[j] = (IrExpression)children[index + j];
            index += count;
            entries.Add(new InitializerEntry(
                members[e],
                arguments,
                consumedMethods[e],
                consumedFields[e],
                consumedMethodsAreVirtual[e]));
        }
        return entries;
    }
}
