namespace DotnetInspector.CSharpBodySlicer.Tests;

internal partial class PartialExtensionBlockCorpusFixture
{
    extension(int value)
    {
        public int ReviewedPartialExtensionProperty => value * 2;
    }
}

internal static partial class PartialExtensionBlockCorpusFixture
{
}
