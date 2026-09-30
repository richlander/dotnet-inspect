namespace System.CodeDom.Compiler
{
    [AttributeUsage(AttributeTargets.All)]
    public sealed class GeneratedCodeAttribute(
        string tool,
        string version) : Attribute
    {
        public string Tool { get; } = tool;
        public string Version { get; } = version;
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.All)]
    public sealed class CompilerGeneratedAttribute : Attribute;
}
