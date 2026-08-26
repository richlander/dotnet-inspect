using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Compiler-generated shape facts over IR metadata evidence. Attribute evidence
/// is required before generated-name patterns are trusted; the names only
/// corroborate the compiler shape.
/// </summary>
public static class GeneratedCodeIdentity
{
    public static bool IsNonCapturingLambdaMethod(MethodRef method)
        => method.DeclaringTypeCompilerGenerated == MetadataFactState.Yes
            && IsStaticLambdaClosureHolderName(method.DeclaringType)
            && IsSynthesizedLambdaMethodName(method.Name);

    /// <summary>
    /// A lambda body method on a <c>&lt;&gt;c__DisplayClass</c> environment — the
    /// capturing form, where the lambda reads hoisted fields through its instance
    /// <c>this</c> rather than running on the static <c>&lt;&gt;c</c> singleton.
    /// </summary>
    public static bool IsCapturingLambdaMethod(MethodRef method)
        => method.DeclaringTypeCompilerGenerated == MetadataFactState.Yes
            && IsDisplayClassName(method.DeclaringType)
            && IsSynthesizedLambdaMethodName(method.Name);

    public static bool IsStaticLambdaClosureHolderName(TypeRef type)
        => LeafTypeName(type.Name) == GeneratedNameGrammar.NonCapturingLambdaHolderName;

    public static bool IsDisplayClassName(TypeRef type)
        => GeneratedNameGrammar.IsDisplayClassLeaf(LeafTypeName(type.Name));

    public static bool IsSynthesizedLambdaMethodName(string name)
        => GeneratedNameGrammar.IsSynthesizedLambdaMethodName(name);

    /// <summary>
    /// A synthesized local-function method: <c>&lt;Enclosing&gt;g__Name|N_M</c>,
    /// emitted directly on the enclosing type (not a closure holder), so it is
    /// the method's own <c>[CompilerGenerated]</c> evidence — not the declaring
    /// type's — that gates the name pattern.
    /// </summary>
    public static bool IsLocalFunctionMethod(MethodRef method)
        => method.CompilerGenerated == MetadataFactState.Yes
            && IsSynthesizedLocalFunctionName(method.Name);

    public static bool IsIteratorStateMachineConstructor(MethodRef constructor)
        => constructor.DeclaringTypeCompilerGenerated == MetadataFactState.Yes
            && constructor.Name == ".ctor"
            && IsIteratorStateMachineTypeName(constructor.DeclaringType);

    public static bool IsRecordCloneMethod(MethodRef method)
        => method.CompilerGenerated == MetadataFactState.Yes
            && method.HasThis
            && method.Name == "<Clone>$"
            && method.ParameterTypes.IsDefaultOrEmpty
            && method.ReturnType.Equals(method.DeclaringType);

    public static bool IsStringHashHelper(MethodRef method)
        => method.DeclaringTypeCompilerGenerated == MetadataFactState.Yes
            && !method.HasThis
            && method.Name == "ComputeStringHash"
            && LeafTypeName(MetadataTypeName(method.DeclaringType)) == GeneratedNameGrammar.PrivateImplementationDetailsTypeName
            && method.ReturnType.Equals(TypeRef.CoreLib("System", "UInt32"))
            && method.ParameterTypes is [var parameter]
            && parameter.Equals(TypeRef.CoreLib("System", "String"));

    /// <summary>
    /// A compiler-generated inline-array element-ref helper:
    /// <c>&lt;PrivateImplementationDetails&gt;.InlineArray{First}ElementRef[ReadOnly]&lt;TBuffer, TElement&gt;</c>,
    /// the runtime intrinsic the C# compiler emits for <c>buffer[i]</c> indexing of an
    /// <c>[InlineArray]</c> buffer. Like every other helper here, the
    /// <c>[CompilerGenerated]</c> attribute on the static <c>&lt;PrivateImplementationDetails&gt;</c>
    /// holder is required before the name is trusted, so a user type or method that merely
    /// reuses the unspeakable holder name and a helper name is not mistaken for the intrinsic
    /// and over-raised into <c>buffer[i]</c> (#1365). <paramref name="first"/> marks the
    /// zero-index form, <paramref name="readOnly"/> the read-only form.
    /// </summary>
    public static bool IsInlineArrayElementRefHelper(MethodRef method, out bool first, out bool readOnly)
    {
        first = false;
        readOnly = false;
        if (method.DeclaringTypeCompilerGenerated != MetadataFactState.Yes
            || method.HasThis
            || LeafTypeName(MetadataTypeName(method.DeclaringType)) != GeneratedNameGrammar.PrivateImplementationDetailsTypeName)
        {
            return false;
        }
        switch (method.Name)
        {
            case "InlineArrayElementRef":
                return true;
            case "InlineArrayElementRefReadOnly":
                readOnly = true;
                return true;
            case "InlineArrayFirstElementRef":
                first = true;
                return true;
            case "InlineArrayFirstElementRefReadOnly":
                first = true;
                readOnly = true;
                return true;
            default:
                return false;
        }
    }

    public static bool IsDynamicCallSiteContainerType(TypeRef type)
        => LeafTypeName(MetadataTypeName(type)).StartsWith(GeneratedNameGrammar.DynamicCallSiteContainerPrefix, StringComparison.Ordinal);

    public static bool IsIteratorStateMachineTypeName(TypeRef type)
        => GeneratedNameGrammar.IsStateMachineLeaf(LeafTypeName(MetadataTypeName(type)));

    public static bool IsSynthesizedLocalFunctionName(string name)
        => GeneratedNameGrammar.IsSynthesizedLocalFunctionName(name);

    /// <summary>
    /// A compiler-generated field name. The leading <c>&lt;</c> is unspeakable in
    /// C# source, so the name alone is reliable evidence the field was
    /// synthesized — whether state-machine plumbing (<c>&lt;&gt;1__state</c>,
    /// <c>&lt;&gt;2__current</c>) or a hoisted local (<c>&lt;i&gt;5__2</c>). The
    /// iterator/async reconstructions use this to spot a residual state-machine
    /// field a rewrite failed to remap.
    /// </summary>
    public static bool IsGeneratedFieldName(string name)
        => GeneratedNameGrammar.IsGeneratedFieldName(name);

    /// <summary>
    /// A hoisted user-local field — <c>&lt;name&gt;5__N</c>, the lifted form of a
    /// source local or parameter inside a state machine. The single <c>&lt;</c>
    /// marks it generated; the absent <c>&lt;&gt;</c> double prefix and the
    /// <c>&gt;5__</c> infix (Roslyn's hoisted-local kind) together distinguish it
    /// from pure state-machine plumbing (<c>&lt;&gt;1__state</c>,
    /// <c>&lt;&gt;2__current</c>), which a reconstruction maps to its own
    /// constructs rather than to a source local. This is exactly the field set
    /// those passes materialize back into kickoff locals and parameters.
    /// </summary>
    public static bool IsHoistedLocalFieldName(string name)
        => GeneratedNameGrammar.IsHoistedLocalFieldName(name);

    static string LeafTypeName(string name)
        => GeneratedNameGrammar.LeafSegment(name);

    static string MetadataTypeName(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance ? type.ElementType?.Name ?? "" : type.Name;
}
