using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

/// <summary>
/// Verifies <see cref="DriftDetector.IsCovered"/>'s ordered structural comparison over trimmed,
/// non-blank lines.
/// </summary>
public sealed class DriftDetectorTests
{
    [Fact]
    public void IsCovered_ReturnsTrue_WhenNormalizedLinesExactlyMatch()
    {
        const string generated = "export interface Foo {\n\n  bar: string;\n}\n";
        const string handWritten = "export interface Foo {\n    bar: string;   \n}\n";

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
    public void IsCovered_ReturnsFalse_WhenDeclarationOrderDiffers()
    {
        const string generated = "export interface Foo {\n  bar: string;\n}\nexport interface Baz {\n  qux: number;\n}\n";
        const string handWritten = "export interface Baz {\n  qux: number;\n}\nexport interface Foo {\n  bar: string;\n}\n";

        Assert.False(DriftDetector.IsCovered(generated, handWritten));
    }

    [Fact]
    public void IsCovered_ReturnsFalse_WhenHandWrittenContainsExtraStructure()
    {
        const string generated = "export interface Foo {\n  bar: string;\n}\n";
        const string handWritten = "export interface Foo {\n  bar: string;\n}\nexport interface Extra {\n  more: string;\n}\n";

        Assert.False(DriftDetector.IsCovered(generated, handWritten));
    }

    [Fact]
    public void IsCovered_ReturnsFalse_WhenHandWrittenIsEmpty()
    {
        const string generated = "export interface Foo {\n  bar: string;\n}\n";

        Assert.False(DriftDetector.IsCovered(generated, string.Empty));
    }
}
