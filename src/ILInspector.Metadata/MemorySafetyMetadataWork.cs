using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Metadata work actually read while deriving memory-safety rules, accessor
/// associations, and member contracts. Row counts are the rows enumerated,
/// which stop early when a scan fails or exhausts its budget. Name characters
/// are the characters admitted by the name-work budget. Signature bytes are
/// the full length of every blob opened for decoding: each member signature,
/// each TypeSpec reached from it, each accessor-shape signature, and, for each
/// attribute row whose type matched, its constructor signature and, once the
/// constructor is accepted, its value blob.
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

    /// <summary>
    /// Records the full length of <paramref name="blob"/>. An unreadable blob
    /// is left to the guarded decode, so receipting never changes evidence.
    /// </summary>
    public void ObserveBlob(MetadataReader reader, BlobHandle blob)
    {
        try
        {
            SignatureBytes += reader.GetBlobReader(blob).Length;
        }
        catch (BadImageFormatException)
        {
        }
    }

    /// <summary>
    /// Records an attribute constructor's signature blob before the
    /// constructor is validated. Always true, so it composes with the
    /// validation it precedes.
    /// </summary>
    public static bool ObserveConstructor(
        MemorySafetyMetadataWorkRecorder? work,
        MetadataReader reader,
        EntityHandle constructor)
    {
        if (work is null)
            return true;

        try
        {
            BlobHandle signature = constructor.Kind switch
            {
                HandleKind.MethodDefinition =>
                    reader.GetMethodDefinition(
                        (MethodDefinitionHandle)constructor).Signature,
                HandleKind.MemberReference =>
                    reader.GetMemberReference(
                        (MemberReferenceHandle)constructor).Signature,
                _ => default,
            };
            if (!signature.IsNil)
                work.ObserveBlob(reader, signature);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or InvalidOperationException
                or InvalidCastException)
        {
        }

        return true;
    }

    /// <summary>
    /// Records an attribute value blob before it is decoded. Always true, so
    /// it composes with the decode it precedes.
    /// </summary>
    public static bool ObserveValue(
        MemorySafetyMetadataWorkRecorder? work,
        MetadataReader reader,
        CustomAttribute attribute)
    {
        work?.ObserveBlob(reader, attribute.Value);
        return true;
    }

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
