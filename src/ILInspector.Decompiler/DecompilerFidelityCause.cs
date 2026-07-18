namespace ILInspector.Decompiler;

/// <summary>
/// Stable semantic facets for <see cref="DecompilerFidelityCause.Discriminator"/>.
/// Human-readable diagnostic reasons are deliberately not part of this contract.
/// </summary>
public static class DecompilerFidelityDiscriminators
{
    public const string AccessorMetadataUnavailable = "accessor-metadata-unavailable";
    public const string DisplayClassTypeName = "display-class-type-name";
    public const string EscapableFieldName = "escapable-field-name";
    public const string EscapableInitializerMemberName = "escapable-initializer-member-name";
    public const string EscapablePropertyName = "escapable-property-name";
    public const string GeneratedFieldName = "generated-field-name";
    public const string GeneratedGenericParameterName = "generated-generic-parameter-name";
    public const string GeneratedInitializerMemberName = "generated-initializer-member-name";
    public const string GeneratedMethodName = "generated-method-name";
    public const string GeneratedPropertyName = "generated-property-name";
    public const string GeneratedTypeName = "generated-type-name";
    public const string LambdaHolderTypeName = "lambda-holder-type-name";
    public const string LambdaMethodName = "lambda-method-name";
    public const string LocalFunctionMethodName = "local-function-method-name";
    public const string PinnedLocal = "pinned-local";
    public const string PrivateImplementationDetailsType = "private-implementation-details-type";
    public const string StateMachineTypeName = "state-machine-type-name";
    public const string UnsupportedTypeShape = "unsupported-type-shape";
    public const string UnspellableFieldName = "unspellable-field-name";
    public const string UnspellableGenericParameterName = "unspellable-generic-parameter-name";
    public const string UnspellableInitializerMemberName = "unspellable-initializer-member-name";
    public const string UnspellableLocalFunctionName = "unspellable-local-function-name";
    public const string UnspellableMethodName = "unspellable-method-name";
    public const string UnspellablePropertyName = "unspellable-property-name";
    public const string UnspellableTypeName = "unspellable-type-name";
}

public enum DecompilerFidelityLocationKind
{
    Unknown,
    Signature,
    IlOffset,
    Local,
}

/// <summary>
/// Typed provenance for one fidelity-lowering cause. IL offsets and local slots
/// are version-local coordinates, not cross-version correspondence identity.
/// </summary>
public readonly record struct DecompilerFidelityLocation
{
    DecompilerFidelityLocation(
        DecompilerFidelityLocationKind kind,
        int? ilOffset,
        int? localIndex)
    {
        Kind = kind;
        ILOffset = ilOffset;
        LocalIndex = localIndex;
    }

    public DecompilerFidelityLocationKind Kind { get; }

    public int? ILOffset { get; }

    public int? LocalIndex { get; }

    public static DecompilerFidelityLocation Unknown { get; } =
        new(DecompilerFidelityLocationKind.Unknown, null, null);

    public static DecompilerFidelityLocation Signature { get; } =
        new(DecompilerFidelityLocationKind.Signature, null, null);

    public static DecompilerFidelityLocation AtIlOffset(int offset)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return new DecompilerFidelityLocation(
            DecompilerFidelityLocationKind.IlOffset,
            offset,
            null);
    }

    public static DecompilerFidelityLocation AtLocal(int localIndex)
    {
        if (localIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(localIndex));
        return new DecompilerFidelityLocation(
            DecompilerFidelityLocationKind.Local,
            null,
            localIndex);
    }
}

/// <summary>
/// One concrete site that prevents a decompiled method from claiming
/// <see cref="DecompilationFidelity.Full"/> fidelity.
/// </summary>
public sealed record DecompilerFidelityCause
{
    public DecompilerFidelityCause(
        string code,
        DecompilerFidelityLocation location,
        string nodeKind,
        string node,
        string reason,
        string? discriminator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Code = code;
        Location = location;
        NodeKind = nodeKind;
        Node = node;
        Reason = reason;
        Discriminator = discriminator;
    }

    public string Code { get; }

    public DecompilerFidelityLocation Location { get; }

    public string NodeKind { get; }

    public string Node { get; }

    public string Reason { get; }

    /// <summary>
    /// Producer-owned structured detail used by consumers that group causes,
    /// such as an unsupported opcode or type-shape reason.
    /// </summary>
    public string? Discriminator { get; }
}
