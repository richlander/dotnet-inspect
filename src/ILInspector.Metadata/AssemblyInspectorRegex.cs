using System.Text.RegularExpressions;

namespace ILInspector.Metadata;

internal static partial class AssemblyInspectorRegex
{
    [GeneratedRegex(@"https://raw\.githubusercontent\.com/([^/]+)/([^/]+)/([^/]+)/")]
    internal static partial Regex GitHubRawUrl();
}
