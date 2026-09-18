using DotnetInspector.Services;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

// Cross-assembly enums must flow through the resolver-backed classifier and
// definition-fact reader into the importer's type, member, and underlying-type
// maps. Same-assembly enum behavior is covered by RefEnumBitwiseTests.
public class CrossAssemblyEnumIntegerTests
{
    static string Render(
        string methodName,
        IAssemblyReferenceResolver? resolver = null)
    {
        using var source = MetadataSource.Open(
            typeof(CfgSampleClass).Assembly.Location,
            null,
            resolver ?? TestAssemblyReferenceResolvers.TrustedPlatformAssemblies());
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    static IrFunction Import(string methodName, IAssemblyReferenceResolver resolver)
    {
        using var source = MetadataSource.Open(
            typeof(CfgSampleClass).Assembly.Location,
            null,
            resolver);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return function!;
    }

    static IAssemblyReferenceResolver ExplicitAssemblyContext(
        params string[] assemblyPaths)
        => new DotnetInspector.Services.AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(
                typeof(CfgSampleClass).Assembly.Location)
            {
                CorpusAssemblyPaths = assemblyPaths,
                IncludeDepsJsonAssets = false,
                IncludeSiblingAssemblies = false,
            });

    [Fact]
    public void EnumComparedToInt_CastsIntegerToEnum()
    {
        var output = Render(nameof(CfgSampleClass.CrossAssemblyEnumEqualsInt));

        Assert.Contains("(DayOfWeek)", output);
        // The bare `day == code` (enum == int) would be CS0019.
        Assert.DoesNotContain("== code", output);
    }

    [Fact]
    public void EnumBitwiseWithInt_RendersNamedMember()
    {
        var output = Render(nameof(CfgSampleClass.CrossAssemblyEnumBitwise));

        Assert.Contains("t & AttributeTargets.Class", output);
        Assert.DoesNotContain("(AttributeTargets)", output);
        // The bare `t & 4` (enum & int) would be CS0019.
        Assert.DoesNotContain("& 4", output);
    }

    [Fact]
    public void EnumCallArgument_RendersNamedMember()
    {
        var output = Render(nameof(CfgSampleClass.CrossAssemblyEnumCallArgument));

        Assert.Contains("StringComparison.OrdinalIgnoreCase", output);
        Assert.DoesNotContain("(StringComparison)", output);
        // The bare `Equals("x", 5)` picks the static object.Equals(object, object)
        // and is then called on an instance — CS0176.
        Assert.DoesNotContain("\"x\", 5)", output);
    }

    [Fact]
    public void EnumCallArgument_UnnamedValueRemainsCast()
    {
        var output = Render(nameof(CfgSampleClass.CrossAssemblyEnumUnnamed));

        Assert.Contains("(StringComparison)123", output);
        Assert.DoesNotContain("StringComparison.", output);
    }

    [Fact]
    public void EnumCallArgument_UnresolvedDefinitionRemainsCast()
    {
        using var source = MetadataSource.Open(
            typeof(CfgSampleClass).Assembly.Location,
            null,
            TestAssemblyReferenceResolvers.None);
        var function = IrImporter.Import(
            source,
            typeof(CfgSampleClass).FullName!,
            nameof(CfgSampleClass.CrossAssemblyEnumCallArgument));

        Assert.NotNull(function);
        Assert.DoesNotContain(
            function!.EnumMembers.Keys,
            static type => type.Name == nameof(StringComparison));

        IrPasses.Run(function);
        function.CheckInvariant();
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("(StringComparison)5", output);
        Assert.DoesNotContain("StringComparison.OrdinalIgnoreCase", output);
    }

    [Fact]
    public void EnumSwitchLabels_RenderNamedMembers()
    {
        var output = Render(nameof(CfgSampleClass.CrossAssemblyEnumSwitch));

        Assert.Contains("switch (day)", output);
        // The jump-table labels raise as bare ints; `case 1:`/`case 2:` over an
        // enum governing expression is CS0266 (only the literal 0 converts).
        Assert.Contains("case DayOfWeek.Sunday:", output);
        Assert.Contains("case DayOfWeek.Monday:", output);
        Assert.Contains("case DayOfWeek.Tuesday:", output);
        Assert.DoesNotContain("(DayOfWeek)", output);
    }

    [Fact]
    public void RoslynTypeKind_WithDefiningAssembly_RendersNamedMembers()
    {
        var output = Render(
            nameof(CfgSampleClass.CrossAssemblyRoslynEnum),
            ExplicitAssemblyContext(
                typeof(Microsoft.CodeAnalysis.TypeKind).Assembly.Location));

        Assert.Contains("TypeKind.Enum", output);
        Assert.Contains("TypeKind.Struct", output);
        Assert.DoesNotContain("(TypeKind)", output);
    }

    [Fact]
    public void UnsignedHighBitMember_WithDefiningAssembly_RendersNamedMember()
    {
        var output = Render(
            nameof(CfgSampleClass.CrossAssemblyExternalUIntConstant),
            ExplicitAssemblyContext(
                typeof(ILInspector.Decompiler.Fixtures.CrossAssemblyEnums.ExternalUInt)
                    .Assembly.Location));

        Assert.Contains("return ExternalUInt.Top;", output);
        Assert.DoesNotContain("(ExternalUInt)", output);
    }

    [Fact]
    public void ResolvedCrossAssemblyEnum_PopulatesImporterEnumShape()
    {
        var function = Import(
            nameof(CfgSampleClass.CrossAssemblyEnumConditional),
            TestAssemblyReferenceResolvers.TrustedPlatformAssemblies());

        var commandBehavior = Assert.Single(
            function.TypeShapes!,
            pair => pair.Key.Namespace == "System.Data" && pair.Key.Name == "CommandBehavior");
        Assert.Equal(TypeShape.Enum, commandBehavior.Value);
        var members = Assert.Single(
            function.EnumMembers,
            pair => pair.Key.Namespace == "System.Data" && pair.Key.Name == "CommandBehavior");
        Assert.Equal(
            nameof(System.Data.CommandBehavior.SequentialAccess),
            members.Value[(long)System.Data.CommandBehavior.SequentialAccess]);
        var underlyingType = Assert.Single(
            function.EnumUnderlyingTypes,
            pair => pair.Key.Namespace == "System.Data" && pair.Key.Name == "CommandBehavior");
        Assert.Equal("Int32", underlyingType.Value.Name);

        IrPasses.Run(function);
        function.CheckInvariant();
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("(CommandBehavior)", output);
    }

    [Fact]
    public void UnresolvedCrossAssemblyStruct_DoesNotPopulateEnumShape()
    {
        var function = Import(
            nameof(CfgSampleClass.CrossAssemblyStructConditional),
            TestAssemblyReferenceResolvers.None);

        var dateTime = Assert.Single(
            function.TypeShapes!,
            pair => pair.Key.Namespace == "System" && pair.Key.Name == "DateTime");
        Assert.Equal(TypeShape.Unknown, dateTime.Value);

        IrPasses.Run(function);
        function.CheckInvariant();
        string output = CSharpPrinter.Print(function).Output!;
        Assert.DoesNotContain("(DateTime)", output);
    }
}
