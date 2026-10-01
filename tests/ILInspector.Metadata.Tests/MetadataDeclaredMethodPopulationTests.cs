using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataDeclaredMethodPopulationTests
{
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
