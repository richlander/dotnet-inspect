using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

#nullable enable

/// <summary>
/// Slice 2 of #1113: a non-nullable value-type optional parameter encodes its
/// `= default` as a null constant, which the renderer printed as `= null`
/// (CS1750). The fix renders `= default` for value types while keeping `= null`
/// for reference types and Nullable&lt;T&gt; (both accept the null literal). Uses
/// this test assembly as the subject, like <see cref="NullabilityTests"/>.
/// </summary>
public sealed class DefaultValueRenderingTests
{
    static readonly ApiSurface Surface;

    static DefaultValueRenderingTests()
    {
        using var stream = File.OpenRead(typeof(DefaultValueRenderingTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        Surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    static string Signature(string method) => Assert.IsType<string>(Surface.Types
        .First(t => t.Name == nameof(DefaultValueFixtures))
        .Members.First(m => m.Name == method).Signature);

    static ApiParameter Parameter(string method, int index = 0) => Surface.Types
        .First(t => t.Name == nameof(DefaultValueFixtures))
        .Members.First(m => m.Name == method).SignatureModel!.Parameters[index];

    [Fact]
    public void ValueTypeStruct_Default_RendersDefaultNotNull()
    {
        var sig = Signature(nameof(DefaultValueFixtures.ValueTypeStructDefault));
        Assert.Contains("ct = default", sig);
        Assert.DoesNotContain("ct = null", sig);
        Assert.Equal("default", Parameter(nameof(DefaultValueFixtures.ValueTypeStructDefault)).DefaultValueText);
    }

    [Fact]
    public void CustomStruct_Default_RendersDefault()
    {
        var sig = Signature(nameof(DefaultValueFixtures.CustomStructDefault));
        Assert.Contains("s = default", sig);
        Assert.DoesNotContain("s = null", sig);
    }

    // --- regression canaries: these must stay `= null` / unchanged ---

    [Fact]
    public void RefType_Null_StaysNull()
    {
        var sig = Signature(nameof(DefaultValueFixtures.RefTypeNull));
        Assert.Contains("r = null", sig);
        Assert.DoesNotContain("= default", sig);
    }

    [Fact]
    public void NullableValue_Null_StaysNull()
    {
        // Nullable<T> is a value type but accepts the null literal as its default.
        var sig = Signature(nameof(DefaultValueFixtures.NullableValueNull));
        Assert.Contains("n = null", sig);
        Assert.DoesNotContain("n = default", sig);
        Assert.Equal("null", Parameter(nameof(DefaultValueFixtures.NullableValueNull)).DefaultValueText);
    }

    [Fact]
    public void Int_Default_Unchanged()
    {
        Assert.Contains("i = 5", Signature(nameof(DefaultValueFixtures.IntDefault)));
        Assert.Equal("5", Parameter(nameof(DefaultValueFixtures.IntDefault)).DefaultValueText);
    }

    [Fact]
    public void Bool_Default_Unchanged()
        => Assert.Contains("b = true", Signature(nameof(DefaultValueFixtures.BoolDefault)));

    [Fact]
    public void Enum_NonZeroDefault_RendersMemberName()
    {
        var sig = Signature(nameof(DefaultValueFixtures.EnumNonZeroDefault));
        Assert.Contains("color = ILInspector.Metadata.Tests.DefaultValueFixtures.SampleColor.Green", sig);
        Assert.DoesNotContain("color = 1", sig);
        Assert.Equal(
            "ILInspector.Metadata.Tests.DefaultValueFixtures.SampleColor.Green",
            Parameter(nameof(DefaultValueFixtures.EnumNonZeroDefault)).DefaultValueText);
    }

    [Fact]
    public void Enum_ZeroDefault_RendersZeroMemberName()
    {
        var sig = Signature(nameof(DefaultValueFixtures.EnumZeroDefault));
        Assert.Contains("color = ILInspector.Metadata.Tests.DefaultValueFixtures.SampleColor.Red", sig);
        Assert.DoesNotContain("color = 0", sig);
    }

    [Fact]
    public void Enum_UnnamedFlagsDefault_RendersCast()
    {
        var sig = Signature(nameof(DefaultValueFixtures.EnumUnnamedFlagsDefault));
        Assert.Contains("flags = (ILInspector.Metadata.Tests.DefaultValueFixtures.SampleFlags)3", sig);
    }

    [Fact]
    public void Enum_ExternalDefault_RendersCast()
    {
        var sig = Signature(nameof(DefaultValueFixtures.ExternalEnumDefault));
        Assert.Contains("day = (System.DayOfWeek)1", sig);
        Assert.DoesNotContain("day = 1", sig);
    }

    [Theory]
    [InlineData("\u202e", "\\u202E")]
    [InlineData("\\u202e", "\\\\u202e")]
    public void HostileEnumDefaults_ContainRawTypeAndMemberSlots(
        string rawSuffix,
        string containedSuffix)
    {
        string path = EmitHostileEnumDefaults(rawSuffix);
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            MetadataReader reader = peReader.GetMetadataReader();
            ApiSurface extracted = ApiSurfaceExtractor.Extract(
                peReader,
                includeAll: true);
            ApiType extractedHost = Assert.Single(
                extracted.Types,
                type => type.Namespace == "N" && type.Name == "Host");
            TypeDefinitionHandle hostHandle = reader.TypeDefinitions.Single(handle =>
                TypeResolver.GetFullName(
                    reader,
                    reader.GetTypeDefinition(handle)) == "N.Host");
            ApiType queriedHost = MetadataDeclarationQuery.GetTypeSurface(
                reader,
                hostHandle,
                includeNonPublicMembers: true);

            string rawType = $"N.Flag{rawSuffix}";
            string containedType = $"N.Flag{containedSuffix}";
            string namedDefault = $"{containedType}.One{containedSuffix}";
            string castDefault = $"({containedType})2";
            string externalCastDefault =
                $"(N.ExternalFlag{containedSuffix})2";
            ApiMember extractedNamed = Assert.Single(
                extractedHost.Members,
                member => member.Name == "NamedDefault");
            ApiMember extractedCast = Assert.Single(
                extractedHost.Members,
                member => member.Name == "CastDefault");
            ApiMember extractedExternalCast = Assert.Single(
                extractedHost.Members,
                member => member.Name == "ExternalCastDefault");
            ApiMember queriedCast = Assert.Single(
                queriedHost.Members,
                member => member.Name == "CastDefault");
            string extractedNamedSignature =
                Assert.IsType<string>(extractedNamed.Signature);
            string extractedCastSignature =
                Assert.IsType<string>(extractedCast.Signature);
            string extractedExternalCastSignature =
                Assert.IsType<string>(extractedExternalCast.Signature);
            string queriedCastSignature =
                Assert.IsType<string>(queriedCast.Signature);

            Assert.Equal(
                rawType,
                Assert.Single(extractedNamed.SignatureModel!.Parameters).Type);
            Assert.Equal(
                namedDefault,
                Assert.Single(extractedNamed.SignatureModel.Parameters).DefaultValueText);
            Assert.Contains($"= {namedDefault}", extractedNamedSignature);
            Assert.Contains($"= {castDefault}", extractedCastSignature);
            Assert.Contains(
                $"= {externalCastDefault}",
                extractedExternalCastSignature);
            Assert.Contains($"= {castDefault}", queriedCastSignature);
            Assert.DoesNotContain('\u202e', extractedNamedSignature);
            Assert.DoesNotContain('\u202e', extractedCastSignature);
            Assert.DoesNotContain('\u202e', extractedExternalCastSignature);
            Assert.DoesNotContain('\u202e', queriedCastSignature);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static string EmitHostileEnumDefaults(string suffix)
    {
        var assemblyName = new AssemblyName("HostileEnumDefaults");
        var assembly = new PersistedAssemblyBuilder(
            assemblyName,
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var externalAssemblyName = new AssemblyName("ExternalHostileEnumDefaults");
        var externalAssembly = new PersistedAssemblyBuilder(
            externalAssemblyName,
            typeof(object).Assembly);
        var externalModule = externalAssembly.DefineDynamicModule(
            externalAssemblyName.Name!);
        var externalEnumBuilder = externalModule.DefineEnum(
            $"N.ExternalFlag{suffix}",
            TypeAttributes.Public,
            typeof(int));
        externalEnumBuilder.DefineLiteral("One", 1);
        Type externalEnumType = externalEnumBuilder.CreateType();
        var enumBuilder = module.DefineEnum(
            $"N.Flag{suffix}",
            TypeAttributes.Public,
            typeof(int));
        enumBuilder.DefineLiteral($"One{suffix}", 1);
        Type enumType = enumBuilder.CreateType();

        var host = module.DefineType(
            "N.Host",
            TypeAttributes.Public | TypeAttributes.Class);
        DefineOptionalEnumMethod(host, "NamedDefault", enumType, 1);
        DefineOptionalEnumMethod(host, "CastDefault", enumType, 2);
        DefineOptionalEnumMethod(
            host,
            "ExternalCastDefault",
            externalEnumType,
            2);
        host.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"HostileEnumDefaults-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static void DefineOptionalEnumMethod(
        TypeBuilder host,
        string name,
        Type enumType,
        int defaultValue)
    {
        var method = host.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [enumType]);
        method
            .DefineParameter(
                1,
                ParameterAttributes.Optional | ParameterAttributes.HasDefault,
                "value")
            .SetConstant(defaultValue);
        method.GetILGenerator().Emit(OpCodes.Ret);
    }
}

public class DefaultValueFixtures
{
    public struct PlainStruct { public int X; }

    public void ValueTypeStructDefault(System.Threading.CancellationToken ct = default) { }
    public void CustomStructDefault(PlainStruct s = default) { }
    public void RefTypeNull(string? r = null) { }
    public void NullableValueNull(int? n = null) { }
    public void IntDefault(int i = 5) { }
    public void BoolDefault(bool b = true) { }
    public void EnumNonZeroDefault(SampleColor color = SampleColor.Green) { }
    public void EnumZeroDefault(SampleColor color = SampleColor.Red) { }
    public void EnumUnnamedFlagsDefault(SampleFlags flags = (SampleFlags)3) { }
    public void ExternalEnumDefault(DayOfWeek day = DayOfWeek.Monday) { }

    public enum SampleColor
    {
        Red = 0,
        Green = 1,
        Blue = 2
    }

    [Flags]
    public enum SampleFlags
    {
        One = 1,
        Two = 2
    }
}
