namespace DotnetInspector.CSharpBodySlicer.Tests;

internal sealed class ConstructorInitializerOptions
{
    public string Path { get; init; } = "";
}

internal abstract class ConstructorInitializerBase
{
    protected ConstructorInitializerBase(ConstructorInitializerOptions options)
    {
        _ = options;
    }
}

internal sealed class ConstructorInitializerCorpusFixture : ConstructorInitializerBase
{
    public ConstructorInitializerCorpusFixture(string path)
        : base(new ConstructorInitializerOptions { Path = path })
    {
        GC.KeepAlive(path);
    }

    public ConstructorInitializerCorpusFixture(string path, int count) : this(new ConstructorInitializerOptions { Path = path }, count) { }

    private ConstructorInitializerCorpusFixture(ConstructorInitializerOptions options, int count)
        : base(options)
    {
        GC.KeepAlive(count);
    }
}

internal sealed class SameLineMemberCorpusFixture
{
    public void First() { } public void Second() { }
}
