namespace ILInspector.Metadata;

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
