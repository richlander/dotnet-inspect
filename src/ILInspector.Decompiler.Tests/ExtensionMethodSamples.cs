namespace ILInspector.Decompiler.Tests;

// Same-assembly samples for extension-method rendering: one genuine [Extension]
// method and one plain static of the same call shape, to exercise the IsExtension
// gate that decides instance-vs-static spelling.
public static class ExtensionMethodSamples
{
    public static int Doubled(this int value) => value * 2;

    public static int Combine(int left, int right) => left + right;
}
