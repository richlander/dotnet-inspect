namespace CSharpText.Tests;

internal static class ConditionalCorpusFixture
{
    internal static int Before() => 0;

#if CONDITIONAL_CORPUS_FEATURE
    internal static int SelectedBranch() => 1;
#else
    internal static int SelectedBranch() => 2;
#endif

    internal static int After() => 3;

    internal static int ContainsConditional()
    {
#if CONDITIONAL_CORPUS_FEATURE
        return 4;
#else
        return 5;
#endif
    }
}
