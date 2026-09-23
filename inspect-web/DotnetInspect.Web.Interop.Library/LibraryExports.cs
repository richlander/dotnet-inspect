using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Sections;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Library;

/// <summary>
/// Inspects one caller-supplied managed image as a transient standalone Library.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class LibraryExports
{
    [JSExport]
    public static async Task<string> OpenUploadedLibrary(
        string declaredName,
        byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        InspectionEnvelope<EmbeddedLibraryInspectionResult> inspection =
            await EmbeddedLibraryInspection.ExecuteAsync(
                declaredName,
                ImmutableArray.CreateRange(content),
                BrowserApiSurfacePolicy.Limits).ConfigureAwait(false);
        return JsonSerializer.Serialize(
            BrowserLibraryWireProjection.Project(inspection),
            BrowserLibraryJsonContext.Default.BrowserUploadedLibraryInspection);
    }
}
