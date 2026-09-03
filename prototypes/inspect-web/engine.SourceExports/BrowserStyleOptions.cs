using System.Runtime.Versioning;
using System.Text.Json;
using Pipeline = ILInspector.Decompiler.Pipeline;

namespace InspectWeb.Engine.SourceFacade;

/// <summary>
/// Turns the client's selected style option ids into <see cref="Pipeline.PrinterOptions"/> using
/// the library-owned <see cref="Pipeline.StyleOptionCatalog"/>.
/// </summary>
/// <remarks>
/// The ids are exactly the product-owned <see cref="Pipeline.StyleOptionCatalog.Choices"/> exposed
/// in the <c>csharp.style-choices</c> vocabulary section. The host only decodes the transport;
/// identity, defaults, conflicts, and selection semantics remain in the product catalog. An id
/// the catalog does not know is a visible failure rather than a silently ignored selection.
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserStyleOptions
{
    internal static Pipeline.PrinterOptions Resolve(string? styleOptionsJson)
    {
        string[] selected = string.IsNullOrWhiteSpace(styleOptionsJson)
            ? []
            : JsonSerializer.Deserialize(
                styleOptionsJson,
                BrowserSourceJsonContext.Default.StringArray) ?? [];
        return Pipeline.StyleOptionCatalog.ResolveChoices(selected);
    }
}
