using System.Text.RegularExpressions;

namespace DotnetInspector.Metadata;

internal static partial class AssemblyInspectorRegex
{
    [GeneratedRegex(@"https://raw\.githubusercontent\.com/([^/]+)/([^/]+)/([^/]+)/")]
    internal static partial Regex GitHubRawUrl();
}
