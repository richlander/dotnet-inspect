using System.Reflection.PortableExecutable;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Fidelity")]
public class FidelityCheckSelectionGuardTests
{
    static string TestAssembly => typeof(FidelityCheckSelectionGuardTests).Assembly.Location;

    [Fact]
    public void Evaluate_UnfilteredLargeAssembly_RejectsBeforeCompileBack()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => FidelityCheck.Evaluate(TestAssembly));

        Assert.Contains(TestAssembly, error.Message);
        Assert.Contains($"budget of {FidelityCheck.MaxEvaluationTypeCount}", error.Message);
    }

    [Fact]
    public void Evaluate_AllMatchingFilter_CannotBypassBudget()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => FidelityCheck.Evaluate(TestAssembly, _ => true));

        Assert.Contains("after filtering", error.Message);
        Assert.Contains("all-matching predicate", error.Message);
    }

    [Fact]
    public void Evaluate_ReflectionNestedTypeSpelling_RejectsZeroMatch()
    {
        string reflectionName = typeof(FidelityCheckSelectionGuardFixture.Inner).FullName!;

        var error = Assert.Throws<ArgumentException>(
            "typeFilter",
            () => FidelityCheck.Evaluate(TestAssembly, type => type == reflectionName));

        Assert.Contains("selected no processable top-level class or struct", error.Message);
        Assert.Contains("select their containing top-level type", error.Message);
        Assert.Contains("Outer.Inner", error.Message);
    }

    [Fact]
    public void Evaluate_FilterMatchingOnlyGeneratedType_RejectsZeroProcessableSelection()
    {
        string generatedType = typeof(FidelityCheckSelectionGuardGeneratedFixture).FullName!;

        var error = Assert.Throws<ArgumentException>(
            "typeFilter",
            () => FidelityCheck.Evaluate(TestAssembly, type => type == generatedType));

        Assert.Contains("selected no processable top-level class or struct", error.Message);
    }

    [Fact]
    public void Evaluate_PeWithoutManagedMetadata_PreservesUnfilteredBehaviorButRejectsFilter()
    {
        string path = CreatePeWithoutManagedMetadata();

        try
        {
            using (var pe = new PEReader(File.OpenRead(path)))
                Assert.False(pe.HasMetadata);

            Assert.Empty(FidelityCheck.Evaluate(path));

            var error = Assert.Throws<ArgumentException>(
                "typeFilter",
                () => FidelityCheck.Evaluate(path, _ => true));

            Assert.Contains("selected no processable top-level class or struct", error.Message);
            Assert.Contains("does not contain managed metadata", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Evaluate_FocusedFilterOnLargeAssembly_StillRuns()
    {
        string fixtureType = typeof(FidelityCheckSelectionGuardFixture).FullName!;

        var result = Assert.Single(
            FidelityCheck.Evaluate(TestAssembly, type => type == fixtureType),
            row => row.Type == fixtureType && row.Method == nameof(FidelityCheckSelectionGuardFixture.Increment));

        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
    }

    static string CreatePeWithoutManagedMetadata()
    {
        byte[] bytes = File.ReadAllBytes(TestAssembly);

        using (var pe = new PEReader(new MemoryStream(bytes)))
        {
            var header = pe.PEHeaders.PEHeader!;
            // Data directories follow the PE32/PE32+ optional-header fixed part;
            // COR20 is directory 14.
            int directoryBase = pe.PEHeaders.PEHeaderStartOffset
                + (header.Magic == PEMagic.PE32Plus ? 112 : 96);
            Array.Clear(bytes, directoryBase + (14 * 8), 8);
        }

        string path = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-check-no-metadata-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}

public sealed class FidelityCheckSelectionGuardFixture
{
    public static int Increment(int value) => value + 1;

    public sealed class Inner;
}

[System.Runtime.CompilerServices.CompilerGenerated]
public sealed class FidelityCheckSelectionGuardGeneratedFixture;
