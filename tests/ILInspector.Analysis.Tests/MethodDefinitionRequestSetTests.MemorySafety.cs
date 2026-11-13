using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed partial class MethodDefinitionRequestSetTests
{
    [Fact]
    public void MethodQuerySource_MemorySafetyContractsAbsentWhenUndeclared()
    {
        MethodDefinitionSourceAssociation undeclared =
            Association(
                CountingProducer.Instance,
                ProducerTerminal.Count);
        MethodDefinitionSourceAssociation silent =
            Association(
                MemorySafetyProbe.Silent,
                ProducerTerminal.Count);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([undeclared, silent]));

        Assert.Same(
            MemorySafetyContractCoverage.Empty,
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.MemorySafetyContracts);
        Assert.Same(
            MemorySafetyContractCoverage.Empty,
            execution.ResultOf(undeclared)
                .SourceReceipt.Coverage.MemorySafetyContracts);
        Assert.Same(
            MemorySafetyContractCoverage.Empty,
            execution.ResultOf(silent)
                .SourceReceipt.Coverage.MemorySafetyContracts);
        Assert.Contains(
            execution.ResultOf(silent).WorkReceipt.Producers[0].Layers,
            layer => layer
                == new ProducerLayerParticipation(
                    nameof(MethodDefinitionLayers.MemorySafetyContracts),
                    0));
    }

    [Fact]
    public void MethodQuerySource_MemorySafetyContractsRequireDeclaration()
    {
        MethodDefinitionSourceAssociation undeclared =
            Association(
                UndeclaredMemorySafetyProbe.Instance,
                ProducerTerminal.Count);

        ProducerContractException exception =
            Assert.Throws<ProducerContractException>(
                () => Execute(AcceptedPlan([undeclared])));

        Assert.Contains(
            "memory-safety contract support",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MethodQuerySource_MemorySafetyContractsReceiptExactMethodWork()
    {
        string path = typeof(MethodDefinitionRequestSetTests).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        PropertyDefinitionHandle property =
            Assert.Single(
                reader.GetTypeDefinition(
                    FindTypeDefinition(
                        reader,
                        typeof(MemorySafetyProbeTarget)))
                    .GetProperties());
        MethodDefinitionHandle getter =
            reader.GetPropertyDefinition(property).GetAccessors().Getter;
        MemorySafetyMetadataIndex oracle =
            MemorySafetyMetadataIndex.CreateReceipted(reader);
        MemorySafetyMetadataWork construction = oracle.ConstructionWork!;
        MemorySafetyMethodContract expected =
            new(
                oracle.GetMemberContract(getter),
                oracle.GetAccessorAssociation(getter));
        MemorySafetyMetadataWork queryWork =
            oracle.RecordedWork!.Since(construction);

        MethodDefinitionSourceAssociation exact =
            Association(
                MemorySafetyProbe.Recording,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(getter));
        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([exact]));
        MethodDefinitionSourceRequestResult result =
            execution.ResultOf(exact);
        MethodDefinitionSourceCoverage coverage =
            result.SourceReceipt.Coverage;

        Assert.Equal(
            new MemorySafetyAccessorAssociationResult.Associated(
                MetadataTokens.GetToken(property),
                MemorySafetyAccessorRole.PropertyGetter),
            expected.Association);
        Assert.Equal(
            expected,
            Assert.Single(
                ResultOf<ImmutableList<MemorySafetyMethodContract>>(
                        execution,
                        exact)
                    .Value!));
        Assert.Equal(1, coverage.MethodsSelected.Count);
        Assert.True(coverage.MethodsSelected.Contains(getter));
        Assert.Equal(0, coverage.BodiesAttempted.Count);
        Assert.Equal(0, coverage.BodiesAcquired.Count);
        Assert.Equal(0, coverage.ModuleLookupMethods.Count);
        Assert.Equal(
            new MemorySafetyContractCoverage(
                Constructed: true,
                construction,
                queryWork,
                Queries: 1,
                coverage.MemorySafetyContracts.QueryingMethods),
            coverage.MemorySafetyContracts);
        Assert.Equal(
            1,
            coverage.MemorySafetyContracts.QueryingMethods.Count);
        Assert.True(
            coverage.MemorySafetyContracts.QueryingMethods.Contains(getter));
        Assert.True(construction.CustomAttributeOrderRows > 0);
        Assert.Equal(
            reader.GetTableRowCount(TableIndex.MethodSemantics),
            construction.AssociationMethodSemanticsRows);
    }

    [Fact]
    public void MethodQuerySource_MemorySafetyContractsSharedOnceAcrossLanes()
    {
        MethodDefinitionSourceAssociation first =
            Association(
                MemorySafetyProbe.Counting,
                ProducerTerminal.Count);
        MethodDefinitionSourceAssociation second =
            Association(
                MemorySafetyProbe.SecondCounting,
                ProducerTerminal.Count);
        MethodDefinitionSourceAssociation undeclared =
            Association(
                CountingProducer.Instance,
                ProducerTerminal.Count);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([first, second, undeclared]));
        MemorySafetyContractCoverage physical =
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.MemorySafetyContracts;
        MemorySafetyContractCoverage firstCoverage =
            execution.ResultOf(first)
                .SourceReceipt.Coverage.MemorySafetyContracts;
        MemorySafetyContractCoverage secondCoverage =
            execution.ResultOf(second)
                .SourceReceipt.Coverage.MemorySafetyContracts;
        int methods = ValueOf(execution, undeclared);

        Assert.True(methods > 0);
        Assert.Equal(methods, ValueOf(execution, first));
        Assert.Equal(methods, ValueOf(execution, second));

        // One construction, charged to the lane whose query created it.
        Assert.True(physical.Constructed);
        Assert.True(firstCoverage.Constructed);
        Assert.False(secondCoverage.Constructed);
        Assert.Equal(physical.ConstructionWork, firstCoverage.ConstructionWork);
        Assert.Equal(
            MemorySafetyMetadataWork.Empty,
            secondCoverage.ConstructionWork);

        // Each lane receipts only its own queries; the group receipts all.
        Assert.Equal(methods, firstCoverage.Queries);
        Assert.Equal(methods, secondCoverage.Queries);
        Assert.Equal(2 * methods, physical.Queries);
        Assert.Equal(methods, firstCoverage.QueryingMethods.Count);
        Assert.Equal(methods, physical.QueryingMethods.Count);
        Assert.Equal(firstCoverage.QueryWork, secondCoverage.QueryWork);
        Assert.Equal(
            firstCoverage.QueryWork.Plus(secondCoverage.QueryWork),
            physical.QueryWork);
        Assert.True(firstCoverage.QueryWork.MemberAttributeRows > 0);
        Assert.Same(
            MemorySafetyContractCoverage.Empty,
            execution.ResultOf(undeclared)
                .SourceReceipt.Coverage.MemorySafetyContracts);
        Assert.Equal(
            0,
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.BodiesAcquired.Count);
    }

    static TypeDefinitionHandle FindTypeDefinition(
        MetadataReader reader,
        Type type) =>
        Assert.Single(
            reader.TypeDefinitions,
            handle =>
            {
                TypeDefinition definition = reader.GetTypeDefinition(handle);
                return reader.StringComparer.Equals(
                        definition.Name,
                        type.Name)
                    && reader.StringComparer.Equals(
                        definition.Namespace,
                        type.Namespace!);
            });

    /// <summary>Queries its own contract at every visited method.</summary>
    sealed class MemorySafetyProbe
        : MethodDefinitionProducer<
            MemorySafetyMethodContract?,
            ImmutableList<MemorySafetyMethodContract>,
            ImmutableList<MemorySafetyMethodContract>>
    {
        readonly bool _query;

        MemorySafetyProbe(string identity, bool query)
            : base(
                identity,
                version: 1,
                tier: 0,
                MethodDefinitionLayers.MemorySafetyContracts)
        {
            _query = query;
        }

        public static MemorySafetyProbe Recording { get; } =
            new("MemorySafetyProbe", query: true);

        public static MemorySafetyProbe Silent { get; } =
            new("SilentMemorySafetyProbe", query: false);

        public static MemorySafetyCountingProbe Counting { get; } =
            new("CountingMemorySafetyProbe");

        public static MemorySafetyCountingProbe SecondCounting { get; } =
            new("SecondCountingMemorySafetyProbe");

        internal override MemorySafetyMethodContract? Visit(
            scoped MethodDefinitionView view) =>
            _query
                ? view.GetMemorySafetyMethodContract(view.MethodHandle)
                : null;

        internal override ImmutableList<MemorySafetyMethodContract> Seed() =>
            [];

        internal override ImmutableList<MemorySafetyMethodContract> Accumulate(
            ImmutableList<MemorySafetyMethodContract> accumulator,
            MemorySafetyMethodContract? fact) =>
            fact is null ? accumulator : accumulator.Add(fact);

        internal override ImmutableList<MemorySafetyMethodContract> Complete(
            ImmutableList<MemorySafetyMethodContract> accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }

    /// <summary>Counts the methods whose contract it queried.</summary>
    sealed class MemorySafetyCountingProbe(string identity)
        : MethodDefinitionProducer<int, int, int>(
            identity,
            version: 1,
            tier: 0,
            MethodDefinitionLayers.MemorySafetyContracts)
    {
        internal override int Visit(scoped MethodDefinitionView view)
        {
            _ = view.GetMemorySafetyMethodContract(view.MethodHandle);
            return 1;
        }

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }

    sealed class UndeclaredMemorySafetyProbe
        : MethodDefinitionProducer<int, int, int>
    {
        UndeclaredMemorySafetyProbe()
            : base(
                "UndeclaredMemorySafetyProbe",
                version: 1,
                tier: 0,
                MethodDefinitionLayers.Flags)
        {
        }

        public static UndeclaredMemorySafetyProbe Instance { get; } = new();

        internal override int Visit(scoped MethodDefinitionView view)
        {
            _ = view.GetMemorySafetyRules();
            return 1;
        }

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }
}

/// <summary>An exact accessor target for memory-safety contract support.</summary>
public static class MemorySafetyProbeTarget
{
    public static int Value { get; set; }
}
