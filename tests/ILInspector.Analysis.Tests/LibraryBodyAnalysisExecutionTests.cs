using DotnetInspector.Fixtures;
using System.Runtime.CompilerServices;

namespace ILInspector.Analysis.Tests;

public sealed class LibraryBodyAnalysisExecutionTests
{
    [Fact]
    public void ExecutePath_PublishesFocusedResultsWithOneReceipt()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles));

        Assert.Same(
            execution.Receipt,
            execution.Safety.Receipt);
        Assert.Same(
            execution.Receipt,
            execution.ImplementationProfiles.Receipt);
        Assert.False(
            execution.Safety.Evidence.IsDefault);
        Assert.True(
            execution.Safety.WasRequested);
        Assert.True(
            execution.ImplementationProfiles.WasRequested);
        Assert.True(
            execution.Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures.MethodEvidence));
        Assert.False(
            execution.Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures.Allocations));
        Assert.False(
            execution.Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities));
        Assert.NotEmpty(
            execution.ImplementationProfiles.Profiles);
        Assert.NotEmpty(
            execution.ImplementationProfiles
                .OverloadRelationships);
    }

    [Fact]
    public void ExecutePath_PublishesFocusedOptimizationResult()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities));

        Assert.Same(
            execution.Receipt,
            execution.Optimization.Receipt);
        Assert.True(
            execution.Optimization.WasRequested);
        Assert.NotEmpty(
            execution.Optimization.Opportunities);
        Assert.NotEmpty(
            execution.Optimization
                .AllocationFanoutOpportunities);
    }

    [Fact]
    public void ExecutePath_DoesNotProduceUnrequestedSafetyEvidence()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.None));

        Assert.False(
            execution.Safety.WasRequested);
        Assert.Empty(
            execution.Safety.Evidence);
    }

    [Fact]
    public void ExecutePath_DoesNotProduceUnrequestedProfiles()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));

        Assert.False(
            execution.ImplementationProfiles.WasRequested);
        Assert.Empty(
            execution.ImplementationProfiles.Profiles);
        Assert.Empty(
            execution.ImplementationProfiles
                .OverloadRelationships);
        Assert.Empty(
            execution.ImplementationProfiles
                .GeneratedFrameworkTypes);
    }

    [Fact]
    public void ExecutePath_DoesNotProduceUnrequestedOptimizationEvidence()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));

        Assert.False(
            execution.Optimization.WasRequested);
        Assert.Empty(
            execution.Optimization.Opportunities);
        Assert.Empty(
            execution.Optimization
                .AllocationFanoutOpportunities);
        Assert.Empty(
            execution.Optimization
                .GeneratedFrameworkTypes);
    }

    [Fact]
    public void CompatibilityIndex_PreservesFocusedProfileResults()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles));

        LibraryBodyIndex index =
            execution.CompatibilityIndex();

        Assert.Equal(
            execution.ImplementationProfiles.Profiles,
            index.ImplementationProfiles());
        Assert.Equal(
            execution.ImplementationProfiles
                .OverloadRelationships,
            index.OverloadRelationships());
        Assert.True(
            execution.ImplementationProfiles
                .GeneratedFrameworkTypes.SetEquals(
                    index.GeneratedFrameworkTypes));
    }

    [Fact]
    public void CompatibilityIndex_PreservesFocusedOptimizationResults()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities));

        LibraryBodyIndex index =
            execution.CompatibilityIndex();

        Assert.Equal(
            execution.Optimization.Opportunities,
            index.OptimizationOpportunities);
        Assert.Equal(
            execution.Optimization
                .AllocationFanoutOpportunities,
            index.AllocationFanoutOpportunities);
        Assert.True(
            execution.Optimization
                .GeneratedFrameworkTypes.SetEquals(
                    index.GeneratedFrameworkTypes));
        Assert.Equal(
            execution.Receipt.Diagnostics,
            index.Diagnostics);
    }

    [Fact]
    public void FocusedOptimizationResult_DoesNotRetainExecution()
    {
        (
            LibraryOptimizationAnalysisResult result,
            WeakReference<LibraryBodyAnalysisExecution> execution
        ) = CreateDetachedOptimizationResult();

        for (int attempt = 0; attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(execution.TryGetTarget(out _));
        Assert.NotEmpty(result.Opportunities);
        Assert.NotNull(result.GeneratedFrameworkTypes);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (
        LibraryOptimizationAnalysisResult Result,
        WeakReference<LibraryBodyAnalysisExecution> Execution
    ) CreateDetachedOptimizationResult()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities));
        return (
            execution.Optimization,
            new(execution));
    }
}
