namespace DotnetInspector.Metadata;

public static class MemberFilters
{
    public static bool IsCompilerGenerated(string name)
    {
        return name.StartsWith('<') ||
               name.StartsWith("__") ||
               name.StartsWith("s_") ||
               name.Contains("__BackingField") ||
               name == "value__";
    }
}
