namespace ILInspector.Metadata.Tests;

internal static class CustomAttributeCorpusSamples
{
    public enum LocalValue { First }

    [Local(LocalValue.First)]
    public sealed class WithLocalEnum;

    sealed class LocalAttribute(LocalValue value) : Attribute
    {
        public LocalValue Value { get; } = value;
    }
}
