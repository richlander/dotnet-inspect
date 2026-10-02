namespace ILInspector.Analysis;

/// <summary>Why a direct call has no statically known target in the inspected module.</summary>
public enum DirectCallTargetUnresolvedReason
{
    /// <summary><c>calli</c>: the target is a runtime function pointer.</summary>
    Indirect,
    /// <summary>The callee signature could not be decoded.</summary>
    UnsupportedSignature,
    /// <summary>The callee or caller-scope signature shape is malformed.</summary>
    MalformedSignature,
    /// <summary>The caller or matched definition has an invalid generic parameter declaration.</summary>
    InvalidGenericDeclaration,
    /// <summary>No current-module method matches the reference.</summary>
    Unmatched,
    /// <summary>More than one current-module method matches the reference.</summary>
    Ambiguous,
}

/// <summary>
/// The Analysis-issued target of one physical direct call: a method declared in the
/// inspected module, a member of another module, a runtime-provided array member, or
/// a typed unresolved outcome. Current-module calls through generic instantiations
/// resolve by token and then by signature.
/// </summary>
public abstract record DirectCallTarget
{
    private protected DirectCallTarget()
    {
    }

    /// <summary>The call binds to <paramref name="Method"/>, declared in the inspected module.</summary>
    public sealed record CurrentModule(MethodIdentity Method) : DirectCallTarget;

    /// <summary>
    /// The callee's declaring type definition belongs to another module; <paramref name="Origin"/>
    /// is its exact decoded reference origin (never followed through type forwarding).
    /// </summary>
    public sealed record External(TypeReferenceOrigin Origin) : DirectCallTarget;

    /// <summary>The callee is a runtime-provided member of an array type (for example <c>Get</c> or <c>Set</c>).</summary>
    public sealed record RuntimeProvided : DirectCallTarget;

    /// <summary>No target is statically known; <paramref name="Reason"/> says why.</summary>
    public sealed record Unresolved(DirectCallTargetUnresolvedReason Reason) : DirectCallTarget;
}
