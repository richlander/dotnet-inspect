using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

// Cross-assembly enums (System.DayOfWeek, System.AttributeTargets — CoreLib, not
// this test assembly) must flow through the resolver-backed classifier into the
// importer's TypeShapes. The same-assembly enums (CfgPriority, CfgStyles) are
// covered by RefEnumBitwiseTests.
public class CrossAssemblyEnumIntegerTests
{
    static string Render(string methodName)
    {
        using var source = MetadataSource.Open(
            typeof(CfgSampleClass).Assembly.Location,
            null,
            TestAssemblyReferenceResolvers.TrustedPlatformAssemblies());
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

    [Fact]
    public void EnumComparedToInt_CastsIntegerToEnum()
    {
        var output = Render(nameof(CfgSampleClass.CrossAssemblyEnumEqualsInt));

        Assert.Contains("(DayOfWeek)", output);
        // The bare `day == code` (enum == int) would be CS0019.
        Assert.DoesNotContain("== code", output);
    }

    [Fact]
    public void EnumBitwiseWithInt_CastsIntegerToEnum()
    {
        var output = Render(nameof(CfgSampleClass.CrossAssemblyEnumBitwise));

        Assert.Contains("(AttributeTargets)", output);
        // The bare `t & 4` (enum & int) would be CS0019.
        Assert.DoesNotContain("& 4", output);
    }

    [Fact]
    public void EnumCallArgument_CastsIntegerToEnum()
    {
        var output = Render(nameof(CfgSampleClass.CrossAssemblyEnumCallArgument));

        Assert.Contains("(StringComparison)", output);
        // The bare `Equals("x", 5)` picks the static object.Equals(object, object)
        // and is then called on an instance — CS0176.
        Assert.DoesNotContain("\"x\", 5)", output);
    }

    [Fact]
    public void EnumSwitchLabels_CastIntegerToEnum()
    {
        var output = Render(nameof(CfgSampleClass.CrossAssemblyEnumSwitch));

        Assert.Contains("switch (day)", output);
        // The jump-table labels raise as bare ints; `case 1:`/`case 2:` over an
        // enum governing expression is CS0266 (only the literal 0 converts).
        Assert.Contains("case (DayOfWeek)1:", output);
        Assert.Contains("case (DayOfWeek)2:", output);
        Assert.DoesNotContain("case 1:", output);
        Assert.DoesNotContain("case 2:", output);
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
