namespace ILInspector.Decompiler;

public enum DecompilerFidelityLocationKind
{
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
