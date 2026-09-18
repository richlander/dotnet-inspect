using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using ILInspector.JsExportSurface.PolymorphicContractsFixtures;

namespace ILInspector.JsExportSurface.PolymorphicExportFixtures;

public static partial class PolymorphicExports
{
    [JSExport]
    public static string GetDocumentation(bool available)
    {
        PackageDocumentationOutcome outcome = available
            ? new PackageDocumentationOutcome.Available(
                new PackageSubject("Example.Package", null),
                new PackageDocumentation("Summary", 3),
                PackageSourceKind.Package)
            : new PackageDocumentationOutcome.Absent(
                new PackageSubject("Example.Package", "1.0.0"),
                null,
                SourcesTruncated: false);
        return JsonSerializer.Serialize(
            outcome,
            PolymorphicJsonContext.Default.PackageDocumentationOutcome);
    }
}
