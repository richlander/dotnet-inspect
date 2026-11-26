using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis.Planning;
using QuerySpace.Operations;

namespace ILInspector.Analysis.Tests;

public sealed partial class MethodDefinitionRequestSetTests
{
    [Fact]
    public void Plan_SharesOnlyIdenticalExactMethodGeneratedExpansion()
    {
        MethodDefinitionSourceBreadth TypeBreadth(
            MethodDefinitionGeneratedBodyRoles roles,
            bool candidates = false) =>
            MethodDefinitionSourceBreadth
                .ExactTypes(MetadataTokens.TypeDefinitionHandle(2))
                .IncludeGeneratedExecutionBodies(
                    roles: roles,
                    includeUnauthenticatedCandidates: candidates);

        MethodDefinitionSourceBreadth MethodBreadth(
            MethodDefinitionGeneratedBodyRoles roles) =>
            MethodDefinitionSourceBreadth
                .ExactMethods(MetadataTokens.MethodDefinitionHandle(1))
                .IncludeGeneratedExecutionBodies(roles: roles);

        MethodDefinitionSourceRequestSetPlan exactEqual =
            AcceptedPlan(
            [
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Count,
                    MethodBreadth(MethodDefinitionGeneratedBodyRoles.All)),
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Exists,
                    MethodBreadth(MethodDefinitionGeneratedBodyRoles.All)),
            ]);
        MethodDefinitionSourceRequestSetPlan typeEqual =
            AcceptedPlan(
            [
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Count,
                    TypeBreadth(MethodDefinitionGeneratedBodyRoles.All)),
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Exists,
                    TypeBreadth(MethodDefinitionGeneratedBodyRoles.All)),
            ]);
        MethodDefinitionSourceRequestSetPlan roles =
            AcceptedPlan(
            [
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Count,
                    TypeBreadth(
                        MethodDefinitionGeneratedBodyRoles.Execution)),
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Exists,
                    TypeBreadth(MethodDefinitionGeneratedBodyRoles.All)),
            ]);
        MethodDefinitionSourceRequestSetPlan candidates =
            AcceptedPlan(
            [
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Count,
                    TypeBreadth(MethodDefinitionGeneratedBodyRoles.All)),
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Exists,
                    TypeBreadth(
                        MethodDefinitionGeneratedBodyRoles.All,
                        candidates: true)),
            ]);

        Assert.Single(exactEqual.Groups);
        Assert.Equal(2, Assert.Single(exactEqual.Groups).Lanes.Length);
        Assert.Equal(2, typeEqual.Groups.Length);
        Assert.Equal(2, roles.Groups.Length);
        Assert.Equal(2, candidates.Groups.Length);
    }
}
