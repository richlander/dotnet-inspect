using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Accumulates encoded then decoded type-name lengths against
/// <see cref="MetadataSafetyPolicy.MaxTypeNameCharacters"/> so a relationship
/// reader can refuse a name before <see cref="MetadataReader.GetString(StringHandle)"/>
/// materializes an over-budget heap entry or concatenates many shared ones.
/// </summary>
/// <remarks>
/// UTF-8 storage length is an upper bound on UTF-16 length, so the encoded
/// preflight is sufficient to refuse a huge #Strings entry. Projected virtual
/// strings may already be materialized by <see cref="MetadataReader.GetBlobReader(StringHandle)"/>;
/// the decoded recheck still prevents later segments from being appended.
/// Accounting matches <c>MetadataTypeDefinitionName.Create</c>: namespace
/// characters plus one delimiter per name segment.
/// </remarks>
internal struct MetadataTypeNameBudget
{
    long encoded;
    long characters;

    public void SeedMaterialized(string value)
    {
        encoded += value.Length;
        characters += value.Length;
    }

    public bool TryRead(
        MetadataReader reader,
        StringHandle handle,
        int delimiterChars,
        Action<int>? beforeMaterialize,
        out string value)
    {
        int utf8Length = reader.GetBlobReader(handle).Length;
        beforeMaterialize?.Invoke(utf8Length);
        encoded += utf8Length + delimiterChars;
        if (encoded > MetadataSafetyPolicy.MaxTypeNameCharacters)
        {
            value = string.Empty;
            return false;
        }

        value = reader.GetString(handle);
        characters += value.Length + delimiterChars;
        return characters <= MetadataSafetyPolicy.MaxTypeNameCharacters;
    }
}
