using System.Reflection.Metadata;
using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// Accumulates encoded then decoded type-name lengths against
/// <see cref="MetadataSafetyPolicy.MaxTypeNameCharacters"/> so a relationship
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
/// characters plus one delimiter per name segment.
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

    public void SeedMaterialized(string value)
    {
        encoded += Encoding.UTF8.GetByteCount(value);
        characters += value.Length;
    }

    public bool TryRead(
        MetadataReader reader,
        StringHandle handle,
        int delimiterChars,
        Action<int>? beforeMaterialize,
        out string value,
        bool enforceCharacterBudget = true)
    {
        if (handle.IsNil)
        {
            value = string.Empty;
            encoded += delimiterChars;
            characters += delimiterChars;
            return !enforceCharacterBudget
                || characters <= MetadataSafetyPolicy.MaxTypeNameCharacters;
        }

        int utf8Length = reader.GetBlobReader(handle).Length;
        beforeMaterialize?.Invoke(utf8Length);
        encoded += utf8Length + delimiterChars;
        if (encoded > MaxEncodedBytes)
        {
            value = string.Empty;
            return false;
        }

        value = reader.GetString(handle);
        characters += value.Length + delimiterChars;
        return !enforceCharacterBudget
            || characters <= MetadataSafetyPolicy.MaxTypeNameCharacters;
    }
}
