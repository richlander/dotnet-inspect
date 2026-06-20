namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Compiler-generated shape facts over IR metadata evidence. Attribute evidence
/// is required before generated-name patterns are trusted; the names only
/// corroborate the compiler shape.
/// </summary>
public static class GeneratedCodeIdentity
{
    public static bool IsNonCapturingLambdaMethod(MethodRef method)
        => method.DeclaringTypeIsCompilerGenerated
            && IsStaticLambdaClosureHolderName(method.DeclaringType)
            && IsSynthesizedLambdaMethodName(method.Name);

    /// <summary>
    /// A lambda body method on a <c>&lt;&gt;c__DisplayClass</c> environment — the
    /// capturing form, where the lambda reads hoisted fields through its instance
    /// <c>this</c> rather than running on the static <c>&lt;&gt;c</c> singleton.
    /// </summary>
    public static bool IsCapturingLambdaMethod(MethodRef method)
        => method.DeclaringTypeIsCompilerGenerated
            && IsDisplayClassName(method.DeclaringType)
            && IsSynthesizedLambdaMethodName(method.Name);

    public static bool IsStaticLambdaClosureHolderName(TypeRef type)
        => LeafTypeName(type.Name) == "<>c";

    public static bool IsDisplayClassName(TypeRef type)
        => LeafTypeName(type.Name).StartsWith("<>c__DisplayClass", StringComparison.Ordinal);

    public static bool IsSynthesizedLambdaMethodName(string name)
        => name.StartsWith("<", StringComparison.Ordinal)
            && name.Contains(">b__", StringComparison.Ordinal);

    static string LeafTypeName(string name)
    {
        int plus = name.LastIndexOf('+');
        return plus < 0 ? name : name[(plus + 1)..];
    }
}
