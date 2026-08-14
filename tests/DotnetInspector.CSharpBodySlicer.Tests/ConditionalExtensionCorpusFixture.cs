namespace DotnetInspector.CSharpBodySlicer.Tests;

internal static class ConditionalExtensionCorpusFixture
{
#if CONDITIONAL_EXTENSION_BRANCH
    extension(ConditionalExtensionCorpusFixture receiver)
#else
    internal static int ReviewedConditionalExtensionProperty
#endif
    {
        get { return 1; }
    }
}
