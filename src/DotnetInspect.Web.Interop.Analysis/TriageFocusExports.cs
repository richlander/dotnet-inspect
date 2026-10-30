using System.Runtime.InteropServices.JavaScript;
using ILInspector.Decompiler.Annotations;

namespace DotnetInspect.Web.Interop.Analysis;

public static partial class AnalysisExports
{
    /// <summary>Prints a focus annotation using the existing decompiler caret geometry.</summary>
    [JSExport]
    public static string RenderTriageCaret(string sourceLine, int column, int length)
        => TriageCaret(sourceLine, column, length);

    internal static string TriageCaret(string sourceLine, int column, int length)
    {
        if (sourceLine.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("Focus requires one source line.", nameof(sourceLine));
        if (column < 0 || length <= 0 || column > sourceLine.Length - length)
            throw new ArgumentOutOfRangeException(nameof(column), "Focus must lie within the source line.");

        // The issue chip already names the finding. This presentation-only annotation
        // carries no extra label or classifier claim; its extent comes from source provenance.
        IAnnotation focus = new Annotation(new("", AnnotationCategory.Cost, ""), -1);
        return string.Join('\n', AnnotationCaret.Render(sourceLine, "", [focus],
            extents: new Dictionary<IAnnotation, AnnotationAnchor.CaretExtent>
            {
                [focus] = new(column, length),
            }));
    }
}
