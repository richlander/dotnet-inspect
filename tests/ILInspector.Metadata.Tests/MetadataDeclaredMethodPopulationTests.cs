using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataDeclaredMethodPopulationTests
{
    [Fact]
    public void PreparedSourceReusesAuthenticationAndReceipts()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(System.Text.Json.JsonSerializer).Assembly.Location);
        MetadataTypeDefinitionBinding type = Bind(
            session,
            Name("System.Text.Json", "JsonSerializer"));
        var ready = Assert.IsType<
            MetadataDeclaredMethodPopulationPreparation.Ready>(
                session.PrepareDeclaredMethods(type));

        MetadataDeclaredMethodPopulationResult count =
            ready.Source.Count();
        MetadataDeclaredMethodPopulationResult repeatedCount =
            ready.Source.Count();
        MetadataDeclaredMethodPopulationResult rows =
            ready.Source.Rows();
        MetadataDeclaredMethodPopulationResult repeatedRows =
            ready.Source.Rows();

        Assert.Equal(
            MetadataDeclaredMethodPopulationResultKind.Counted,
            count.Kind);
        Assert.Equal(
            MetadataDeclaredMethodPopulationResultKind.Read,
            rows.Kind);
        Assert.Equal(rows.Count, count.Count);
        Assert.Equal(rows.Rows.Length, count.Count);
        Assert.True(
            rows.Rows.AsSpan().SequenceEqual(
                repeatedRows.Rows.AsSpan()));
        Assert.Same(count.Receipt, repeatedCount.Receipt);
        Assert.Same(rows.Receipt, repeatedRows.Receipt);
        Assert.Equal(0, count.Receipt.MethodDefinitionHandlesVisited);
        Assert.Equal(
            rows.Count,
            rows.Receipt.MethodDefinitionHandlesVisited);
    }

    [Fact]
    public void PreparedSourceRejectsForeignBindingBeforeExecution()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(System.Text.Json.JsonSerializer).Assembly.Location);
        MetadataTypeDefinitionBinding type = Bind(
            session,
            Name("System.Text.Json", "JsonSerializer"));

        var rejected = Assert.IsType<
            MetadataDeclaredMethodPopulationPreparation.Rejected>(
                session.PrepareDeclaredMethods(
                    new(
                        Guid.NewGuid(),
                        type.Definition)));

        Assert.Equal(
            MetadataDeclaredMethodPopulationRejection
                .ModuleVersionIdMismatch,
            rejected.Reason);
    }

    [Fact]
    public void PreparedSourceRequiresIssuingSessionLifetime()
    {
        var session =
            AssemblyInspectionSession.Open(
                typeof(System.Text.Json.JsonSerializer).Assembly.Location);
        MetadataTypeDefinitionBinding type = Bind(
            session,
            Name("System.Text.Json", "JsonSerializer"));
        MetadataDeclaredMethodPopulationSource source =
            Assert.IsType<
                    MetadataDeclaredMethodPopulationPreparation.Ready>(
                    session.PrepareDeclaredMethods(type))
                .Source;
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => source.Count());
        Assert.Throws<ObjectDisposedException>(() => source.Rows());
    }

    [Fact]
    public void PreparedRowsBoundReturnsIncompletenessBeforeTraversal()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(System.Text.Json.JsonSerializer).Assembly.Location);
        MetadataTypeDefinitionBinding type = Bind(
            session,
            Name("System.Text.Json", "JsonSerializer"));
        MetadataDeclaredMethodPopulationSource source =
            Assert.IsType<
                    MetadataDeclaredMethodPopulationPreparation.Ready>(
                    session.PrepareDeclaredMethods(type))
                .Source;

        MetadataDeclaredMethodPopulationResult incomplete =
            source.Rows(maximumRows: 1);

        Assert.Equal(
            MetadataDeclaredMethodPopulationResultKind.Incomplete,
            incomplete.Kind);
        Assert.True(incomplete.Count > incomplete.MaximumRows);
        Assert.Equal(0, incomplete.Receipt.MethodDefinitionHandlesVisited);
        Assert.Equal(0, incomplete.Receipt.ProjectedRows);
    }

    [Fact]
    public void CountMatchesCompleteRowsWithoutMethodDefinitionWork()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(System.Text.Json.JsonSerializer).Assembly.Location);
        MetadataTypeDefinitionBinding type = Bind(
            session,
            Name("System.Text.Json", "JsonSerializer"));

        var count = Assert.IsType<
            MetadataDeclaredMethodPopulationOutcome.Counted>(
                session.DeclaredMethods(
                    new(
                        type,
                        MetadataDeclaredMethodPopulationTerminal.Count)));
        var rows = Assert.IsType<
            MetadataDeclaredMethodPopulationOutcome.Read>(
                session.DeclaredMethods(
                    new(
                        type,
                        MetadataDeclaredMethodPopulationTerminal.Rows)));

        Assert.Equal(rows.Rows.Length, count.Count);
        Assert.Equal(rows.Count, count.Count);
        Assert.True(count.Count > 100);
        Assert.Equal(0, count.Receipt.MethodDefinitionHandlesVisited);
        Assert.Equal(0, count.Receipt.MethodDefinitionRowsRead);
        Assert.Equal(0, count.Receipt.MethodNamesDecoded);
        Assert.Equal(0, count.Receipt.MethodSignaturesDecoded);
        Assert.Equal(0, count.Receipt.MethodAttributesDecoded);
        Assert.Equal(0, count.Receipt.ProjectedRows);
        Assert.Equal(rows.Count, rows.Receipt.MethodDefinitionHandlesVisited);
        Assert.Equal(rows.Count, rows.Receipt.ProjectedRows);
        Assert.Equal(0, rows.Receipt.MethodDefinitionRowsRead);
    }

    [Fact]
    public void EmptyTypeHasExactZeroCountAndRows()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(EmptyMethodProbe).Assembly.Location);
        MetadataTypeDefinitionBinding type = Bind(
            session,
            Name(
                typeof(EmptyMethodProbe).Namespace ?? "",
                nameof(EmptyMethodProbe)));

        var count = Assert.IsType<
            MetadataDeclaredMethodPopulationOutcome.Counted>(
                session.DeclaredMethods(
                    new(
                        type,
                        MetadataDeclaredMethodPopulationTerminal.Count)));
        var rows = Assert.IsType<
            MetadataDeclaredMethodPopulationOutcome.Read>(
                session.DeclaredMethods(
                    new(
                        type,
                        MetadataDeclaredMethodPopulationTerminal.Rows)));

        Assert.Equal(0, count.Count);
        Assert.Empty(rows.Rows);
        Assert.Equal(0, rows.Count);
    }

    [Fact]
    public void ForeignModuleBindingIsRejected()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(System.Text.Json.JsonSerializer).Assembly.Location);
        MetadataTypeDefinitionBinding type = Bind(
            session,
            Name("System.Text.Json", "JsonSerializer"));
        var foreign = new MetadataTypeDefinitionBinding(
            Guid.NewGuid(),
            type.Definition);

        var rejected = Assert.IsType<
            MetadataDeclaredMethodPopulationOutcome.Rejected>(
                session.DeclaredMethods(
                    new(
                        foreign,
                        MetadataDeclaredMethodPopulationTerminal.Count)));

        Assert.Equal(
            MetadataDeclaredMethodPopulationRejection
                .ModuleVersionIdMismatch,
            rejected.Reason);
    }

    [Fact]
    public void RowsBoundReturnsVisibleIncompletenessBeforeTraversal()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(System.Text.Json.JsonSerializer).Assembly.Location);
        MetadataTypeDefinitionBinding type = Bind(
            session,
            Name("System.Text.Json", "JsonSerializer"));

        var incomplete = Assert.IsType<
            MetadataDeclaredMethodPopulationOutcome.Incomplete>(
                session.DeclaredMethods(
                    new(
                        type,
                        MetadataDeclaredMethodPopulationTerminal.Rows,
                        maximumRows: 1)));

        Assert.True(incomplete.Count > incomplete.MaximumRows);
        Assert.Equal(0, incomplete.Receipt.MethodDefinitionHandlesVisited);
        Assert.Equal(0, incomplete.Receipt.ProjectedRows);
    }

    private static MetadataTypeDefinitionBinding Bind(
        AssemblyInspectionSession session,
        MetadataTypeDefinitionName name)
    {
        var defined = Assert.IsType<TypeDeclarationResult.Defined>(
            session.ProbeDeclaration(name));
        return new(session.ModuleVersionId(), defined.Definition);
    }

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        MetadataTypeDefinitionName.Create(@namespace, [.. segments])
            is MetadataTypeDefinitionNameResult.Valid valid
                ? valid.Name
                : throw new InvalidOperationException(
                    "The test Type name was invalid.");
}

internal interface EmptyMethodProbe
{
}
