namespace ILInspector.Metadata;

/// <summary>
/// One SDK-generated runtime-wrapper retention registration, before its
/// textual target is matched to an extracted type definition.
/// </summary>
public sealed record RuntimeJsExportWrapperRegistration(
    string MemberName,
    string TargetTypeName,
    string TargetAssemblyName);

/// <summary>
/// Recognizes the SDK generator's wrapper-name grammar without treating the
/// name as proof that the wrapper publishes a particular export.
/// </summary>
public static class RuntimeJsExportWrapperName
{
    public static bool IsCandidateFor(
        string wrapperName,
        string exportName)
    {
        string prefix = $"__Wrapper_{exportName}_";
        if (!wrapperName.StartsWith(prefix, StringComparison.Ordinal)
            || wrapperName.Length == prefix.Length)
        {
            return false;
        }

        for (int i = prefix.Length; i < wrapperName.Length; i++)
        {
            if (!char.IsAsciiDigit(wrapperName[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The unsigned decimal suffix the SDK generator appends to a wrapper name.
    /// The generator derives it from the export's marshaled signature and passes
    /// the same value as <c>BindManagedFunction</c>'s signature hash, so an
    /// exact match ties the registration to this wrapper rather than to a
    /// neighbouring one.
    /// </summary>
    /// <remarks>
    /// Parsed as <see cref="uint"/> because the generator formats the hash
    /// unsigned while the IL literal is a signed <c>int32</c>.
    /// <c>GeneratedJsExportAuthenticationTests.Build_RejectsRegistrationWithMismatchedSignatureHash</c>
    /// gates the comparison.
    /// </remarks>
    public static bool TryGetSignatureHash(
        string wrapperName,
        string exportName,
        out uint signatureHash)
    {
        signatureHash = 0;
        if (!IsCandidateFor(wrapperName, exportName))
            return false;

        return uint.TryParse(
            wrapperName.AsSpan($"__Wrapper_{exportName}_".Length),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out signatureHash);
    }
}
