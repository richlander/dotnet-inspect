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
            && row.ChangeId == "csharp.return-expression.changed"
            && row.OldValue == "1"
            && row.NewValue == "2");
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("ConstantValue", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Add
            && row.Text.Contains("2", StringComparison.Ordinal));
        Assert.All(diff.Rows.Where(row => row.Member.Contains("ConstantValue", StringComparison.Ordinal) && row.ChangeId.StartsWith("csharp.line.", StringComparison.Ordinal)), row =>
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
        Assert.Equal("M:DiffFixtureSample.DiffSample.GenericParamBody<T>(T)", row.Anchor.CanonicalSignature);
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
    public void CompareAssemblies_SemanticSwitchCaseRowsPreserveLineFallback()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

        var removed = Assert.Single(diff.Rows, row =>
            row.Member.Contains("SemanticSwitchCase", StringComparison.Ordinal)
            && row.ChangeId == "csharp.switch.case.removed");
        Assert.Equal(CSharpDiffKind.Remove, removed.Kind);
        Assert.Equal("7", removed.OldValue);
        Assert.Null(removed.NewValue);
        Assert.Equal(CSharpDiffOperationKind.SwitchCase, removed.OldOperation?.Kind);
        Assert.Equal("7", removed.OldOperation?.Value);
        Assert.Null(removed.NewOperation);
        Assert.Equal("Removed switch case '7'", removed.Message);

        var added = Assert.Single(diff.Rows, row =>
            row.Member.Contains("SemanticSwitchCase", StringComparison.Ordinal)
            && row.ChangeId == "csharp.switch.case.added");
        Assert.Equal(CSharpDiffKind.Add, added.Kind);
        Assert.Null(added.OldValue);
        Assert.Equal("7", added.NewValue);
        Assert.Null(added.OldOperation);
        Assert.Equal(CSharpDiffOperationKind.SwitchCase, added.NewOperation?.Kind);
        Assert.Equal("7", added.NewOperation?.Value);
        Assert.Equal("Added switch case '7'", added.Message);

        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("SemanticSwitchCase", StringComparison.Ordinal)
            && row.ChangeId == "csharp.line.removed");
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("SemanticSwitchCase", StringComparison.Ordinal)
            && row.ChangeId == "csharp.line.added");
    }

    [Fact]
    public void CompareAssemblies_SemanticReturnExpressionChangedRowPreservesLineFallback()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

        var row = Assert.Single(diff.Rows, row =>
            row.Member.Contains("SemanticReturnExpression", StringComparison.Ordinal)
            && row.ChangeId == "csharp.return-expression.changed");
        Assert.Equal(CSharpDiffKind.Changed, row.Kind);
        Assert.Equal("value + 1", row.OldValue);
        Assert.Equal("value + 2", row.NewValue);
        Assert.Equal(CSharpDiffOperationKind.ReturnExpression, row.OldOperation?.Kind);
        Assert.Equal("value + 1", row.OldOperation?.Value);
        Assert.Equal(CSharpDiffOperationKind.ReturnExpression, row.NewOperation?.Kind);
        Assert.Equal("value + 2", row.NewOperation?.Value);
        Assert.Equal("Changed return expression from 'value + 1' to 'value + 2'", row.Message);

        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("SemanticReturnExpression", StringComparison.Ordinal)
            && row.ChangeId == "csharp.line.removed");
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("SemanticReturnExpression", StringComparison.Ordinal)
            && row.ChangeId == "csharp.line.added");
    }

    [Fact]
    public void CompareAssemblies_SemanticCallChangedRowPreservesLineFallback()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

        var row = Assert.Single(diff.Rows, row =>
            row.Member.Contains("SemanticCallChange", StringComparison.Ordinal)
            && row.ChangeId == "csharp.call.changed");
        Assert.Equal(CSharpDiffKind.Changed, row.Kind);
        Assert.Contains("Sink(value)", row.OldValue);
        Assert.Contains("MaybeThrow(value)", row.NewValue);
        Assert.Equal(CSharpDiffOperationKind.Invocation, row.OldOperation?.Kind);
        Assert.Contains("Sink(value)", row.OldOperation?.Value);
        Assert.Equal(CSharpDiffOperationKind.Invocation, row.NewOperation?.Kind);
        Assert.Contains("MaybeThrow(value)", row.NewOperation?.Value);
        Assert.Contains("Changed call", row.Message);

        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("SemanticCallChange", StringComparison.Ordinal)
            && row.ChangeId == "csharp.line.removed");
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("SemanticCallChange", StringComparison.Ordinal)
            && row.ChangeId == "csharp.line.added");
    }

    [Fact]
    public void CompareAssemblies_SemanticMultiCallHunkEmitsAllChangedRows()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

        var rows = diff.Rows
            .Where(row => row.Member.Contains("SemanticMultiCallChange", StringComparison.Ordinal)
                && row.ChangeId == "csharp.call.changed")
            .OrderBy(row => row.Line)
            .ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal("DiffSample.Sink(value)", rows[0].OldValue);
        Assert.Equal("DiffSample.MaybeThrow(value)", rows[0].NewValue);
        Assert.Equal("DiffSample.Sink(value + 1)", rows[1].OldValue);
        Assert.Equal("DiffSample.MaybeThrow(value + 1)", rows[1].NewValue);
    }

    [Fact]
    public void CompareAssemblies_SemanticCallIgnoresStringLiteralParentheses()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

        Assert.DoesNotContain(diff.Rows, row =>
            row.Member.Contains("SemanticCallStringLiteralNearMiss", StringComparison.Ordinal)
            && row.ChangeId == "csharp.call.changed");
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("SemanticCallStringLiteralNearMiss", StringComparison.Ordinal)
            && row.ChangeId == "csharp.line.removed");
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("SemanticCallStringLiteralNearMiss", StringComparison.Ordinal)
            && row.ChangeId == "csharp.line.added");
    }

    [Fact]
    public void CompareAssemblies_SemanticCallIgnoresArithmeticGroupingParentheses()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

        Assert.DoesNotContain(diff.Rows, row =>
            row.Member.Contains("SemanticCallArithmeticGroupingNearMiss", StringComparison.Ordinal)
            && row.ChangeId == "csharp.call.changed");
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("SemanticCallArithmeticGroupingNearMiss", StringComparison.Ordinal)
            && row.ChangeId == "csharp.line.removed");
        Assert.Contains(diff.Rows, row =>
            row.Member.Contains("SemanticCallArithmeticGroupingNearMiss", StringComparison.Ordinal)
            && row.ChangeId == "csharp.line.added");
    }

    [Fact]
    public void CompareAssemblies_SemanticCallExtractsTrailingExpressionInvocation()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });

        var row = Assert.Single(diff.Rows, row =>
            row.Member.Contains("SemanticCallTrailingExpression", StringComparison.Ordinal)
            && row.ChangeId == "csharp.call.changed");
        Assert.Equal(CSharpDiffKind.Changed, row.Kind);
        Assert.Contains("Abs(value)", row.OldValue);
        Assert.Contains("Sign(value)", row.NewValue);
    }

    [Fact]
    public void Printer_SemanticRows_RenderFromStructuredOperations()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });
        var row = Assert.Single(diff.Rows, row =>
            row.Member.Contains("SemanticReturnExpression", StringComparison.Ordinal)
            && row.ChangeId == "csharp.return-expression.changed");

        var display = CSharpDiffPrinter.ToDisplayRow(row);

        Assert.Equal("~", display.Marker);
        Assert.Equal(CSharpDiffOperationKind.ReturnExpression, display.OperationKind);
        Assert.Equal("value + 1", display.OldValue);
        Assert.Equal("value + 2", display.NewValue);
        Assert.Equal("return value + 1 => return value + 2", display.Operation);
        Assert.Contains("return value + 1 => return value + 2", display.UnifiedLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Printer_LineRows_RenderFromStructuredOperations()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" });
        var source = diff.Rows.Single(row =>
            row.Member.Contains("ConstantValue", StringComparison.Ordinal)
            && row.ChangeId == "csharp.line.removed");

        var display = CSharpDiffPrinter.ToDisplayRow(source);

        Assert.Equal("-", display.Marker);
        Assert.Equal(CSharpDiffOperationKind.Line, display.OperationKind);
        Assert.Equal(source.Text, display.OldValue);
        Assert.Null(display.NewValue);
        Assert.Equal(source.Text, display.Operation);
        Assert.Contains(source.Text, display.UnifiedLine, StringComparison.Ordinal);
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
        Assert.Equal("M:DiffFixtureSample.ConversionSample.op_Implicit(DiffFixtureSample.ConversionSample)", row.Anchor.CanonicalSignature);
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
    public void CompareAssemblies_ExtensionMethodsUseMetadataMemberAnchor()
    {
        var v1 = FixtureCatalog.DiffPair.OldAssemblyPath();
        var v2 = FixtureCatalog.DiffPair.NewAssemblyPath();

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ExtensionSample" });

        var row = Assert.Single(diff.Rows, row =>
            row.Anchor.CanonicalSignature.Contains("Twice", StringComparison.Ordinal)
            && row.Kind == CSharpDiffKind.Remove);
        Assert.StartsWith("Twice~", row.Anchor.StableSelector, StringComparison.Ordinal);
        Assert.Equal("M:DiffFixtureSample.ExtensionSample.Twice(int)", row.Anchor.CanonicalSignature);
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
        Assert.Equal("M:DiffFixtureSample.CheckedConversionSample.op_CheckedExplicit(DiffFixtureSample.CheckedConversionSample)", row.Anchor.CanonicalSignature);
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
        Assert.Equal("M:DiffFixtureSample.GenericParameterCollisionSample<T>.M<U>(U)", row.Anchor.CanonicalSignature);
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
        Assert.Equal(CSharpDiffOperationKind.Method, row.OldOperation?.Kind);
        Assert.Equal("/* method removed */", row.OldOperation?.Value);
        Assert.Null(row.NewOperation);
        Assert.Null(row.SourceCoordinate);
    }

    [Fact]
    public void CompareAssemblies_NoBodyToBodyUsesSyntheticBodyAddedRow()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BodyStateSample" });

        var row = Assert.Single(diff.Rows);
        var failure = Assert.Single(diff.FailureRows);
        Assert.Equal(CSharpDiffFailureKind.OldBodyMissing, failure.Kind);
        Assert.Equal("old", failure.Side);
        Assert.Equal("Old method has no C# body.", failure.Message);
        Assert.Equal(row.HunkId, failure.HunkId);
        Assert.Contains("BodyStateSample", failure.Member, StringComparison.Ordinal);
        Assert.Equal(CSharpDiffKind.Add, row.Kind);
        Assert.Equal("csharp.method.body-added", row.ChangeId);
        Assert.Equal("Added C# method body.", row.Message);
        Assert.Equal("/* method body added */", row.Text);
        Assert.Null(row.OldOperation);
        Assert.Equal(CSharpDiffOperationKind.MethodBody, row.NewOperation?.Kind);
        Assert.Equal("/* method body added */", row.NewOperation?.Value);
        Assert.Null(row.SourceCoordinate);
    }

    [Fact]
    public void CompareAssemblies_BodyToNoBodyUsesSyntheticBodyRemovedRow()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v2, v1, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BodyStateSample" });

        var row = Assert.Single(diff.Rows);
        var failure = Assert.Single(diff.FailureRows);
        Assert.Equal(CSharpDiffFailureKind.NewBodyMissing, failure.Kind);
        Assert.Equal("new", failure.Side);
        Assert.Equal("New method has no C# body.", failure.Message);
        Assert.Equal(row.HunkId, failure.HunkId);
        Assert.Contains("BodyStateSample", failure.Member, StringComparison.Ordinal);
        Assert.Equal(CSharpDiffKind.Remove, row.Kind);
        Assert.Equal("csharp.method.body-removed", row.ChangeId);
        Assert.Equal("Removed C# method body.", row.Message);
        Assert.Equal("/* method body removed */", row.Text);
        Assert.Equal(CSharpDiffOperationKind.MethodBody, row.OldOperation?.Kind);
        Assert.Equal("/* method body removed */", row.OldOperation?.Value);
        Assert.Null(row.NewOperation);
        Assert.Null(row.SourceCoordinate);
    }

    [Fact]
    public void Printer_FailureRows_RenderFromStructuredFailureRows()
    {
        var v1 = DiffFixturePath("DiffFixtures.V1");
        var v2 = DiffFixturePath("DiffFixtures.V2");

        var diff = CSharpBodyDiff.CompareAssemblies(v1, v2, typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BodyStateSample" });

        var failure = Assert.Single(diff.FailureRows);
        var display = CSharpDiffPrinter.ToDisplayFailureRow(failure);

        Assert.Equal(CSharpDiffFailureKind.OldBodyMissing, display.Kind);
        Assert.Equal("old", display.Side);
        Assert.Equal("Old method has no C# body.", display.Message);
        Assert.Equal(failure.Detail, display.Detail);
        Assert.Equal("C# diff failed: Old method has no C# body.", display.UnifiedLine);
        Assert.Contains(display.UnifiedLine, CSharpDiffPrinter.RenderUnified(diff), StringComparison.Ordinal);
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
