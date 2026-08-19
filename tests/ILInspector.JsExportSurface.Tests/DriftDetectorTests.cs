using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

/// <summary>
/// Verifies <see cref="DriftDetector.IsCovered"/>'s line-membership check (intentionally crude
/// per its own doc comment — this pins the current behavior, not a claim of structural rigor).
/// </summary>
public sealed class DriftDetectorTests
{
    [Fact]
    public void IsCovered_ReturnsTrue_WhenHandWrittenContainsEveryGeneratedLine()
    {
        const string generated = "export interface Foo {\n  bar: string;\n}\n";
        const string handWritten = "// header comment\nexport interface Foo {\n  bar: string;\n}\n// trailer\n";

        Assert.True(DriftDetector.IsCovered(generated, handWritten));
    }

    [Fact]
    public void IsCovered_ReturnsFalse_WhenAGeneratedLineIsMissing()
    {
        const string generated = "export interface Foo {\n  bar: string;\n}\n";
        const string handWritten = "export interface Foo {\n  bar: number;\n}\n";

        Assert.False(DriftDetector.IsCovered(generated, handWritten));
    }

    [Fact]
    public void IsCovered_IgnoresBlankLinesAndSurroundingWhitespace()
    {
        const string generated = "export interface Foo {\n\n  bar: string;\n\n}\n";
        const string handWritten = "export interface Foo {\n    bar: string;   \n}\n";

        Assert.True(DriftDetector.IsCovered(generated, handWritten));
    }

    [Fact]
    public void IsCovered_ReturnsFalse_WhenHandWrittenIsEmpty()
    {
        const string generated = "export interface Foo {\n  bar: string;\n}\n";

        Assert.False(DriftDetector.IsCovered(generated, string.Empty));
    }
}
