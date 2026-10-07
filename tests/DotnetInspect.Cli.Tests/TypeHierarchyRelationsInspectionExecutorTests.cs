using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Tests;

public sealed class TypeHierarchyRelationsInspectionExecutorTests
{
    [Fact]
    public void CountOnlyRequestsNoRows()
    {
        SubjectRelationPopulationRequest request =
            TypeHierarchyRelationsInspectionExecutor.CreatePopulationRequest(
                new TypeOptions { Count = true },
                SubjectRelationForm.Interface);

        Assert.NotNull(request.Count);
        Assert.Null(request.Rows);
    }

    [Fact]
    public void BoundedRowsRequestNoCountAndPushesProducerPrefix()
    {
        SubjectRelationPopulationRequest request =
            TypeHierarchyRelationsInspectionExecutor.CreatePopulationRequest(
                new TypeOptions
                {
                    Rows = RowWindow.Range(3, 7),
                },
                SubjectRelationForm.BaseType);

        Assert.Null(request.Count);
        Assert.Equal(7, request.Rows?.MaximumRows);
    }

    [Fact]
    public void CountWithRowsRequestsBothTerminals()
    {
        SubjectRelationPopulationRequest request =
            TypeHierarchyRelationsInspectionExecutor.CreatePopulationRequest(
                new TypeOptions
                {
                    Count = true,
                    Rows = RowWindow.Head(5),
                },
                SubjectRelationForm.Interface);

        Assert.NotNull(request.Count);
        Assert.Equal(5, request.Rows?.MaximumRows);
    }
}
