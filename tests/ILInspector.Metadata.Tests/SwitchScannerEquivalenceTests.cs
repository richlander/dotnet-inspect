using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using ILInspector.Metadata.LegacyOracles;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The feature-switch scan reads the custom attribute table once instead of
/// walking every type's properties; it must report exactly what the walk did.
/// </summary>
public class SwitchScannerEquivalenceTests
{
    public static TheoryData<string> SharedFrameworkAssemblies()
    {
        var data = new TheoryData<string>();
        string directory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (string path in Directory.EnumerateFiles(directory, "*.dll").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(path));
        return data;
    }

    [Theory]
    [MemberData(nameof(SharedFrameworkAssemblies))]
    public void Scan_EqualsThePropertyWalkOnTheSharedFramework(string fileName)
    {
        string path = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, fileName);
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);

        Assert.Equal(LegacySwitchScanner.Scan(peReader), SwitchScanner.Scan(peReader));
    }

    [Fact]
    public void Scan_ReportsTheSharedFrameworksFeatureSwitches()
    {
        using var stream = File.OpenRead(typeof(object).Assembly.Location);
        using var peReader = new PEReader(stream);

        Assert.Contains(SwitchScanner.Scan(peReader), static s => s.Kind == "Feature Switch");
    }

    [Fact]
    public void Scan_FindsTheDeclaringTypeOfAPropertyWithoutAccessors()
    {
        ImmutableArray<byte> image = ImageWithAccessorlessFeatureSwitch();
        using var peReader = new PEReader(image);

        var switches = SwitchScanner.Scan(peReader);

        Assert.Equal(LegacySwitchScanner.Scan(peReader), switches);
        var feature = Assert.Single(switches);
        Assert.Equal("Feature Switch", feature.Kind);
        Assert.Equal("Fixture.Switch", feature.Switch);
        Assert.Equal("N.Holder.Enabled", feature.Api);
    }

    static ImmutableArray<byte> ImageWithAccessorlessFeatureSwitch()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Switches.dll"), metadata.GetOrAddGuid(Guid.Parse("6a1e0c55-3b5a-4c1a-9a38-2a4d3f0c6e11")), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Switches"), new Version(1, 0, 0, 0), default, default, default, AssemblyHashAlgorithm.None);
        var runtime = metadata.AddAssemblyReference(metadata.GetOrAddString("System.Runtime"), new Version(11, 0, 0, 0), default, default, default, default);
        var attributeType = metadata.AddTypeReference(runtime, metadata.GetOrAddString("System.Diagnostics.CodeAnalysis"), metadata.GetOrAddString("FeatureSwitchDefinitionAttribute"));

        var ctorSignature = new BlobBuilder();
        new BlobEncoder(ctorSignature).MethodSignature(isInstanceMethod: true).Parameters(1, r => r.Void(), p => p.AddParameter().Type().String());
        var ctor = metadata.AddMemberReference(attributeType, metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(ctorSignature));

        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var holder = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Holder"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var propertySignature = new BlobBuilder();
        new BlobEncoder(propertySignature).PropertySignature(isInstanceProperty: false).Parameters(0, r => r.Type().Boolean(), _ => { });
        var property = metadata.AddProperty(PropertyAttributes.None, metadata.GetOrAddString("Enabled"), metadata.GetOrAddBlob(propertySignature));
        metadata.AddPropertyMap(holder, property);

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString("Fixture.Switch");
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(property, ctor, metadata.GetOrAddBlob(value));

        var output = new BlobBuilder();
        new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), new BlobBuilder()).Serialize(output);
        return [.. output.ToArray()];
    }
}
