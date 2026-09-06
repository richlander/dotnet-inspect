using DotnetInspector.PackageQueries;
using ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageAssemblyQueryPlanningTests
{
    [Fact]
    public void Plan_PreservesExactOrderedSelectionAndDeclaredRole()
    {
        PackageAssemblyQueryPlan plan = PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            ["System.Text.Json@10.0.0", "System.Text.Encodings.Web@10.0.0"],
            "net10.0",
            "linux-x64");

        Assert.Equal("system.text.json", plan.Coordinates[0].PackageId);
        Assert.Equal("system.text.encodings.web", plan.Coordinates[1].PackageId);
        Assert.All(plan.Coordinates, coordinate =>
            Assert.Equal("10.0.0", coordinate.Version));
        Assert.Equal("net10.0", plan.TargetFramework);
        Assert.Equal("linux-x64", plan.RuntimeIdentifier);
        Assert.Equal(
            PackageAssemblyPatternRole.ImplementationBody,
            plan.Pattern.Pattern.Role);
        Assert.Same(PackageAssemblyEvaluationBudget.Default, plan.Budget);
    }

    [Fact]
    public void Plan_FreezesCallerSelection()
    {
        string[] packages = ["System.Text.Json@10.0.0"];
        PackageAssemblyQueryPlan plan = PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            packages,
            "net10.0");
        packages[0] = "Other.Package@1.0.0";

        Assert.Equal("system.text.json", Assert.Single(plan.Coordinates).PackageId);
    }

    [Fact]
    public void Plan_RejectsDuplicateNormalizedCoordinates()
    {
        Assert.Throws<ArgumentException>(() => PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            ["Example.Package@1.0", "example.package@1.0.0"],
            "net10.0"));
    }

    [Theory]
    [InlineData("Example.Package")]
    [InlineData("Example.Package@")]
    [InlineData("Example.Package@latest")]
    [InlineData("Example.Package@1.*")]
    public void Plan_RequiresExactVersions(string coordinate)
    {
        Assert.ThrowsAny<ArgumentException>(() => PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            [coordinate],
            "net10.0"));
    }

    [Fact]
    public void Plan_RejectsEmptyOrOversizedSelection()
    {
        Assert.Throws<ArgumentException>(() => PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            [],
            "net10.0"));
        Assert.Throws<ArgumentException>(() => PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            Enumerable.Range(1, PackageAssemblyQuery.MaximumPackages + 1)
                .Select(index => $"Package{index}@1.0.0")
                .ToArray(),
            "net10.0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-registered")]
    public void Plan_RejectsUnknownPattern(string patternId)
    {
        Assert.Throws<ArgumentException>(() => PackageAssemblyQuery.Plan(
            patternId,
            "Json",
            ["Example.Package@1.0.0"],
            "net10.0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Plan_RequiresExplicitSelectionTarget(string targetFramework)
    {
        Assert.ThrowsAny<ArgumentException>(() => PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            ["Example.Package@1.0.0"],
            targetFramework));
    }

    [Fact]
    public void Budget_RejectsUnboundedOrNonpositiveInputs()
    {
        StringLiteralUsePatternBudget semantic = StringLiteralUsePatternBudget.Default;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PackageAssemblyEvaluationBudget(0, 32, semantic, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PackageAssemblyEvaluationBudget(16, 1, semantic, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PackageAssemblyEvaluationBudget(16, 32, semantic, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PackageAssemblyEvaluationBudget(16, 32, semantic, Timeout.InfiniteTimeSpan));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PackageAssemblyEvaluationBudget(16, 32, semantic, TimeSpan.FromMinutes(6)));
    }
}
