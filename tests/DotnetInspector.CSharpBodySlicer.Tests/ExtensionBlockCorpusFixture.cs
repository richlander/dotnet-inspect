namespace DotnetInspector.CSharpBodySlicer.Tests;

[AttributeUsage(AttributeTargets.GenericParameter)]
internal sealed class ExtensionTypeParameterAttribute(bool condition) : Attribute
{
    public bool Condition { get; } = condition;
}

internal static class ExtensionBlockCorpusFixture
{
    extension<[ExtensionTypeParameter(1 > 0)] T>(T receiver)
    {
        public void ReviewedExtensionMember()
        {
            GC.KeepAlive(receiver);
        }
    }
}
