namespace ILInspector.Decompiler.Tests
{
    public static class EmbeddedAttributeSkeletonFixture
    {
        public static int Value() => 42;
    }
}

namespace Microsoft.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    internal sealed class EmbeddedAttribute : Attribute
    {
    }
}
