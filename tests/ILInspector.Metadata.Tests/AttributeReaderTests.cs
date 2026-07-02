using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The scope-uniform attribute API: assembly, module, type, and member
/// attributes all render through the same core, exercised here against CoreLib.
/// </summary>
public class AttributeReaderTests
{
    static PEReader OpenCoreLib() => new(File.OpenRead(typeof(object).Assembly.Location));

    [Fact]
    public void RenderAssemblyAttributes_IncludesClsCompliant()
    {
        using var pe = OpenCoreLib();
        var rendered = AttributeReader.RenderAssemblyAttributes(pe.GetMetadataReader());

        // CoreLib is marked [assembly: CLSCompliant(true)].
        Assert.Contains("CLSCompliant(true)", rendered);
    }

    [Fact]
    public void RenderAttributes_ByTypeHandle_FindsFlagsEnum()
    {
        using var pe = OpenCoreLib();
        var reader = pe.GetMetadataReader();
        var handle = reader.TypeDefinitions.Single(h =>
            TypeResolver.GetFullName(reader, reader.GetTypeDefinition(h)) == "System.AttributeTargets");

        Assert.Contains("Flags", AttributeReader.RenderAttributes(reader, handle));
    }

    [Fact]
    public void RenderAttributes_NoNamespaceSet_DoesNotThrow()
    {
        // The namespace accumulator is optional — callers that only want the
        // rendered text omit it.
        using var pe = OpenCoreLib();
        var rendered = AttributeReader.RenderModuleAttributes(pe.GetMetadataReader());

        Assert.NotNull(rendered);
    }

    [Fact]
    public void AttributeDecoder_ResolvesNameAndDecodesArgument()
    {
        // The shared decode primitive: CoreLib is [assembly: CLSCompliant(true)].
        using var pe = OpenCoreLib();
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attr = reader.GetCustomAttribute(handle);
            if (AttributeDecoder.GetAttributeTypeName(reader, attr.Constructor) != "System.CLSCompliantAttribute")
                continue;
            var value = AttributeDecoder.TryDecode(reader, attr);
            Assert.NotNull(value);
            Assert.Equal(true, value!.Value.FixedArguments[0].Value);
            return;
        }
        Assert.Fail("CLSCompliant attribute not found on CoreLib");
    }

    [Theory]
    [InlineData("Attribute", "Attribute")]
    [InlineData("Samples.Attribute", "Samples.Attribute")]
    [InlineData("Samples.WidgetAttribute", "Samples.Widget")]
    [InlineData("Samples.AttributeAttribute", "Samples.Attribute")]
    public void QualifiedAttributeName_DoesNotStripToInvalidName(string fullName, string expected)
    {
        var method = typeof(AttributeReader).GetMethod(
            "GetQualifiedAttributeName",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null, [fullName]));
    }
}
