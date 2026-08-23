using ILInspector.Metadata;

namespace ILInspector.JsExportSurface;

public static class JsonWireMemberRules
{
    /// <summary>
    /// The direction-independent membership rule: true when the member appears
    /// in at least one direction's contract. Discovery uses this union so that
    /// no type reachable through a direction-sensitive member is left
    /// undeclared.
    /// </summary>
    public static bool IsSerialized(ApiMember member) =>
        IsSerialized(member, JsonWireDirection.Both);

    /// <summary>
    /// True when the member appears in the contract for at least one of the
    /// requested <paramref name="directions"/>.
    /// </summary>
    /// <remarks>
    /// A member whose <c>[JsonIgnore]</c> or <c>[JsonInclude]</c> metadata is
    /// duplicated or malformed is excluded from every direction: the intent is
    /// real but unreadable, and <c>DtsEmitter</c> refuses to emit such a
    /// declaration at all. Gated by
    /// <c>JsonWireMemberRulesTests.DirectionalIgnoreConditionsSelectDirections</c>.
    /// </remarks>
    public static bool IsSerialized(
        ApiMember member,
        JsonWireDirection directions)
    {
        if (member.IsStatic
            || member.IsCompilerGenerated
            || HasUnsupportedJsonIgnoreMetadata(member)
            || HasUnsupportedJsonIncludeMetadata(member)
            || (PresentDirections(member) & directions)
                == JsonWireDirection.None)
        {
            return false;
        }

        return member.Kind switch
        {
            "property" => IsSerializedProperty(member),
            "field" => member.HasJsonInclude
                && IsSourceGeneratorAccessible(member.Accessibility),
            _ => false,
        };
    }

    /// <summary>
    /// True when the member's presence differs between serialization and
    /// deserialization, which is exactly when one declaration cannot describe
    /// both directions.
    /// </summary>
    public static bool IsDirectionSensitive(ApiMember member) =>
        IsSerialized(member, JsonWireDirection.Serialize)
            != IsSerialized(member, JsonWireDirection.Deserialize);

    /// <summary>
    /// True when the member carries authentic <c>[JsonIgnore]</c> metadata that
    /// cannot be honored: more than one row, or a row whose constructor or
    /// <c>Condition</c> argument could not be read.
    /// </summary>
    public static bool HasUnsupportedJsonIgnoreMetadata(ApiMember member) =>
        member.JsonIgnoreConditions.Count > 1
        || member.JsonIgnoreConditions.Contains(null);

    /// <summary>
    /// True when the member carries an authentic <c>[JsonInclude]</c> row whose
    /// constructor or value blob could not be read.
    /// </summary>
    public static bool HasUnsupportedJsonIncludeMetadata(ApiMember member) =>
        member.HasMalformedJsonInclude;

    /// <summary>
    /// The directions the member's <c>[JsonIgnore]</c> condition leaves intact.
    /// </summary>
    /// <remarks>
    /// <c>WhenWritingDefault</c> and <c>WhenWritingNull</c> are value-dependent
    /// rather than declaration-dependent, so a static projection cannot promise
    /// the member is present in either direction and conservatively drops it,
    /// preserving the behavior that predates directional handling.
    /// </remarks>
    static JsonWireDirection PresentDirections(ApiMember member) =>
        member.JsonIgnoreConditions is [var condition]
            ? condition switch
            {
                JsonWireIgnoreCondition.Never => JsonWireDirection.Both,
                JsonWireIgnoreCondition.WhenWriting =>
                    JsonWireDirection.Deserialize,
                JsonWireIgnoreCondition.WhenReading =>
                    JsonWireDirection.Serialize,
                _ => JsonWireDirection.None,
            }
            : JsonWireDirection.Both;

    static bool IsSerializedProperty(ApiMember member)
    {
        int? indexParameterCount =
            member.IndexParameterCount
            ?? member.SignatureModel?.ParameterCount;
        if (indexParameterCount != 0)
            return false;

        if (member.HasGetter is false)
            return false;

        if (member.HasJsonInclude)
        {
            string? getterAccessibility = member.HasGetter is true
                ? member.GetterAccessibility
                : member.Accessibility;
            return IsSourceGeneratorAccessible(getterAccessibility);
        }

        return member.HasGetter is true
            ? member.GetterAccessibility is null
            : member.Accessibility is null;
    }

    static bool IsSourceGeneratorAccessible(string? accessibility) =>
        accessibility is null or "internal" or "protected internal";
}
