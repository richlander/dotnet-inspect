using System.Reflection.Metadata;

namespace ILInspector.Metadata;

internal enum MetadataTypeNameBudgetKind
{
    EncodedBytes,
    Characters,
}

internal readonly record struct MetadataTypeNameBudgetFailure(
    MetadataTypeNameBudgetKind Kind,
    long Limit,
    long AttemptedCharge);

/// <summary>
/// Accumulates encoded and decoded type-name lengths against their respective
/// ceilings so a relationship
/// reader can refuse a name before <see cref="MetadataReader.GetString(StringHandle)"/>
/// materializes an over-budget heap entry or concatenates many shared ones.
/// </summary>
/// <remarks>
/// UTF-8 storage is at most three bytes per UTF-16 code unit, so the encoded
/// preflight uses <c>3 * MaxTypeNameCharacters</c> and refuses a huge #Strings
/// entry before <see cref="MetadataReader.GetString(StringHandle)"/>. The
/// decoded recheck is the 4,096-character policy; a legal CJK name must not
/// fail the encoded check. Projected virtual strings may already be
/// materialized by <see cref="MetadataReader.GetBlobReader(StringHandle)"/>;
/// the decoded recheck still prevents later segments from being appended.
/// Accounting matches <c>MetadataTypeDefinitionName.Create</c>: namespace
/// characters plus one delimiter per name segment. Every component the reader
/// consumes is charged, including a nil one: the decoded length is zero but the
/// read still costs the caller a segment, so charging nothing would let a deep
/// chain of nil-named nodes materialize proportionally for free.
/// </remarks>
internal struct MetadataTypeNameBudget
{
    /// <summary>
    /// Worst-case UTF-8 bytes for a name that is still within
    /// <see cref="MetadataSafetyPolicy.MaxTypeNameCharacters"/> UTF-16 units.
    /// </summary>
    public const int MaxEncodedBytes =
        MetadataSafetyPolicy.MaxTypeNameCharacters * 3;

    long encoded;
    long characters;

    public void CopyFrom(in MetadataTypeNameBudget other)
    {
        encoded = other.encoded;
        characters = other.characters;
    }

    public bool TryRead(
        MetadataReader reader,
        StringHandle handle,
        int delimiterChars,
        Action<int>? beforeMaterialize,
        out string value,
        bool enforceCharacterBudget = true)
        => TryRead(
            reader,
            handle,
            delimiterChars,
            beforeMaterialize,
            out value,
            out _,
            enforceCharacterBudget);

    internal bool TryRead(
        MetadataReader reader,
        StringHandle handle,
        int delimiterChars,
        Action<int>? beforeMaterialize,
        out string value,
        out MetadataTypeNameBudgetFailure failure,
        bool enforceCharacterBudget = true)
    {
        if (handle.IsNil)
        {
            // A nil component still consumes a chain slot, a delimiter, and a
            // segment allocation in the caller. Charge the read so that a deep
            // chain of nil-named nodes cannot drive proportional materializing
            // work while charging the observer nothing.
            beforeMaterialize?.Invoke(0);
            value = string.Empty;
            encoded += delimiterChars;
            characters += delimiterChars;
            if (encoded > MaxEncodedBytes)
            {
                failure = new(
                    MetadataTypeNameBudgetKind.EncodedBytes,
                    MaxEncodedBytes,
                    encoded);
                return false;
            }
            if (enforceCharacterBudget
                && characters
                    > MetadataSafetyPolicy.MaxTypeNameCharacters)
            {
                failure = new(
                    MetadataTypeNameBudgetKind.Characters,
                    MetadataSafetyPolicy.MaxTypeNameCharacters,
                    characters);
                return false;
            }
            failure = default;
            return true;
        }

        int utf8Length = reader.GetBlobReader(handle).Length;
        beforeMaterialize?.Invoke(utf8Length);
        encoded += utf8Length + delimiterChars;
        if (encoded > MaxEncodedBytes)
        {
            value = string.Empty;
            failure = new(
                MetadataTypeNameBudgetKind.EncodedBytes,
                MaxEncodedBytes,
                encoded);
            return false;
        }

        value = reader.GetString(handle);
        characters += value.Length + delimiterChars;
        if (enforceCharacterBudget
            && characters > MetadataSafetyPolicy.MaxTypeNameCharacters)
        {
            failure = new(
                MetadataTypeNameBudgetKind.Characters,
                MetadataSafetyPolicy.MaxTypeNameCharacters,
                characters);
            return false;
        }
        failure = default;
        return true;
    }
}
