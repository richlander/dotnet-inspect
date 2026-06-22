namespace ILInspector.Metadata;

/// <summary>
/// Full type names of well-known <c>System.Runtime.CompilerServices</c>
/// attributes the metadata scanners classify (compiler-emitted noise,
/// re-synthesized attributes, state-machine markers, etc.). Centralized so the
/// long namespace prefix and exact spelling live in one place instead of being
/// retyped at each comparison site.
/// </summary>
public static class KnownAttributeNames
{
    private const string Prefix = "System.Runtime.CompilerServices.";

    public const string AsyncIteratorStateMachineAttribute = Prefix + "AsyncIteratorStateMachineAttribute";
    public const string AsyncStateMachineAttribute = Prefix + "AsyncStateMachineAttribute";
    public const string CompilationRelaxationsAttribute = Prefix + "CompilationRelaxationsAttribute";
    public const string CompilerFeatureRequiredAttribute = Prefix + "CompilerFeatureRequiredAttribute";
    public const string CompilerGeneratedAttribute = Prefix + "CompilerGeneratedAttribute";
    public const string DateTimeConstantAttribute = Prefix + "DateTimeConstantAttribute";
    public const string DecimalConstantAttribute = Prefix + "DecimalConstantAttribute";
    public const string DisableRuntimeMarshallingAttribute = Prefix + "DisableRuntimeMarshallingAttribute";
    public const string DynamicAttribute = Prefix + "DynamicAttribute";
    public const string ExtensionAttribute = Prefix + "ExtensionAttribute";
    public const string IsByRefLikeAttribute = Prefix + "IsByRefLikeAttribute";
    public const string IsReadOnlyAttribute = Prefix + "IsReadOnlyAttribute";
    public const string IsUnmanagedAttribute = Prefix + "IsUnmanagedAttribute";
    public const string IteratorStateMachineAttribute = Prefix + "IteratorStateMachineAttribute";
    public const string MemorySafetyRulesAttribute = Prefix + "MemorySafetyRulesAttribute";
    public const string MethodImplAttribute = Prefix + "MethodImplAttribute";
    public const string NativeIntegerAttribute = Prefix + "NativeIntegerAttribute";
    public const string NullableAttribute = Prefix + "NullableAttribute";
    public const string NullableContextAttribute = Prefix + "NullableContextAttribute";
    public const string NullablePublicOnlyAttribute = Prefix + "NullablePublicOnlyAttribute";
    public const string RefSafetyRulesAttribute = Prefix + "RefSafetyRulesAttribute";
    public const string RequiredMemberAttribute = Prefix + "RequiredMemberAttribute";

    // RequiresUnsafeAttribute is emitted in System.Diagnostics.CodeAnalysis (the
    // metadata form of the member `unsafe`/`extern` modifier under the updated
    // memory-safety rules). Early design notes placed it in
    // System.Runtime.CompilerServices, so the legacy spelling is tolerated too.
    public const string RequiresUnsafeAttribute = "System.Diagnostics.CodeAnalysis.RequiresUnsafeAttribute";
    public const string RequiresUnsafeAttributeCompilerServices = Prefix + "RequiresUnsafeAttribute";

    public const string RuntimeCompatibilityAttribute = Prefix + "RuntimeCompatibilityAttribute";
    public const string RuntimeHostConfigurationOptionAttribute = Prefix + "RuntimeHostConfigurationOptionAttribute";
    public const string ScopedRefAttribute = Prefix + "ScopedRefAttribute";
    public const string SkipLocalsInitAttribute = Prefix + "SkipLocalsInitAttribute";
    public const string TupleElementNamesAttribute = Prefix + "TupleElementNamesAttribute";
}
