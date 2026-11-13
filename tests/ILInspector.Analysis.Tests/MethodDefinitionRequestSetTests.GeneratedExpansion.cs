using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis.Planning;
using QuerySpace.Operations;

namespace ILInspector.Analysis.Tests;

public sealed partial class MethodDefinitionRequestSetTests
{
    [Fact]
    public void Plan_KeepsGeneratedExpansionRequestsInSeparateGroups()
    {
        MethodDefinitionSourceBreadth Breadth(
            MethodDefinitionGeneratedBodyRoles roles,
            bool candidates = false) =>
            MethodDefinitionSourceBreadth
                .ExactTypes(MetadataTokens.TypeDefinitionHandle(2))
                .IncludeGeneratedExecutionBodies(
                    roles: roles,
                    includeUnauthenticatedCandidates: candidates);

        MethodDefinitionSourceRequestSetPlan equal =
            AcceptedPlan(
            [
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Count,
                    Breadth(MethodDefinitionGeneratedBodyRoles.All)),
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Exists,
                    Breadth(MethodDefinitionGeneratedBodyRoles.All)),
            ]);
        MethodDefinitionSourceRequestSetPlan roles =
            AcceptedPlan(
            [
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Count,
                    Breadth(MethodDefinitionGeneratedBodyRoles.Execution)),
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Exists,
                    Breadth(MethodDefinitionGeneratedBodyRoles.All)),
            ]);
        MethodDefinitionSourceRequestSetPlan candidates =
            AcceptedPlan(
            [
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Count,
                    Breadth(MethodDefinitionGeneratedBodyRoles.All)),
                Association(
                    CountingProducer.Instance,
                    ProducerTerminal.Exists,
                    Breadth(
                        MethodDefinitionGeneratedBodyRoles.All,
                        candidates: true)),
            ]);

        // Only all-definition requests share a physical source group, and
        // all-definition breadth carries no expansion. Requests that expand,
        // even with equal roles, therefore never share one, so their roles
        // and candidates cannot widen each other's breadth.
        Assert.Equal(2, equal.Groups.Length);
        Assert.Equal(2, roles.Groups.Length);
        Assert.Equal(2, candidates.Groups.Length);
    }
}
