using ILInspector.DecompilerHarness;
using ILInspector.Metadata;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Corpus")]
public sealed class AuthoredSourceOracleManifestTests
{
    [Fact]
    public void PrinterComparison_IsExactBeforeNormalization()
    {
        Assert.Equal(
            PrinterExactOutcome.Exact,
            ReturnToSenderSourceProbe.ComparePrinterText(
                "if (value)\n{\n    return;\n}",
                "if (value)\n{\n    return;\n}\n"));
        Assert.Equal(
            PrinterExactOutcome.Different,
            ReturnToSenderSourceProbe.ComparePrinterText(
                "if (value)\n{\n    return;\n}",
                "if (value) { return; }"));
        Assert.Equal(
            PrinterExactOutcome.NotRecorded,
            ReturnToSenderSourceProbe.ComparePrinterText(
                expected: null,
                "if (value) { return; }"));
    }

    [Fact]
    public void SourceProbe_PurposeBuiltFixtureClearsAllThreeSourceLayers()
    {
        const string memberSource = """
            public static int PrinterExactFixture(int value)
            {
                return value + 1;
            }
            """;
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBodies(
            memberSource,
            nameof(PrinterExactFixture),
            expectedParameterCount: 1,
            out string expected,
            out string? printerBody));
        Assert.NotNull(printerBody);
        string assemblyPath = typeof(AuthoredSourceOracleManifestTests).Assembly.Location;
        using var pe = new PEReader(System.IO.File.OpenRead(assemblyPath));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle typeHandle = reader.TypeDefinitions.Single(handle =>
            reader.GetFullTypeName(reader.GetTypeDefinition(handle))
                == typeof(AuthoredSourceOracleManifestTests).FullName);
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        MethodDefinitionHandle methodHandle = type.GetMethods().Single(handle =>
            reader.GetString(reader.GetMethodDefinition(handle).Name)
                == nameof(PrinterExactFixture));
        int overload = ReturnToSenderSourceProbe.OverloadIndex(
            reader,
            type,
            methodHandle,
            nameof(PrinterExactFixture));
        string signature = ReturnToSenderSourceProbe.UniqueTargetSignature(
            reader,
            type,
            nameof(PrinterExactFixture),
            methodHandle)
            ?? throw new InvalidOperationException("Fixture signature was unavailable.");
        var member = new ReturnToSenderSourceMember(
            typeof(AuthoredSourceOracleManifestTests).FullName!,
            nameof(PrinterExactFixture),
            overload,
            signature,
            "PrinterExactFixture.cs",
            expected,
            MetadataTokens.GetToken(methodHandle),
            reader.GetGuid(reader.GetModuleDefinition().Mvid),
            PrinterBody: printerBody);
        var index = ReturnToSenderSourceIndex.FromCorrelatedMembers([member], reader);

        ReturnToSenderSourceProbeResult result = Assert.Single(
            ReturnToSenderSourceProbe.EvaluateWithIndex(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    member.Type,
                    member.Method,
                    member.Overload,
                    member.Signature)],
                index));

        Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome);
        Assert.Equal(PrinterExactOutcome.Exact, result.PrinterExact);
    }

    [Fact]
    public void Manifest_RequiresValidAndCorrectForEveryRegisteredFile()
    {
        var exact = Row("Exact.cs", 1, ReturnToSenderSourceOutcome.ValidMatch, PrinterExactOutcome.Exact);
        var notOptedIn = Row("Correct.cs", 2, ReturnToSenderSourceOutcome.ValidMatch, PrinterExactOutcome.Different);
        var manifest = Manifest(
            File(exact.Record, requirePrinterExact: true),
            File(notOptedIn.Record, requirePrinterExact: false));

        var report = AuthoredSourceOracleManifest.Evaluate(
            manifest,
            [exact, notOptedIn]);

        Assert.True(report.Passed);
        Assert.Equal(2, report.FilesValid);
        Assert.Equal(2, report.FilesCorrect);
        Assert.Equal(1, report.PrinterExactRequired);
        Assert.Equal(1, report.PrinterExactPassing);
    }

    [Fact]
    public void Manifest_MissingExpectedMemberFailsInsteadOfShorteningDenominator()
    {
        var first = Row("Oracle.cs", 1, ReturnToSenderSourceOutcome.ValidMatch, PrinterExactOutcome.Exact);
        var second = Row("Oracle.cs", 2, ReturnToSenderSourceOutcome.ValidMatch, PrinterExactOutcome.Exact);
        var file = File(first.Record, requirePrinterExact: true) with
        {
            Members = [Member(first.Record), Member(second.Record)],
        };

        var report = AuthoredSourceOracleManifest.Evaluate(
            Manifest(file),
            [first]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains("expected member", StringComparison.Ordinal)
            && failure.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_StaleExpectedSetFailsWhenCorpusAddsMember()
    {
        var first = Row("Oracle.cs", 1, ReturnToSenderSourceOutcome.ValidMatch, PrinterExactOutcome.Exact);
        var second = Row("Oracle.cs", 2, ReturnToSenderSourceOutcome.ValidMatch, PrinterExactOutcome.Exact);

        var report = AuthoredSourceOracleManifest.Evaluate(
            Manifest(File(first.Record, requirePrinterExact: true)),
            [first, second]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains("absent from the expected set", StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_PrinterExactCannotPassAFormattingOnlyDifference()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Different);

        var report = AuthoredSourceOracleManifest.Evaluate(
            Manifest(File(row.Record, requirePrinterExact: true)),
            [row]);

        Assert.False(report.Passed);
        Assert.Equal(1, report.FilesValid);
        Assert.Equal(1, report.FilesCorrect);
        Assert.Equal(0, report.PrinterExactPassing);
    }

    [Fact]
    public void Manifest_PrinterExactRequiresVersionedCapturedText()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact);
        row = row with
        {
            Record = row.Record with { PrinterBodyVersion = null },
        };

        var report = AuthoredSourceOracleManifest.Evaluate(
            Manifest(File(row.Record, requirePrinterExact: true)),
            [row]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains("lack Printer body version", StringComparison.Ordinal));
    }

    [Fact]
    public void SyntaxInventory_CollectsConcretePrinterSurfaces()
    {
        const string body = """
            int total = values.Count;
            if (total > 0)
            {
                return values[0] ?? new Item(total);
            }
            else
            {
                return default;
            }
            """;

        Assert.True(PrinterSyntaxInventory.TryCollect(
            body,
            out IReadOnlyList<string> features,
            out string? error),
            error);
        Assert.Equal(
            [
                "clause.else",
                "declaration.local.explicit-type",
                "expression.coalesce",
                "expression.default-literal",
                "expression.element-access",
                "expression.greater-than",
                "expression.numeric-literal",
                "expression.object-creation",
                "expression.simple-member-access",
                "statement.if",
                "statement.local-declaration",
                "statement.return",
            ],
            features);
    }

    [Fact]
    public void SyntaxInventory_TracksPatternSwitchLabelsAndWhenGuards()
    {
        const string body = """
            switch (value)
            {
                case string text when ready:
                    break;
            }
            return value switch
            {
                int number when ready => 1,
            };
            """;

        Assert.True(PrinterSyntaxInventory.TryCollect(
            body,
            out IReadOnlyList<string> features,
            out string? error),
            error);
        Assert.Equal(
            [
                "clause.switch-case-pattern",
                "clause.switch-expression-arm",
                "clause.when",
                "expression.numeric-literal",
                "expression.switch",
                "pattern.declaration",
                "statement.break",
                "statement.return",
                "statement.switch",
            ],
            features);
    }

    [Fact]
    public void SyntaxInventory_TracksScopedTypesAndRendersInterpolationFamily()
    {
        const string body = """
            scoped Span<int> values;
            return $"{values.Length,5:D3}";
            """;

        Assert.True(PrinterSyntaxInventory.TryCollect(
            body,
            out IReadOnlyList<string> features,
            out string? error),
            error);
        Assert.Equal(
            [
                "declaration.local.explicit-type",
                "expression.interpolated-string",
                "expression.numeric-literal",
                "expression.simple-member-access",
                "interpolation.alignment",
                "interpolation.format",
                "interpolation.hole",
                "statement.local-declaration",
                "statement.return",
                "syntax.generic-name",
                "type.scoped",
            ],
            features);

        Assert.Equal(
            $"      {"interpolation",-30}: alignment, format, hole",
            Assert.Single(
                AuthoredCorpusBenchmark.SyntaxInventoryGroupLines(features),
                line => line.Contains(
                    "interpolation",
                    StringComparison.Ordinal)));

        Assert.True(PrinterSyntaxInventory.TryCollect(
            "Span<int> values;",
            out IReadOnlyList<string> unscopedFeatures,
            out error),
            error);
        Assert.DoesNotContain("type.scoped", unscopedFeatures);
    }

    [Fact]
    public void SyntaxInventory_TracksAwaitOnDeconstructionForeach()
    {
        Assert.True(PrinterSyntaxInventory.TryCollect(
            "await foreach (var (key, value) in items) { }",
            out IReadOnlyList<string> asyncFeatures,
            out string? error),
            error);
        Assert.Equal(
            [
                "expression.declaration",
                "statement.await-foreach",
                "statement.for-each-variable",
            ],
            asyncFeatures);

        Assert.True(PrinterSyntaxInventory.TryCollect(
            "foreach (var (key, value) in items) { }",
            out IReadOnlyList<string> synchronousFeatures,
            out error),
            error);
        Assert.DoesNotContain("statement.await-foreach", synchronousFeatures);
    }

    [Fact]
    public void SyntaxInventory_TracksCollectionSpreads()
    {
        Assert.True(PrinterSyntaxInventory.TryCollect(
            "return [..values];",
            out IReadOnlyList<string> spreadFeatures,
            out string? error),
            error);
        Assert.Contains("expression.collection-spread", spreadFeatures);

        Assert.True(PrinterSyntaxInventory.TryCollect(
            "return [values];",
            out IReadOnlyList<string> elementFeatures,
            out error),
            error);
        Assert.DoesNotContain("expression.collection-spread", elementFeatures);
    }

    [Fact]
    public void SyntaxInventory_TracksStaticLocalFunctions()
    {
        Assert.True(PrinterSyntaxInventory.TryCollect(
            "static int Local(int value) { return value; }",
            out IReadOnlyList<string> staticFeatures,
            out string? error),
            error);
        Assert.Contains("statement.static-local-function", staticFeatures);

        Assert.True(PrinterSyntaxInventory.TryCollect(
            "int Local(int value) { return value; }",
            out IReadOnlyList<string> capturingFeatures,
            out error),
            error);
        Assert.DoesNotContain("statement.static-local-function", capturingFeatures);
    }

    [Fact]
    public void SyntaxInventory_DistinguishesRecursivePatternClauses()
    {
        Assert.True(PrinterSyntaxInventory.TryCollect(
            "return value is PatternPoint { X: > 0, Y: < 10 };",
            out IReadOnlyList<string> propertyFeatures,
            out string? error),
            error);
        Assert.Contains("clause.property-pattern", propertyFeatures);
        Assert.DoesNotContain("clause.positional-pattern", propertyFeatures);

        Assert.True(PrinterSyntaxInventory.TryCollect(
            "return value is (> 0, < 10);",
            out IReadOnlyList<string> positionalFeatures,
            out error),
            error);
        Assert.Contains("clause.positional-pattern", positionalFeatures);
        Assert.DoesNotContain("clause.property-pattern", positionalFeatures);
    }

    [Fact]
    public void SyntaxInventory_DistinguishesUsingResourceForms()
    {
        Assert.True(PrinterSyntaxInventory.TryCollect(
            "using (IDisposable resource = Open()) { }",
            out IReadOnlyList<string> declarationFeatures,
            out string? error),
            error);
        Assert.Contains("clause.using-variable-declaration", declarationFeatures);

        Assert.True(PrinterSyntaxInventory.TryCollect(
            "using (Open()) { }",
            out IReadOnlyList<string> expressionFeatures,
            out error),
            error);
        Assert.DoesNotContain("clause.using-variable-declaration", expressionFeatures);
    }

    [Fact]
    public void SyntaxInventory_DistinguishesCatchDeclarationForms()
    {
        Assert.True(PrinterSyntaxInventory.TryCollect(
            "try { } catch (Exception error) { }",
            out IReadOnlyList<string> variableFeatures,
            out string? error),
            error);
        Assert.Contains("clause.catch-declaration", variableFeatures);
        Assert.Contains("clause.catch-variable", variableFeatures);

        Assert.True(PrinterSyntaxInventory.TryCollect(
            "try { } catch (Exception) { }",
            out IReadOnlyList<string> declarationFeatures,
            out error),
            error);
        Assert.Contains("clause.catch-declaration", declarationFeatures);
        Assert.DoesNotContain("clause.catch-variable", declarationFeatures);

        Assert.True(PrinterSyntaxInventory.TryCollect(
            "try { } catch { }",
            out IReadOnlyList<string> bareFeatures,
            out error),
            error);
        Assert.DoesNotContain("clause.catch-declaration", bareFeatures);
        Assert.DoesNotContain("clause.catch-variable", bareFeatures);
    }

    [Fact]
    public void SyntaxInventory_DistinguishesAnonymousMemberNaming()
    {
        Assert.True(PrinterSyntaxInventory.TryCollect(
            "return new { Value = value };",
            out IReadOnlyList<string> explicitFeatures,
            out string? error),
            error);
        Assert.Contains("expression.anonymous-member-explicit-name", explicitFeatures);

        Assert.True(PrinterSyntaxInventory.TryCollect(
            "return new { value };",
            out IReadOnlyList<string> shorthandFeatures,
            out error),
            error);
        Assert.DoesNotContain(
            "expression.anonymous-member-explicit-name",
            shorthandFeatures);
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("out")]
    [InlineData("in")]
    public void SyntaxInventory_TracksParameterRefKinds(string refKind)
    {
        Assert.True(PrinterSyntaxInventory.TryCollect(
            $"return ({refKind} int value, int sibling) => value + sibling;",
            out IReadOnlyList<string> byRefFeatures,
            out string? error),
            error);
        Assert.Contains($"parameter.{refKind}", byRefFeatures);

        Assert.True(PrinterSyntaxInventory.TryCollect(
            "return (value, sibling) => value + sibling;",
            out IReadOnlyList<string> ordinaryFeatures,
            out error),
            error);
        Assert.DoesNotContain($"parameter.{refKind}", ordinaryFeatures);

        Assert.True(PrinterSyntaxInventory.TryCollect(
            $"delegate*<{refKind} int, void> callback = null;",
            out IReadOnlyList<string> functionPointerFeatures,
            out error),
            error);
        Assert.Contains($"parameter.{refKind}", functionPointerFeatures);
    }

    [Fact]
    public void SyntaxInventory_DistinguishesFunctionPointerConventions()
    {
        Assert.True(PrinterSyntaxInventory.TryCollect(
            "delegate*<int, void> callback = null;",
            out IReadOnlyList<string> managedFeatures,
            out string? error),
            error);
        Assert.DoesNotContain("type.function-pointer-unmanaged", managedFeatures);
        Assert.DoesNotContain(
            "type.function-pointer-named-calling-convention",
            managedFeatures);

        Assert.True(PrinterSyntaxInventory.TryCollect(
            "delegate* unmanaged<int, void> callback = null;",
            out IReadOnlyList<string> unmanagedFeatures,
            out error),
            error);
        Assert.Contains("type.function-pointer-unmanaged", unmanagedFeatures);
        Assert.DoesNotContain(
            "type.function-pointer-named-calling-convention",
            unmanagedFeatures);

        Assert.True(PrinterSyntaxInventory.TryCollect(
            "delegate* unmanaged[Cdecl]<int, void> callback = null;",
            out IReadOnlyList<string> namedConventionFeatures,
            out error),
            error);
        Assert.Contains("type.function-pointer-unmanaged", namedConventionFeatures);
        Assert.Contains(
            "type.function-pointer-named-calling-convention",
            namedConventionFeatures);
    }

    [Fact]
    public void Manifest_SyntaxInventoryRequiresExactObservedFeatureSet()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact,
            printerBody: "return value + 1;");
        var file = File(row.Record, requirePrinterExact: true) with
        {
            ExpectedFeatures =
            [
                "expression.add",
                "expression.numeric-literal",
                "statement.return",
            ],
        };

        var report = AuthoredSourceOracleManifest.Evaluate(
            ManifestWithInventory(file),
            [row]);

        Assert.True(report.Passed);
        Assert.Equal(true, report.SyntaxInventoryEvaluated);
        Assert.Equal(1, report.FilesInventoryTracked);
        Assert.Equal(file.ExpectedFeatures, report.ObservedFeatures);
        Assert.Equal(file.ExpectedFeatures, Assert.Single(report.FileInventory!).Features);
    }

    [Fact]
    public void Manifest_SyntaxInventoryRejectsMissingAndInventedFeatures()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact,
            printerBody: "return value + 1;");
        var file = File(row.Record, requirePrinterExact: true) with
        {
            ExpectedFeatures =
            [
                "expression.multiply",
                "statement.return",
            ],
        };

        var report = AuthoredSourceOracleManifest.Evaluate(
            ManifestWithInventory(file),
            [row]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains(
                "expected syntax feature 'expression.multiply' was not observed",
                StringComparison.Ordinal));
        Assert.Contains(report.Failures, failure =>
            failure.Contains(
                "observed syntax feature 'expression.add' is absent",
                StringComparison.Ordinal));
        Assert.Empty(report.ObservedFeatures!);
        Assert.Equal(
            [
                "expression.add",
                "expression.numeric-literal",
                "statement.return",
            ],
            Assert.Single(report.FileInventory!).Features);
    }

    [Fact]
    public void Manifest_SyntaxInventoryRequiresVersion()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact);
        var file = File(row.Record, requirePrinterExact: true) with
        {
            ExpectedFeatures = ["statement.return"],
        };

        var report = AuthoredSourceOracleManifest.Evaluate(
            Manifest(file),
            [row]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains(
                "expectedFeatures requires a syntaxInventoryVersion",
                StringComparison.Ordinal));
        Assert.Empty(report.ObservedFeatures!);
        Assert.Empty(report.FileInventory!);
    }

    [Fact]
    public void Manifest_SyntaxInventoryRejectsUnsupportedVersion()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact,
            printerBody: "return (");
        var manifest = ManifestWithInventory(
            File(row.Record, requirePrinterExact: true) with
            {
                ExpectedFeatures = ["statement.return"],
            }) with
        {
            SyntaxInventoryVersion = PrinterSyntaxInventory.Version + 1,
        };

        var report = AuthoredSourceOracleManifest.Evaluate(manifest, [row]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains("unsupported", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Failures, failure =>
            failure.Contains("could not parse", StringComparison.Ordinal));
        Assert.Equal(false, report.SyntaxInventoryEvaluated);
        Assert.Null(report.ObservedFeatures);
        Assert.Null(report.FileInventory);
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("printer-comparison")]
    public void Manifest_SyntaxInventoryRejectsUnsupportedGoverningVersion(
        string governingVersion)
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact,
            printerBody: "return value + 1;");
        var file = File(row.Record, requirePrinterExact: true) with
        {
            ExpectedFeatures =
            [
                "expression.add",
                "expression.numeric-literal",
                "statement.return",
            ],
        };
        var manifest = governingVersion switch
        {
            "manifest" => ManifestWithInventory(file) with
            {
                Version = AuthoredSourceOracleManifest.Version + 1,
            },
            "printer-comparison" => ManifestWithInventory(file) with
            {
                PrinterComparisonVersion =
                    AuthoredSourceOracleManifest.PrinterComparisonVersion + 1,
            },
            _ => throw new InvalidOperationException(),
        };

        var report = AuthoredSourceOracleManifest.Evaluate(manifest, [row]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains("unsupported", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Failures, failure =>
            failure.Contains(
                $"syntax inventory version {PrinterSyntaxInventory.Version} is unsupported",
                StringComparison.Ordinal));
        Assert.Equal(false, report.SyntaxInventoryEvaluated);
        Assert.Equal(0, report.FilesInventoryTracked);
        Assert.Null(report.ObservedFeatures);
        Assert.Null(report.FileInventory);
    }

    [Fact]
    public void Manifest_SyntaxInventoryRequiresPrinterExact()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.NotRecorded);
        var file = File(row.Record, requirePrinterExact: false) with
        {
            ExpectedFeatures = ["statement.return"],
        };

        var report = AuthoredSourceOracleManifest.Evaluate(
            ManifestWithInventory(file),
            [row]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains(
                "syntax inventory requires Printer exact",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, "expected syntax feature set is empty")]
    [InlineData("empty", "expected syntax feature is empty")]
    [InlineData("duplicate", "expected syntax feature is duplicated")]
    [InlineData("unsorted", "expected syntax features are not ordinal-sorted")]
    public void Manifest_SyntaxInventoryRejectsNonCanonicalFeatureSets(
        string? shape,
        string expectedFailure)
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact);
        IReadOnlyList<string>? features = shape switch
        {
            null => null,
            "empty" => [""],
            "duplicate" => ["statement.return", "statement.return"],
            "unsorted" => ["statement.return", "expression.identifier-name"],
            _ => throw new InvalidOperationException(),
        };
        var file = File(row.Record, requirePrinterExact: true) with
        {
            ExpectedFeatures = features,
        };

        var report = AuthoredSourceOracleManifest.Evaluate(
            ManifestWithInventory(file),
            [row]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains(expectedFailure, StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_SyntaxInventoryRequiresEveryCapturedPrinterBody()
    {
        var original = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact);
        var row = original with
        {
            Record = original.Record with
            {
                PrinterBody = null,
            },
        };
        var file = File(row.Record, requirePrinterExact: true) with
        {
            ExpectedFeatures = ["statement.return"],
        };

        var report = AuthoredSourceOracleManifest.Evaluate(
            ManifestWithInventory(file),
            [row]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains(
                "has no captured Printer body for syntax inventory",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_SyntaxInventoryRejectsUnparseablePrinterBody()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact,
            printerBody: "return (");
        var file = File(row.Record, requirePrinterExact: true) with
        {
            ExpectedFeatures = ["statement.return"],
        };

        var report = AuthoredSourceOracleManifest.Evaluate(
            ManifestWithInventory(file),
            [row]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains(
                "syntax inventory could not parse",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_UnparseableMemberDoesNotEnterAggregateInventory()
    {
        var valid = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact);
        var malformed = Row(
            "Oracle.cs",
            2,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact,
            printerBody: "return (");
        var file = File(valid.Record, requirePrinterExact: true) with
        {
            Members = [Member(valid.Record), Member(malformed.Record)],
            ExpectedFeatures = ["statement.return"],
        };

        var report = AuthoredSourceOracleManifest.Evaluate(
            ManifestWithInventory(file),
            [valid, malformed]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains(
                "syntax inventory could not parse",
                StringComparison.Ordinal));
        Assert.Empty(report.ObservedFeatures!);
        Assert.Equal(
            ["statement.return"],
            Assert.Single(report.FileInventory!).Features);
    }

    [Fact]
    public void Manifest_FailedPrinterExactFileDoesNotEnterAggregateInventory()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Different);
        var file = File(row.Record, requirePrinterExact: true) with
        {
            ExpectedFeatures = ["statement.return"],
        };

        var report = AuthoredSourceOracleManifest.Evaluate(
            ManifestWithInventory(file),
            [row]);

        Assert.False(report.Passed);
        Assert.Empty(report.ObservedFeatures!);
        Assert.Equal(
            ["statement.return"],
            Assert.Single(report.FileInventory!).Features);
    }

    [Fact]
    public void Manifest_LegacyShapeRemainsReadableButDoesNotClaimInventory()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact);

        var report = AuthoredSourceOracleManifest.Evaluate(
            Manifest(File(row.Record, requirePrinterExact: true)),
            [row]);

        Assert.True(report.Passed);
        Assert.Null(report.SyntaxInventoryVersion);
        Assert.Null(report.SyntaxInventoryEvaluated);
        Assert.Equal(0, report.FilesInventoryTracked);
        Assert.Empty(report.ObservedFeatures!);
        Assert.Empty(report.FileInventory!);
    }

    [Fact]
    public void Manifest_LegacyShapeDoesNotParsePrinterBodies()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact,
            printerBody: "return (");

        var report = AuthoredSourceOracleManifest.Evaluate(
            Manifest(File(row.Record, requirePrinterExact: true)),
            [row]);

        Assert.True(report.Passed);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void Report_LegacyJsonDefaultsInventoryFields()
    {
        const string json = """
            {
              "filesRegistered": 1,
              "filesValid": 1,
              "filesCorrect": 1,
              "printerExactRequired": 1,
              "printerExactPassing": 1,
              "passed": true,
              "failures": []
            }
            """;

        var report = JsonSerializer.Deserialize<AuthoredSourceOracleManifest.Report>(
            json,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling =
                    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
            });

        Assert.NotNull(report);
        Assert.Null(report.SyntaxInventoryVersion);
        Assert.Null(report.SyntaxInventoryEvaluated);
        Assert.Equal(0, report.FilesInventoryTracked);
        Assert.Null(report.ObservedFeatures);
        Assert.Null(report.FileInventory);
    }

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, 1, true)]
    [InlineData(true, null, true)]
    [InlineData(true, 2, true)]
    [InlineData(true, 1, false)]
    public void CorpusReader_ValidatesPrinterBodyAndVersionAsOneShape(
        bool includeBody,
        int? version,
        bool malformed)
    {
        var record = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact).Record with
        {
            PrinterBody = includeBody ? "return;" : null,
            PrinterBodyVersion = version,
        };
        string path = Path.Combine(
            Path.GetTempPath(),
            $"printer-body-schema-{Guid.NewGuid():N}.jsonl");
        try
        {
            System.IO.File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    record,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    })
                    + "\n");

            var records = AuthoredCorpusBenchmark.ReadCorpus(
                path,
                out int malformedRows);

            Assert.Equal(malformed ? 1 : 0, malformedRows);
            Assert.Equal(malformed ? 0 : 1, records.Count);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void Manifest_CorrectGateRejectsValidDifferentBeforePrinterJudgment()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidDifferent,
            PrinterExactOutcome.Different);

        var report = AuthoredSourceOracleManifest.Evaluate(
            Manifest(File(row.Record, requirePrinterExact: false)),
            [row]);

        Assert.False(report.Passed);
        Assert.Equal(1, report.FilesValid);
        Assert.Equal(0, report.FilesCorrect);
    }

    [Fact]
    public void Benchmark_TextAndJsonAgreeOnSourceOracleInventoryFailure()
    {
        string assembly = typeof(ILInspector.CSharp.CSharpFormatter).Assembly.Location;
        string corpus = AuthoredCorpusTestData.WriteCorrelatedCorpus(assembly);
        string manifestPath = Path.Combine(
            Path.GetTempPath(),
            $"source-oracle-{Guid.NewGuid():N}.json");

        try
        {
            var records = AuthoredCorpusBenchmark.ReadCorpus(corpus, out int malformed);
            Assert.Equal(0, malformed);
            var manifest = ManifestWithInventory(
                File(Assert.Single(records), requirePrinterExact: true) with
                {
                    ExpectedFeatures = ["statement.return"],
                });
            System.IO.File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    }));

            using var text = new StringWriter();
            int textExit = AuthoredCorpusBenchmark.Run(
                [assembly],
                corpus,
                json: false,
                sourceOracleManifestPath: manifestPath,
                output: text);

            using var json = new StringWriter();
            int jsonExit = AuthoredCorpusBenchmark.Run(
                [assembly],
                corpus,
                json: true,
                sourceOracleManifestPath: manifestPath,
                output: json);
            AuthoredCorpusBenchmark.Report payload =
                AuthoredCorpusHistoryStore.ParseBenchmarkReport(json.ToString());

            Assert.Equal(1, textExit);
            Assert.Equal(textExit, jsonExit);
            Assert.Contains("Source-oracle files:", text.ToString());
            Assert.Contains("Syntax inventory v1", text.ToString());
            Assert.Contains("0 feature(s) across 1 file(s)", text.ToString());
            Assert.Contains("BLOCKER", text.ToString());
            Assert.NotNull(payload.SourceOracleManifest);
            Assert.Equal(1, payload.SourceOracleManifest.FilesRegistered);
            Assert.False(payload.SourceOracleManifest.Passed);
            Assert.Equal(1, payload.SourceOracleManifest.SyntaxInventoryVersion);
            Assert.Equal(true, payload.SourceOracleManifest.SyntaxInventoryEvaluated);
            Assert.Equal(1, payload.SourceOracleManifest.FilesInventoryTracked);
            Assert.Empty(payload.SourceOracleManifest.ObservedFeatures!);
            Assert.Empty(Assert.Single(
                payload.SourceOracleManifest.FileInventory!).Features);
            Assert.Contains(
                $": {payload.SourceOracleManifest.FilesValid} / "
                    + $"{payload.SourceOracleManifest.FilesRegistered}",
                text.ToString());
            Assert.Contains(
                $": {payload.SourceOracleManifest.FilesCorrect} / "
                    + $"{payload.SourceOracleManifest.FilesRegistered}",
                text.ToString());
        }

        finally
        {
            System.IO.File.Delete(corpus);
            System.IO.File.Delete(manifestPath);
        }
    }

    [Fact]
    public void Benchmark_ReportsRejectedInventoryAsNotEvaluated()
    {
        string assembly = typeof(ILInspector.CSharp.CSharpFormatter).Assembly.Location;
        string corpus = AuthoredCorpusTestData.WriteCorrelatedCorpus(assembly);
        string manifestPath = Path.Combine(
            Path.GetTempPath(),
            $"source-oracle-{Guid.NewGuid():N}.json");

        try
        {
            var records = AuthoredCorpusBenchmark.ReadCorpus(corpus, out int malformed);
            Assert.Equal(0, malformed);
            var manifest = ManifestWithInventory(
                File(Assert.Single(records), requirePrinterExact: true) with
                {
                    ExpectedFeatures = ["statement.return"],
                }) with
            {
                Version = AuthoredSourceOracleManifest.Version + 1,
            };
            System.IO.File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    }));

            using var text = new StringWriter();
            int textExit = AuthoredCorpusBenchmark.Run(
                [assembly],
                corpus,
                json: false,
                sourceOracleManifestPath: manifestPath,
                output: text);

            using var json = new StringWriter();
            int jsonExit = AuthoredCorpusBenchmark.Run(
                [assembly],
                corpus,
                json: true,
                sourceOracleManifestPath: manifestPath,
                output: json);
            AuthoredCorpusBenchmark.Report payload =
                AuthoredCorpusHistoryStore.ParseBenchmarkReport(json.ToString());

            Assert.Equal(1, textExit);
            Assert.Equal(textExit, jsonExit);
            Assert.Contains(
                "Syntax inventory v1              : NOT EVALUATED",
                text.ToString());
            Assert.DoesNotContain(
                "Syntax inventory v1              : 0 feature(s)",
                text.ToString());
            Assert.NotNull(payload.SourceOracleManifest);
            Assert.Equal(false, payload.SourceOracleManifest.SyntaxInventoryEvaluated);
            Assert.Equal(0, payload.SourceOracleManifest.FilesInventoryTracked);
            Assert.Null(payload.SourceOracleManifest.ObservedFeatures);
            Assert.Null(payload.SourceOracleManifest.FileInventory);
        }
        finally
        {
            System.IO.File.Delete(corpus);
            System.IO.File.Delete(manifestPath);
        }
    }

    [Theory]
    [InlineData("""{"version":1,"version":1,"printerComparisonVersion":1,"files":[]}""")]
    [InlineData("""{"version":1,"printerComparisonVersion":1,"unknown":true,"files":[]}""")]
    public void ManifestReader_RejectsUnknownAndDuplicateProperties(string json)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"source-oracle-invalid-{Guid.NewGuid():N}.json");
        try
        {
            System.IO.File.WriteAllText(path, json);

            Assert.False(AuthoredSourceOracleManifest.TryRead(
                path,
                out _,
                out string? error));
            Assert.Contains("not valid JSON", error, StringComparison.Ordinal);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    static AuthoredSourceOracleManifest.Document Manifest(
        params AuthoredSourceOracleManifest.FileEntry[] files)
        => new(
            AuthoredSourceOracleManifest.Version,
            AuthoredSourceOracleManifest.PrinterComparisonVersion,
            files);

    static AuthoredSourceOracleManifest.Document ManifestWithInventory(
        params AuthoredSourceOracleManifest.FileEntry[] files)
        => new(
            AuthoredSourceOracleManifest.Version,
            AuthoredSourceOracleManifest.PrinterComparisonVersion,
            files,
            PrinterSyntaxInventory.Version);

    static AuthoredSourceOracleManifest.FileEntry File(
        AuthoredSourceHarvest.CorpusRecord record,
        bool requirePrinterExact)
        => new(
            record.SourceUrl!,
            record.ChecksumAlgorithm!,
            record.Checksum!,
            AuthoredSourceOracleManifest.DefaultPrinterProfile,
            requirePrinterExact,
            [Member(record)]);

    static AuthoredSourceOracleManifest.MemberEntry Member(
        AuthoredSourceHarvest.CorpusRecord record)
        => new(
            record.Assembly,
            record.AssemblyVersion,
            record.ModuleVersionId!.Value,
            record.MetadataToken,
            record.Type,
            record.Method,
            record.Overload);

    static AuthoredSourceOracleManifest.EvaluatedRow Row(
        string path,
        int token,
        ReturnToSenderSourceOutcome outcome,
        PrinterExactOutcome printerExact,
        string printerBody = "return;")
    {
        var record = new AuthoredSourceHarvest.CorpusRecord(
            Assembly: "Fixture",
            AssemblyVersion: "1.0.0.0",
            Tfm: "net10.0",
            Type: "Fixture.Type",
            Method: $"Method{token}",
            Overload: 0,
            Signature: null,
            MetadataToken: token,
            ParameterCount: 0,
            IlSize: 1,
            SourceUrl: $"https://raw.githubusercontent.com/example/repo/0123456789abcdef/{path}",
            ChecksumAlgorithm: "SHA256",
            Checksum: new string('A', 64),
            AuthoredBody: printerBody,
            ModuleVersionId: new Guid("11111111-2222-3333-4444-555555555555"),
            PrinterBody: printerBody,
            PrinterBodyVersion: AuthoredSourceOracleManifest.PrinterComparisonVersion);
        var result = new ReturnToSenderSourceProbeResult(
            new ReturnToSender.RequestedTarget(record.Type, record.Method, record.Overload),
            outcome,
            FidelityCheck.CompileBackStatus.Exact,
            "fixture",
            Detail: null,
            SourcePath: record.SourceUrl,
            ExpectedBody: record.AuthoredBody,
            ActualBody: record.PrinterBody,
            PrinterExact: printerExact);
        return new(record, result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int PrinterExactFixture(int value)
    {
        return value + 1;
    }
}
