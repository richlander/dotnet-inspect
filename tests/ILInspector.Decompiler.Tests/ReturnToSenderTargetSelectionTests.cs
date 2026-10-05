using ILInspector.CSharp;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

using DotnetInspector.ResearchQueries;
using DotnetInspector.ResearchSections;

using QuerySpace;
using QuerySpace.Composition;

using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

namespace ILInspector.Decompiler.Tests;

[Collection(ConsoleMutatorCollection.Name)]
public class ReturnToSenderTargetSelectionTests
{
    [Fact]
    public void AppliesCapAfterExactDeclarationEligibility()
    {
        string unavailableAssembly =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                public interface IDeferredContract
                {
                    int Transform(int value);
                }

                public readonly struct DeferredExplicitFixture : IDeferredContract
                {
                    int IDeferredContract.Transform(int value) => value;
                }
                """, assemblyName: "UnavailableExactDeclaration");
        string eligibleAssembly =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                public static class EligibleOrdinaryFixture
                {
                    public static int Transform(int value) => value + 1;
                }
                """, assemblyName: "EligibleOrdinaryDeclaration");
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selection =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [unavailableAssembly, eligibleAssembly],
                    cap: 1);

            ReturnToSenderTarget selected =
                Assert.Single(selection.Targets);
            Assert.Equal(eligibleAssembly, selected.AssemblyPath);
            Assert.IsType<
                ReturnToSenderDeclarationSelection
                    .OrdinaryMethod>(selected.Declaration);
            Assert.Equal(1, selection.EligibleCount);

            ReturnToSenderTargetExclusion exclusion =
                Assert.Single(
                    selection.Exclusions,
                    exclusion => exclusion.Method.EndsWith(
                        ".Transform",
                        StringComparison.Ordinal));
            Assert.Equal(
                ReturnToSenderTargetExclusionReason
                    .ExactDeclarationUnavailable,
                exclusion.Reason);
            Assert.Equal(
                ReturnToSenderDeclarationProducer
                    .ExactMethodDeclaration,
                exclusion.Producer);
            var unavailable = Assert.IsType<
                CSharpDeclarationRepresentabilityResult.Unavailable>(
                    exclusion.ExactOutcome);
            Assert.Equal(
                CSharpDeclarationUnavailableReason.OutsideInitialBoundary,
                unavailable.Reason);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(
                unavailableAssembly);
            FidelityCheckGeneratedFilterTests.DeleteFixture(
                eligibleAssembly);
        }
    }

    [Fact]
    public void AdoptsOrdinaryAndExplicitAccessorDeclarations()
    {
        string assemblyPath =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                using System;

                public interface IValueContract
                {
                    int Value { get; }
                    int this[int index] { get; set; }
                    event Action Changed;
                }

                public sealed class ExplicitAccessorFixture : IValueContract
                {
                    public int OrdinaryValue => 7;

                    int IValueContract.Value => 8;

                    int IValueContract.this[int index]
                    {
                        get => index;
                        set { }
                    }

                    event Action IValueContract.Changed
                    {
                        add { }
                        remove { }
                    }

                    public static int Good() => 1;
                }
                """, assemblyName: "ExactAccessor");
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selection =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [assemblyPath],
                    cap: int.MaxValue);

            Assert.Contains(
                selection.Targets,
                target => target.Method == "Good");
            ReturnToSenderTarget[] accessors =
            [
                .. selection.Targets.Where(
                    target => target.Method.Contains(
                            "get_",
                            StringComparison.Ordinal)
                        || target.Method.Contains(
                            "set_",
                            StringComparison.Ordinal)
                        || target.Method.Contains(
                            "add_",
                            StringComparison.Ordinal)
                        || target.Method.Contains(
                            "remove_",
                            StringComparison.Ordinal)),
            ];
            Assert.Equal(6, accessors.Length);
            Assert.All(
                accessors,
                target =>
                {
                    var exact = Assert.IsType<
                        ReturnToSenderDeclarationSelection
                            .ExactAccessor>(target.Declaration);
                    Assert.Equal(
                        target.Address,
                        exact.Request.Coordinate.Method);
                    CSharpAcceptedAccessorBinding selected =
                        Assert.Single(
                            exact.Request.Accessors,
                            accessor => accessor.Occurrence.Method.Method
                                == target.Address);
                    Assert.Equal(
                        CSharpAccessorBodyPolicy.SelectedBody,
                        selected.BodyPolicy);
                    Assert.All(
                        exact.Request.Accessors.Where(
                            accessor => accessor != selected),
                        accessor => Assert.Equal(
                            CSharpAccessorBodyPolicy.SiblingStub,
                            accessor.BodyPolicy));
                });
            Assert.Equal(
                2,
                accessors.Count(target =>
                    ((ReturnToSenderDeclarationSelection
                        .ExactAccessor)target.Declaration!).Request.Kind
                    == CSharpAccessorDeclarationKind.Property));
            Assert.Equal(
                2,
                accessors.Count(target =>
                    ((ReturnToSenderDeclarationSelection
                        .ExactAccessor)target.Declaration!).Request.Kind
                    == CSharpAccessorDeclarationKind.Indexer));
            Assert.Equal(
                2,
                accessors.Count(target =>
                    ((ReturnToSenderDeclarationSelection
                        .ExactAccessor)target.Declaration!).Request.Kind
                    == CSharpAccessorDeclarationKind.Event));
            Assert.Null(
                ((ReturnToSenderDeclarationSelection
                    .ExactAccessor)Assert.Single(
                        accessors,
                        target => target.Method == "get_OrdinaryValue")
                    .Declaration!).Request.ExplicitInterface);
            Assert.All(
                accessors.Where(
                    target => target.Method != "get_OrdinaryValue"),
                target => Assert.NotNull(
                    ((ReturnToSenderDeclarationSelection
                        .ExactAccessor)target.Declaration!)
                    .Request.ExplicitInterface));
            Assert.DoesNotContain(
                selection.Exclusions,
                exclusion => exclusion.Producer
                    == ReturnToSenderDeclarationProducer
                        .ExactAccessorDeclaration);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void NLinqPopulationMatchesCompilerProducedTargetDecisions()
    {
        string assemblyPath =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                using System;

                public interface ITargetContract
                {
                    int Transform(int value);
                    int Value { get; }
                    int this[int index] { get; set; }
                    event Action Changed;
                }

                public sealed class TargetPopulationFixture :
                    ITargetContract
                {
                    public static int Ordinary(int value) => value + 1;

                    int ITargetContract.Transform(int value) => value;

                    int ITargetContract.Value => 8;

                    int ITargetContract.this[int index]
                    {
                        get => index;
                        set { }
                    }

                    event Action ITargetContract.Changed
                    {
                        add { }
                        remove { }
                    }
                }
                """, assemblyName: "TargetPopulation");
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selector =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [assemblyPath],
                    cap: int.MaxValue);
            FidelityCheck.ReturnToSenderTargetSelection oracle =
                FidelityCheck
                    .QueryReturnToSenderTargetPopulationWithNLinq(
                        [assemblyPath]);

            AssertEquivalent(selector, oracle);
            Assert.Contains(
                oracle.Targets,
                target => target.Declaration
                    is ReturnToSenderDeclarationSelection
                        .OrdinaryMethod);
            Assert.Contains(
                oracle.Targets,
                target => target.Declaration
                    is ReturnToSenderDeclarationSelection
                        .ExactAccessor);
            Assert.All(
                oracle.Targets,
                target =>
                {
                    switch (target.Declaration)
                    {
                        case ReturnToSenderDeclarationSelection
                            .ExactMethod exact:
                            Assert.Equal(
                                target.Address,
                                exact.Request.Body);
                            break;
                        case ReturnToSenderDeclarationSelection
                            .ExactAccessor exact:
                            Assert.Equal(
                                target.Address,
                                exact.Request.Coordinate.Method);
                            break;
                    }
                });
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(
                assemblyPath);
        }
    }

    [Fact]
    public void QuerySpaceCountMatchesNLinqWithoutMaterializingRows()
    {
        string assemblyPath =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                using System;

                public interface ICountContract
                {
                    int Value { get; }
                    event Action Changed;
                }

                public sealed class CountPopulationFixture :
                    ICountContract
                {
                    public static int Ordinary(int value) => value + 1;

                    int ICountContract.Value => 8;

                    event Action ICountContract.Changed
                    {
                        add { }
                        remove { }
                    }
                }
                """, assemblyName: "CountPopulation");
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection oracle =
                FidelityCheck
                    .QueryReturnToSenderTargetPopulationWithNLinq(
                        [assemblyPath]);
            var counted = Assert.IsType<
                ReturnToSenderTargetCountOutcome.Counted>(
                    ReturnToSenderTargetCount.Count(
                        [assemblyPath]));

            Assert.Equal(oracle.EligibleCount, counted.Count);
            Assert.Equal(
                oracle.ScannedBodyCount,
                counted.Receipt.ScannedBodyCount);
            Assert.Equal(
                oracle.DeclarationCandidateCount,
                counted.Receipt.DeclarationCandidateCount);
            Assert.Equal(0, counted.Receipt.MaterializedRowCount);
            ReturnToSenderTargetAssemblyCountReceipt assembly =
                Assert.Single(counted.Receipt.Assemblies);
            Assert.Equal(assemblyPath, assembly.Identity);
            Assert.Equal(
                oracle.EligibleCount,
                assembly.EligibleCount);
            Assert.Equal(0, assembly.MaterializedRowCount);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(
                assemblyPath);
        }
    }

    [Fact]
    public void QuerySpaceTargetPopulationAdmitsOnlyExactCount()
    {
        QuerySpaceRequest request =
            ReturnToSenderTargetQuery.CreateCountRequest();
        Assert.IsType<
            ReturnToSenderTargetQueryResolution.Accepted>(
                ReturnToSenderTargetQuery.ResolveRequest(request));
        Assert.Equal(
            QuerySpaceTerminalRequirement.Count,
            request.Terminal);
        Assert.Equal(
            ReturnToSenderTargetQuery.CountResultContract,
            request.ResultContract);

        Assert.Throws<ArgumentException>(
            () => QuerySpaceRequest.Create(
                ReturnToSenderTargetQuery.QuerySpace.Descriptor,
                PortableQueryIntent.Empty,
                [ReturnToSenderTargetQuery.RowSet],
                [
                    new(
                        ReturnToSenderTargetQuery.RowScopeIdentity,
                        PortableQueryIntent.Empty,
                        [ReturnToSenderTargetQuery.RowSet]),
                ],
                QuerySpaceTerminalRequirement.Rows));
    }

    [Fact]
    public void QuerySpaceCountAggregatesAssembliesExactly()
    {
        string assemblyPath =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                public static class CountAggregationFixture
                {
                    public static int First(int value) => value + 1;
                    public static int Second(int value) => value + 2;
                }
                """, assemblyName: "CountAggregation");
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection oracle =
                FidelityCheck
                    .QueryReturnToSenderTargetPopulationWithNLinq(
                        [assemblyPath]);
            var counted = Assert.IsType<
                ReturnToSenderTargetCountOutcome.Counted>(
                    ReturnToSenderTargetCount.Count(
                        [assemblyPath, assemblyPath]));

            Assert.Equal(checked(oracle.EligibleCount * 2), counted.Count);
            Assert.Equal(
                checked(oracle.ScannedBodyCount * 2),
                counted.Receipt.ScannedBodyCount);
            Assert.Equal(
                checked(oracle.DeclarationCandidateCount * 2),
                counted.Receipt.DeclarationCandidateCount);
            Assert.Equal(2, counted.Receipt.Assemblies.Count);
            Assert.All(
                counted.Receipt.Assemblies,
                assembly =>
                {
                    Assert.Equal(
                        oracle.EligibleCount,
                        assembly.EligibleCount);
                    Assert.Equal(0, assembly.MaterializedRowCount);
                });
            Assert.Equal(0, counted.Receipt.MaterializedRowCount);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(
                assemblyPath);
        }
    }

    [Fact]
    public void QuerySpaceCountRejectsForeignRequestsBeforeSourceWork()
    {
        QuerySpaceBinding foreign =
            QuerySpaceBinding.Create(
                "foreign-return-to-sender/query-space/v1",
                ReturnToSenderTargetQuery.OperationRoute,
                [ReturnToSenderTargetQuery.RowScope],
                [QuerySpaceTerminalRequirement.Count],
                acceptsContinuation: false,
                [
                    new(
                        QuerySpaceTerminalRequirement.Count,
                        ReturnToSenderTargetQuery.CountResultContract),
                ]);
        QuerySpaceRequest request =
            QuerySpaceRequest.Create(
                foreign.Descriptor,
                PortableQueryIntent.Empty,
                [ReturnToSenderTargetQuery.RowSet],
                [
                    new(
                        ReturnToSenderTargetQuery.RowScopeIdentity,
                        PortableQueryIntent.Empty,
                        [ReturnToSenderTargetQuery.RowSet]),
                ],
                QuerySpaceTerminalRequirement.Count);

        var rejected = Assert.IsType<
            ReturnToSenderTargetCountOutcome.Rejected>(
                ReturnToSenderTargetInspection.Count(
                    [],
                    request));
        Assert.Equal(
            ReturnToSenderTargetQueryRejectionKind.QuerySpaceMismatch,
            Assert.IsType<
                    ReturnToSenderTargetQueryResolution.Rejected>(
                    rejected.Resolution)
                .Kind);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void NLinqPopulationMatchesPinnedExactMethodDecision()
    {
        string assemblyPath = typeof(int).Assembly.Location;
        FidelityCheck.ReturnToSenderTargetSelection selector =
            FidelityCheck.SelectReturnToSenderTargetPlan(
                [assemblyPath],
                cap: int.MaxValue,
                typeFilter: "System.Int32");
        FidelityCheck.ReturnToSenderTargetSelection oracle =
            FidelityCheck
                .QueryReturnToSenderTargetPopulationWithNLinq(
                    [assemblyPath],
                    typeFilter: "System.Int32");

        AssertEquivalent(selector, oracle);
        ReturnToSenderTarget target =
            Assert.Single(
                oracle.Targets,
                target => target.Type == "System.Int32"
                    && target.Declaration is
                        ReturnToSenderDeclarationSelection
                            .ExactMethod);
        var exact = Assert.IsType<
            ReturnToSenderDeclarationSelection
                .ExactMethod>(target.Declaration);
        Assert.Equal(target.Address, exact.Request.Body);
    }

    [Fact]
    public void NLinqPopulationPreservesTypedExclusionRows()
    {
        string unrepresentableAssembly =
            CreateGeneratedEventRaiserFixture(
                "raise_Changed",
                isStaticType: true,
                includeGoodMethod: false);
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selector =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [unrepresentableAssembly],
                    cap: int.MaxValue);
            FidelityCheck.ReturnToSenderTargetSelection oracle =
                FidelityCheck
                    .QueryReturnToSenderTargetPopulationWithNLinq(
                        [unrepresentableAssembly]);

            AssertEquivalent(selector, oracle);
            Assert.All(
                oracle.Exclusions,
                exclusion =>
                {
                    Assert.Equal(
                        ReturnToSenderTargetExclusionReason
                            .ExactAccessorDeclarationUnrepresentable,
                        exclusion.Reason);
                    var refusal = Assert.IsType<
                        CSharpAccessorDeclarationRepresentabilityResult
                            .Unrepresentable>(
                                exclusion.AccessorOutcome);
                    Assert.Equal(
                        CSharpAccessorDeclarationRefusalReason
                            .UnsupportedSemanticOccurrence,
                        refusal.Reason);
                });
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(
                unrepresentableAssembly);
        }

        string unavailableAssembly =
            CreateGeneratedEventRaiserFixture(
                "unused",
                isStaticType: true,
                includeRemoveAccessor: false,
                includeRaiser: false,
                includeGoodMethod: false);
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selector =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [unavailableAssembly],
                    cap: int.MaxValue);
            FidelityCheck.ReturnToSenderTargetSelection oracle =
                FidelityCheck
                    .QueryReturnToSenderTargetPopulationWithNLinq(
                        [unavailableAssembly]);

            AssertEquivalent(selector, oracle);
            ReturnToSenderTargetExclusion exclusion =
                Assert.Single(oracle.Exclusions);
            Assert.Equal(
                ReturnToSenderTargetExclusionReason
                    .ExactAccessorDeclarationUnavailable,
                exclusion.Reason);
            var unavailable = Assert.IsType<
                CSharpAccessorDeclarationRepresentabilityResult
                    .Unavailable>(
                        exclusion.AccessorOutcome);
            Assert.Equal(
                CSharpAccessorDeclarationUnavailableReason
                    .AccessorDeclarationRejected,
                unavailable.Reason);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(
                unavailableAssembly);
        }
    }

    [Fact]
    public void OrdersExclusionsDeterministically()
    {
        string firstAssembly =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                public interface IExplicitOrder
                {
                    int Alpha(int value);
                    int Beta(int value);
                }

                public readonly struct ExplicitOrderFixture : IExplicitOrder
                {
                    int IExplicitOrder.Alpha(int value) => value;
                    int IExplicitOrder.Beta(int value) => value;
                }
                """, assemblyName: "ExplicitOrder");
        string reorderedAssembly =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                public interface IExplicitOrder
                {
                    int Alpha(int value);
                    int Beta(int value);
                }

                public readonly struct ExplicitOrderFixture : IExplicitOrder
                {
                    int IExplicitOrder.Beta(int value) => value;
                    int IExplicitOrder.Alpha(int value) => value;
                }
                """, assemblyName: "ExplicitOrder");
        try
        {
            string[] first = ExclusionSnapshot(
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [firstAssembly],
                    cap: int.MaxValue));
            string[] reordered = ExclusionSnapshot(
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [reorderedAssembly],
                    cap: int.MaxValue));

            Assert.Equal(first, reordered);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(firstAssembly);
            FidelityCheckGeneratedFilterTests.DeleteFixture(reorderedAssembly);
        }
    }

    [Theory]
    [InlineData("raise_Changed")]
    [InlineData("<raise_Changed>")]
    public void PreservesTypedEventRaiserRefusal(string raiserName)
    {
        string assemblyPath = CreateGeneratedEventRaiserFixture(raiserName);
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selection =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [assemblyPath],
                    cap: int.MaxValue);

            ReturnToSenderTargetExclusion raiser =
                Assert.Single(
                    selection.Exclusions,
                    exclusion => exclusion.Method == raiserName);
            Assert.Equal(
                ReturnToSenderTargetExclusionReason
                    .ExactAccessorDeclarationUnrepresentable,
                raiser.Reason);
            Assert.Equal(
                ReturnToSenderDeclarationProducer
                    .ExactAccessorDeclaration,
                raiser.Producer);
            var refusal = Assert.IsType<
                CSharpAccessorDeclarationRepresentabilityResult
                    .Unrepresentable>(raiser.AccessorOutcome);
            Assert.Equal(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedSemanticOccurrence,
                refusal.Reason);
            Assert.Contains(
                selection.Targets,
                target => target.Method == "Good");
            Assert.Equal(5, selection.DeclarationCandidateCount);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void AppliesCapAfterExactAccessorEligibility()
    {
        string refusedAssembly =
            CreateGeneratedEventRaiserFixture(
                "raise_Changed",
                isStaticType: true,
                includeGoodMethod: false);
        string eligibleAssembly =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                public static class EligibleAccessorFixture
                {
                    public static int Value => 2;
                }
                """, assemblyName: "EligibleExactAccessor");
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selection =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [refusedAssembly, eligibleAssembly],
                    cap: 1);

            ReturnToSenderTarget selected =
                Assert.Single(selection.Targets);
            Assert.Equal(eligibleAssembly, selected.AssemblyPath);
            Assert.Equal("get_Value", selected.Method);
            Assert.IsType<
                ReturnToSenderDeclarationSelection
                    .ExactAccessor>(selected.Declaration);
            Assert.Equal(1, selection.EligibleCount);

            ReturnToSenderTargetExclusion[] exclusions =
            [
                .. selection.Exclusions.Where(
                    exclusion => exclusion.AssemblyPath
                        == refusedAssembly),
            ];
            Assert.Equal(3, exclusions.Length);
            Assert.All(
                exclusions,
                exclusion =>
                {
                    Assert.Equal(
                        ReturnToSenderTargetExclusionReason
                            .ExactAccessorDeclarationUnrepresentable,
                        exclusion.Reason);
                    Assert.Equal(
                        ReturnToSenderDeclarationProducer
                            .ExactAccessorDeclaration,
                        exclusion.Producer);
                    var refusal = Assert.IsType<
                        CSharpAccessorDeclarationRepresentabilityResult
                            .Unrepresentable>(
                                exclusion.AccessorOutcome);
                    Assert.Equal(
                        CSharpAccessorDeclarationRefusalReason
                            .UnsupportedSemanticOccurrence,
                        refusal.Reason);
                });
            ReturnToSenderTargetExclusion raiser =
                Assert.Single(
                    exclusions,
                    exclusion => exclusion.Method == "raise_Changed");
            Assert.IsType<
                CSharpAccessorDeclarationRepresentabilityResult
                    .Unrepresentable>(raiser.AccessorOutcome);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(
                refusedAssembly);
            FidelityCheckGeneratedFilterTests.DeleteFixture(
                eligibleAssembly);
        }
    }

    [Fact]
    public void PreservesTypedIncompleteAccessorUnavailability()
    {
        string assemblyPath =
            CreateGeneratedEventRaiserFixture(
                "unused",
                isStaticType: true,
                includeRemoveAccessor: false,
                includeRaiser: false,
                includeGoodMethod: false);
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selection =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [assemblyPath],
                    cap: int.MaxValue);

            Assert.Empty(selection.Targets);
            ReturnToSenderTargetExclusion exclusion =
                Assert.Single(selection.Exclusions);
            Assert.Equal("add_Changed", exclusion.Method);
            Assert.Equal(
                ReturnToSenderTargetExclusionReason
                    .ExactAccessorDeclarationUnavailable,
                exclusion.Reason);
            Assert.Equal(
                ReturnToSenderDeclarationProducer
                    .ExactAccessorDeclaration,
                exclusion.Producer);
            var unavailable = Assert.IsType<
                CSharpAccessorDeclarationRepresentabilityResult.Unavailable>(
                    exclusion.AccessorOutcome);
            Assert.Equal(
                CSharpAccessorDeclarationUnavailableReason
                    .AccessorDeclarationRejected,
                unavailable.Reason);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void LegacyMemberIndexExcludesCompilerGeneratedHelpers()
    {
        string assemblyPath =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                public static class GeneratedHelperFixture
                {
                    public static int Good(int value)
                    {
                        return Local(value);

                        static int Local(int input) => input + 1;
                    }
                }
                """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            MetadataReader reader = pe.GetMetadataReader();
            TypeDefinition type = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(
                    reader.GetTypeDefinition(handle).Name)
                    == "GeneratedHelperFixture"));
            MethodDefinitionHandle generated = Assert.Single(
                type.GetMethods(),
                handle => reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                    .StartsWith("<Good>g__Local", StringComparison.Ordinal));
            using var source = MetadataSource.Open(assemblyPath);

            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                generated,
                targeted: false,
                isPrimaryConstructor: false));
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(assemblyPath);
        }
    }

    static string[] ExclusionSnapshot(
        FidelityCheck.ReturnToSenderTargetSelection selection)
        => selection.Exclusions
            .Select(exclusion =>
                $"{exclusion.Type}::{exclusion.Method}{exclusion.Signature}:"
                + $"{exclusion.Reason}:{exclusion.Producer}:"
                + $"{exclusion.ExactOutcome?.GetType().Name}:"
                + $"{exclusion.AccessorOutcome?.GetType().Name}")
            .ToArray();

    static void AssertEquivalent(
        FidelityCheck.ReturnToSenderTargetSelection selector,
        FidelityCheck.ReturnToSenderTargetSelection oracle)
    {
        Assert.Equal(
            selector.ScannedBodyCount,
            oracle.ScannedBodyCount);
        Assert.Equal(
            selector.DeclarationCandidateCount,
            oracle.DeclarationCandidateCount);
        Assert.Equal(
            selector.EligibleCount,
            oracle.EligibleCount);
        Assert.Equal(
            TargetSnapshot(selector),
            TargetSnapshot(oracle));
        Assert.Equal(
            ExclusionSnapshot(selector),
            ExclusionSnapshot(oracle));
    }

    static TargetSelectionSnapshot[] TargetSnapshot(
        FidelityCheck.ReturnToSenderTargetSelection selection)
        =>
        [
            .. selection.Targets.Select(
                target => new TargetSelectionSnapshot(
                    target.AssemblyPath,
                    target.Type,
                    target.Method,
                    target.Overload,
                    target.Signature,
                    target.Address,
                    target.Declaration switch
                    {
                        ReturnToSenderDeclarationSelection
                            .OrdinaryMethod =>
                            ReturnToSenderDeclarationProducer
                                .OrdinaryTypeArtifact,
                        ReturnToSenderDeclarationSelection
                            .ExactMethod =>
                            ReturnToSenderDeclarationProducer
                                .ExactMethodDeclaration,
                        ReturnToSenderDeclarationSelection
                            .ExactAccessor =>
                            ReturnToSenderDeclarationProducer
                                .ExactAccessorDeclaration,
                        _ => throw new InvalidOperationException(
                            "Unknown RTS declaration selection."),
                    })),
        ];

    readonly record struct TargetSelectionSnapshot(
        string AssemblyPath,
        string Type,
        string Method,
        int Overload,
        string Signature,
        ILInspector.MetadataPrimitives.MetadataMethodAddress? Address,
        ReturnToSenderDeclarationProducer Producer);

    static string CreateGeneratedEventRaiserFixture(
        string raiserName,
        bool isStaticType = false,
        bool includeRemoveAccessor = true,
        bool includeRaiser = true,
        bool includeGoodMethod = true)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"rts-generated-raiser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "GeneratedEventRaiser.dll");

        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("GeneratedEventRaiser"),
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule("GeneratedEventRaiser");
        TypeBuilder type = module.DefineType(
            "GeneratedEventRaiserFixture",
            TypeAttributes.Public
                | TypeAttributes.Class
                | (isStaticType
                    ? TypeAttributes.Abstract | TypeAttributes.Sealed
                    : 0));

        if (includeGoodMethod)
        {
            MethodBuilder good = type.DefineMethod(
                "Good",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int),
                Type.EmptyTypes);
            ILGenerator goodBody = good.GetILGenerator();
            goodBody.Emit(OpCodes.Ldc_I4_1);
            goodBody.Emit(OpCodes.Ret);
        }

        EventBuilder changed = type.DefineEvent(
            "Changed",
            EventAttributes.None,
            typeof(Action));
        MethodBuilder add = DefineEventMethod(
            type,
            "add_Changed",
            [typeof(Action)],
            isStaticType);
        changed.SetAddOnMethod(add);
        if (includeRemoveAccessor)
        {
            MethodBuilder remove = DefineEventMethod(
                type,
                "remove_Changed",
                [typeof(Action)],
                isStaticType);
            changed.SetRemoveOnMethod(remove);
        }
        if (includeRaiser)
        {
            MethodBuilder raise = DefineEventMethod(
                type,
                raiserName,
                Type.EmptyTypes,
                isStaticType);
            raise.SetCustomAttribute(
                new CustomAttributeBuilder(
                    typeof(CompilerGeneratedAttribute).GetConstructor(
                        Type.EmptyTypes)!,
                    []));
            changed.SetRaiseMethod(raise);
        }

        type.CreateType();
        assembly.Save(path);
        return path;
    }

    static MethodBuilder DefineEventMethod(
        TypeBuilder type,
        string name,
        Type[] parameterTypes,
        bool isStatic = false)
    {
        MethodBuilder method = type.DefineMethod(
            name,
            MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig
                | (isStatic ? MethodAttributes.Static : 0),
            typeof(void),
            parameterTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        return method;
    }
}
