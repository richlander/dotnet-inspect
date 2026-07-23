using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class TargetTypedNewPassTests
{
    static readonly ILInspector.Metadata.IAssemblyReferenceResolver RuntimeResolver =
        TestAssemblyReferenceResolvers.RuntimeAssemblies();

    static string PrintRaised(string methodName)
    {
        using var context = new MetadataContext(RuntimeResolver);
        using var source = MetadataSource.Open(typeof(TargetTypedNewFixtures).Assembly.Location, null, RuntimeResolver, context);
        var function = IrImporter.Import(source, typeof(TargetTypedNewFixtures).FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    [Fact]
    public void LocalDeclaration_ShortensToTargetTypedNew()
    {
        string output = PrintRaised(nameof(TargetTypedNewFixtures.LocalDeclaration));

        Assert.Contains("= new(", output);
        Assert.DoesNotContain("new StringBuilder(", output);
    }

    [Fact]
    public void FieldStore_ShortensToTargetTypedNew()
    {
        string output = PrintRaised(nameof(TargetTypedNewFixtures.FieldStore));

        Assert.Contains("= new(", output);
        Assert.DoesNotContain("new StringBuilder(", output);
    }

    [Fact]
    public void ReturnPosition_KeepsExplicitType_OutOfScopeForNow()
    {
        // Return positions are intentionally out of the v1 LHS-only scope: the type
        // is apparent from the signature but not on an assignment target, so the
        // explicit spelling is kept until a follow-up extends the transform there.
        string output = PrintRaised(nameof(TargetTypedNewFixtures.ReturnPosition));

        Assert.Contains("return new StringBuilder(", output);
        Assert.DoesNotContain("return new(", output);
    }

    [Fact]
    public void StructLocal_ShortensToTargetTypedNew()
    {
        string output = PrintRaised(nameof(TargetTypedNewFixtures.StructLocal));

        Assert.Contains("= new(", output);
        Assert.DoesNotContain("new Box(", output);
    }

    [Fact]
    public void ArrayElementStore_ShortensToTargetTypedNew()
    {
        string output = PrintRaised(nameof(TargetTypedNewFixtures.ElementStore));

        Assert.Contains("] = new(", output);
        Assert.DoesNotContain("new Box(", output);
    }

    [Fact]
    public void InterfaceTarget_KeepsExplicitType()
    {
        // Target IList<int> is not the constructed List<int>, so `new()` would bind
        // the wrong type — the explicit spelling must stay.
        string output = PrintRaised(nameof(TargetTypedNewFixtures.InterfaceTargetDeclines));

        Assert.Contains("new List<int>(", output);
        Assert.DoesNotContain("= new(", output);
    }

    [Fact]
    public void MultiDimArray_KeepsArrayCreation()
    {
        // A rectangular-array `newobj` has no target-typed-new form.
        string output = PrintRaised(nameof(TargetTypedNewFixtures.MultiDimArrayDeclines));

        Assert.Contains("new int[", output);
        Assert.DoesNotContain("= new(", output);
    }

    [Fact]
    public void ArgumentPosition_KeepsExplicitType()
    {
        // An argument-position `new()` would participate in overload resolution; the
        // transform never fires there.
        string output = PrintRaised(nameof(TargetTypedNewFixtures.ArgumentPositionDeclines));

        Assert.Contains("new StringBuilder(", output);
    }
}
