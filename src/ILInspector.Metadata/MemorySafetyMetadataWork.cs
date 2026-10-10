namespace ILInspector.Metadata;

/// <summary>
/// Metadata work actually read while deriving memory-safety rules, accessor
/// associations, and member contracts. Row counts are the rows enumerated,
/// which stop early when a scan fails or exhausts its budget. Name characters
/// are the characters admitted by the name-work budget. Signature bytes are
/// the member signature, accessor-shape, and TypeSpec blob bytes decoded.
/// </summary>
public sealed record MemorySafetyMetadataWork(
    int CustomAttributeOrderRows,
    int IntegrityTypeDefRows,
    int IntegrityMethodDefRows,
    int IntegrityNestedClassRows,
    int IntegrityPropertyRows,
    int IntegrityEventRows,
    int AssociationPropertyRows,
    int AssociationEventRows,
    int AssociationMethodSemanticsRows,
    int ModuleAttributeRows,
    int MemberAttributeRows,
    long NameCharacters,
    long SignatureBytes)
{
    public static MemorySafetyMetadataWork Empty { get; } =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>The work recorded after <paramref name="earlier"/>.</summary>
    public MemorySafetyMetadataWork Since(MemorySafetyMetadataWork earlier)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        return new(
            CustomAttributeOrderRows - earlier.CustomAttributeOrderRows,
            IntegrityTypeDefRows - earlier.IntegrityTypeDefRows,
            IntegrityMethodDefRows - earlier.IntegrityMethodDefRows,
            IntegrityNestedClassRows - earlier.IntegrityNestedClassRows,
            IntegrityPropertyRows - earlier.IntegrityPropertyRows,
            IntegrityEventRows - earlier.IntegrityEventRows,
            AssociationPropertyRows - earlier.AssociationPropertyRows,
            AssociationEventRows - earlier.AssociationEventRows,
            AssociationMethodSemanticsRows
                - earlier.AssociationMethodSemanticsRows,
            ModuleAttributeRows - earlier.ModuleAttributeRows,
            MemberAttributeRows - earlier.MemberAttributeRows,
            NameCharacters - earlier.NameCharacters,
            SignatureBytes - earlier.SignatureBytes);
    }

    /// <summary>The sum of this work and <paramref name="other"/>.</summary>
    public MemorySafetyMetadataWork Plus(MemorySafetyMetadataWork other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(
            checked(CustomAttributeOrderRows + other.CustomAttributeOrderRows),
            checked(IntegrityTypeDefRows + other.IntegrityTypeDefRows),
            checked(IntegrityMethodDefRows + other.IntegrityMethodDefRows),
            checked(IntegrityNestedClassRows + other.IntegrityNestedClassRows),
            checked(IntegrityPropertyRows + other.IntegrityPropertyRows),
            checked(IntegrityEventRows + other.IntegrityEventRows),
            checked(AssociationPropertyRows + other.AssociationPropertyRows),
            checked(AssociationEventRows + other.AssociationEventRows),
            checked(
                AssociationMethodSemanticsRows
                + other.AssociationMethodSemanticsRows),
            checked(ModuleAttributeRows + other.ModuleAttributeRows),
            checked(MemberAttributeRows + other.MemberAttributeRows),
            checked(NameCharacters + other.NameCharacters),
            checked(SignatureBytes + other.SignatureBytes));
    }
}

/// <summary>The mutable counters behind <see cref="MemorySafetyMetadataWork"/>.</summary>
internal sealed class MemorySafetyMetadataWorkRecorder
{
    public int CustomAttributeOrderRows;
    public int IntegrityTypeDefRows;
    public int IntegrityMethodDefRows;
    public int IntegrityNestedClassRows;
    public int IntegrityPropertyRows;
    public int IntegrityEventRows;
    public int AssociationPropertyRows;
    public int AssociationEventRows;
    public int AssociationMethodSemanticsRows;
    public int ModuleAttributeRows;
    public int MemberAttributeRows;
    public long NameCharacters;
    public long SignatureBytes;

    public void ObserveSignatureBytes(int bytes) =>
        SignatureBytes += bytes;

    public MemorySafetyMetadataWork Snapshot() =>
        new(
            CustomAttributeOrderRows,
            IntegrityTypeDefRows,
            IntegrityMethodDefRows,
            IntegrityNestedClassRows,
            IntegrityPropertyRows,
            IntegrityEventRows,
            AssociationPropertyRows,
            AssociationEventRows,
            AssociationMethodSemanticsRows,
            ModuleAttributeRows,
            MemberAttributeRows,
            NameCharacters,
            SignatureBytes);
}
