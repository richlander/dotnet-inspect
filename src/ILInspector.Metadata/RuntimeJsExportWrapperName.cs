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
}
