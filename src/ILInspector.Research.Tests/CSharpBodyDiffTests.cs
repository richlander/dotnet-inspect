using DotnetInspector.Fixtures;

namespace ILInspector.Research.Tests;

public class CSharpBodyDiffTests
{
    [Fact]
    public void CompareAssemblies_SelfDiffHasNoRows()
    {
        var path = DiffFixturePath("DiffFixtures.V1");

        var diff = CSharpBodyDiff.CompareAssemblies(path, path);

        Assert.True(diff.IsExact);
        Assert.Empty(diff.Rows);
    }

    [Fact]
    public void CompareAssemblies_ConstantChangeSurfacesProductCSharpRows()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("ConstantValue", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove
            && row.Text.Contains("1", StringComparison.Ordinal));
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("ConstantValue", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Add
            && row.Text.Contains("2", StringComparison.Ordinal));
        Assert.All(diff.Rows.Where(row => row.Member.Contains("ConstantValue", StringComparison.Ordinal)), row =>
        {
            Assert.StartsWith("ConstantValue~", row.Anchor.StableSelector, StringComparison.Ordinal);
            Assert.StartsWith("M:DiffFixtureSample.DiffSample.ConstantValue()", row.Anchor.CanonicalSignature, StringComparison.Ordinal);
            Assert.Contains("DiffFixtureSample|neutral|", row.AssemblyIdentity, StringComparison.Ordinal);
            Assert.StartsWith(row.AssemblyIdentity + "|", row.StableMemberKey, StringComparison.Ordinal);
            Assert.Contains(row.Anchor.CanonicalSignature + "#", row.StableMemberKey, StringComparison.Ordinal);
            Assert.Equal(10, row.Anchor.Fingerprint.Length);
            Assert.StartsWith("csharp.line.", row.ChangeId, StringComparison.Ordinal);
            Assert.NotEmpty(row.Message);
            Assert.NotNull(row.SourceCoordinate);
            Assert.Equal("Full", row.Fidelity);
        });
    }

    [Fact]
    public void CompareAssemblies_GenericParameterRenameDoesNotBreakMethodIdentity()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

        Assert.DoesNotContain(diff.Rows, row => row.Member.Contains("GenericIdentity", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareAssemblies_GenericMethodCanonicalSignatureUsesMethodGenericParameter()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

        var row = Assert.Single(diff.Rows, row =>
            row.Member.Contains("GenericParamBody`1", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove);
        Assert.StartsWith("GenericParamBody~", row.Anchor.StableSelector, StringComparison.Ordinal);
        Assert.Equal("M:DiffFixtureSample.DiffSample.GenericParamBody<!!0>(!!0)", row.Anchor.CanonicalSignature);
    }

    [Fact]
    public void CompareAssemblies_TokenTargetChangeIsNotSkippedByFastPath()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("StringToken", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove
            && row.Text.Contains("\"alpha\"", StringComparison.Ordinal));
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("StringToken", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Add
            && row.Text.Contains("\"beta\"", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareAssemblies_ProtectedMethodsAreIncludedInDefaultSurface()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ProtectedSample" });

        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("ProtectedConstant", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove
            && row.Text.Contains("1", StringComparison.Ordinal));
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("ProtectedConstant", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Add
            && row.Text.Contains("2", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareAssemblies_GenericArityOverloadsDoNotCollide()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GenericOverloadSample" });

        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("GenericOverloadSample.M()", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove
            && row.Text.Contains("1", StringComparison.Ordinal));
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("GenericOverloadSample.M()", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Add
            && row.Text.Contains("2", StringComparison.Ordinal));
        Assert.DoesNotContain(diff.Rows, row => row.Member.Contains("GenericOverloadSample.M`1", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareAssemblies_DuplicateInputPathsDoNotThrowOrCreateRows()
    {
        var path = DiffFixturePath("DiffFixtures.V1");

        var diff = CSharpBodyDiff.CompareAssemblies([path, path], [path, path], typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

        Assert.Empty(diff.Rows);
    }

    [Fact]
    public void CompareAssemblies_OneSidedDuplicateInputPathsKeepMatchingStable()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies([v1, v1], [v2], typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("ConstantValue", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove
            && row.Text.Contains("1", StringComparison.Ordinal));
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("ConstantValue", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Add
            && row.Text.Contains("2", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareAssemblies_DistinctPathsWithSameAssemblyIdentityRemainOccurrenceDistinct()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var copy = Path.Combine(Path.GetTempPath(), $"DiffFixtureSample-{Guid.NewGuid():N}.dll");
        File.Copy(v1, copy);
        try
        {
            var diff = CSharpBodyDiff.CompareAssemblies([v1, copy], [v1], typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

            Assert.Contains(diff.Rows, row => row.AssemblyIdentity.EndsWith("#1", StringComparison.Ordinal));
            Assert.All(diff.Rows, row => Assert.StartsWith(row.AssemblyIdentity + "|", row.StableMemberKey, StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [Fact]
    public void CompareAssemblies_GenericTypeArityDoesNotCollapseDeclaringTypes()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*GenericTypeAritySample*" });

        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("GenericTypeAritySample`1.M()", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove
            && row.Text.Contains("1", StringComparison.Ordinal));
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("GenericTypeAritySample`1.M()", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Add
            && row.Text.Contains("2", StringComparison.Ordinal));
        Assert.DoesNotContain(diff.Rows, row => row.Member.Contains("GenericTypeAritySample`2.M()", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareAssemblies_NestedGenericDeclaringArityDoesNotCollapse()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*NestedGenericOuter*" });

        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("NestedGenericOuter`1.Inner`1.M()", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove
            && row.Text.Contains("1", StringComparison.Ordinal));
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("NestedGenericOuter`1.Inner`1.M()", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Add
            && row.Text.Contains("2", StringComparison.Ordinal));
        Assert.DoesNotContain(diff.Rows, row => row.Member.Contains("NestedGenericOuter`2.Inner`1.M()", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareAssemblies_ConstructorsUseMemberIndexCanonicalName()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ConstructorSample" });

        var row = Assert.Single(diff.Rows, row =>
            row.Member.Contains("ConstructorSample.#ctor()", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove);
        Assert.StartsWith(".ctor~", row.Anchor.StableSelector, StringComparison.Ordinal);
        Assert.Equal("M:DiffFixtureSample.ConstructorSample.#ctor()", row.Anchor.CanonicalSignature);
    }

    [Fact]
    public void CompareAssemblies_ConversionOperatorsIncludeReturnTypeInCanonicalSignature()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ConversionSample" });

        var row = Assert.Single(diff.Rows, row =>
            row.Anchor.CanonicalSignature.Contains("op_Implicit", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove);
        Assert.StartsWith("operator:op_Implicit~", row.Anchor.StableSelector, StringComparison.Ordinal);
        Assert.EndsWith("~System.Int32", row.Anchor.CanonicalSignature, StringComparison.Ordinal);
        Assert.DoesNotContain(diff.Rows, row => row.Anchor.CanonicalSignature.EndsWith("~System.String", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareAssemblies_OperatorsUseMemberIndexSelectorPrefix()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OperatorSample" });

        var row = Assert.Single(diff.Rows, row =>
            row.Anchor.CanonicalSignature.Contains("op_Addition", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove);
        Assert.StartsWith("operator:op_Addition~", row.Anchor.StableSelector, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_ExplicitImplementationsUseMemberIndexSelectorPrefix()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ExplicitSurface" });

        var row = Assert.Single(diff.Rows, row =>
            row.Anchor.CanonicalSignature.Contains("IExplicitSurface.Get", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove);
        Assert.StartsWith("explicit:DiffFixtureSample.IExplicitSurface.Get~", row.Anchor.StableSelector, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CheckedConversionOperatorsIncludeReturnTypeInCanonicalSignature()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CheckedConversionSample" });

        var row = Assert.Single(diff.Rows, row =>
            row.Anchor.CanonicalSignature.Contains("op_CheckedExplicit", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove);
        Assert.StartsWith("operator:op_CheckedExplicit~", row.Anchor.StableSelector, StringComparison.Ordinal);
        Assert.EndsWith("~System.Int32", row.Anchor.CanonicalSignature, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_TypeAndMethodGenericParametersStayDistinct()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*GenericParameterCollisionSample*" });

        var row = Assert.Single(diff.Rows, row =>
            row.Member.Contains("GenericParameterCollisionSample`1.M`1", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove);
        Assert.Equal("M:DiffFixtureSample.GenericParameterCollisionSample`1.M<!!0>(!!0)", row.Anchor.CanonicalSignature);
        Assert.DoesNotContain(diff.Rows, row => row.Anchor.CanonicalSignature.EndsWith("(!0)", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareAssemblies_MethodRemovalUsesMethodLevelMessage()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MethodRemovalSample" });

        var row = Assert.Single(diff.Rows, row => row.Member.EndsWith("Removed()", StringComparison.Ordinal));
        Assert.Equal(CSharpDiffKind.Remove, row.Kind);
        Assert.Equal("csharp.method.removed", row.ChangeId);
        Assert.Equal("Removed C# method.", row.Message);
        Assert.Equal("/* method removed */", row.Text);
        Assert.Null(row.SourceCoordinate);
    }

    [Fact]
    public void CompareAssemblies_NoBodyToBodyUsesSyntheticBodyAddedRow()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BodyStateSample" });

        var row = Assert.Single(diff.Rows);
        Assert.Equal(CSharpDiffKind.Add, row.Kind);
        Assert.Equal("csharp.method.body-added", row.ChangeId);
        Assert.Equal("Added C# method body.", row.Message);
        Assert.Equal("/* method body added */", row.Text);
        Assert.Null(row.SourceCoordinate);
    }

    [Fact]
    public void CompareAssemblies_BodyToNoBodyUsesSyntheticBodyRemovedRow()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v2, v1, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BodyStateSample" });

        var row = Assert.Single(diff.Rows);
        Assert.Equal(CSharpDiffKind.Remove, row.Kind);
        Assert.Equal("csharp.method.body-removed", row.ChangeId);
        Assert.Equal("Removed C# method body.", row.Message);
        Assert.Equal("/* method body removed */", row.Text);
        Assert.Null(row.SourceCoordinate);
    }

    [Fact]
    public void CompareAssemblies_ExplicitInterfaceImplementationsAreInDefaultSurface()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ExplicitSurface" });

        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("IExplicitSurface.Get", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove
            && row.Text.Contains("1", StringComparison.Ordinal));
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("IExplicitSurface.Get", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Add
            && row.Text.Contains("2", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareAssemblies_InternalExplicitInterfaceImplementationsStayOutOfDefaultSurface()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "InternalExplicitSurface" });

        Assert.Empty(diff.Rows);
    }

    [Fact]
    public void CompareAssemblies_DefaultSurfaceSkipsInternalTypesAndTheirNestedPublicTypes()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "InternalSurfaceSample" });

        Assert.Empty(diff.Rows);

        var nested = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*InternalSurfaceSample*" });

        Assert.Empty(nested.Rows);
    }

    [Fact]
    public void CompareAssemblies_GlobTypeFilterCanLimitRows()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*NoSuchType" });

        Assert.Empty(diff.Rows);
    }

    static string DiffFixturePath(string project)
    {
        var outputDirectory = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string path = Path.GetFullPath(Path.Combine(
            outputDirectory.FullName, "..", "..", project, outputDirectory.Name, "DiffFixtureSample.dll"));
        Assert.True(File.Exists(path), $"Expected diff fixture assembly at {path}");
        return path;
    }
}
