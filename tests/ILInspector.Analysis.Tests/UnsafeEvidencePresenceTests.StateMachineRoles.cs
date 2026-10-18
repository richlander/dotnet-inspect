using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class UnsafeEvidencePresenceTests
{
    const string IteratorFixtures =
        "ILInspector.Analysis.IteratorFixtures";

    [Fact]
    public void
        MethodQuerySource_GeneratedExpansionDefaultsToExecutionRole()
    {
        MethodDefinitionGeneratedExpansionCoverage expansion =
            ExpandIteratorSample(
                "GeneratedIteratorFinallyUnsafeSample",
                MethodDefinitionGeneratedBodyRoles.Execution,
                out _);

        Assert.All(
            expansion.Origins,
            origin => Assert.Equal(
                MethodDefinitionGeneratedBodyRoles.Execution,
                origin.Role));
        Assert.Equal(
            ["MoveNext"],
            OriginNames(expansion));
    }

    [Fact]
    public void
        MethodQuerySource_GeneratedExpansionIncludesRequestedStateMachineRoles()
    {
        MethodDefinitionGeneratedExpansionCoverage helpers =
            ExpandIteratorSample(
                "GeneratedIteratorFinallyUnsafeSample",
                MethodDefinitionGeneratedBodyRoles.UserHelper,
                out MethodDefinitionHandle iterator);
        MethodDefinitionGeneratedExpansionCoverage all =
            ExpandIteratorSample(
                "GeneratedIteratorFinallyUnsafeSample",
                MethodDefinitionGeneratedBodyRoles.All,
                out _);

        Assert.Equal(
            ["<>m__Finally1", "MoveNext"],
            OriginNames(helpers));
        MethodDefinitionGeneratedExpansionOrigin finallyHelper =
            Assert.Single(
                helpers.Origins,
                origin => origin.Role
                    == MethodDefinitionGeneratedBodyRoles.UserHelper);
        Assert.Equal(iterator, finallyHelper.DeclaredOwner);
        Assert.Equal(
            MethodDefinitionGeneratedExpansionOriginKind
                .StateMachineExecutionBody,
            finallyHelper.Kind);

        ImmutableArray<string> scaffolding =
            OriginNames(
                all,
                MethodDefinitionGeneratedBodyRoles.Scaffolding);
        Assert.Contains(".ctor", scaffolding);
        Assert.Contains(
            scaffolding,
            name => name.EndsWith(".Dispose", StringComparison.Ordinal));
        Assert.Contains(
            scaffolding,
            name => name.EndsWith(".GetEnumerator", StringComparison.Ordinal));
        Assert.DoesNotContain("<>m__Finally1", scaffolding);
        Assert.DoesNotContain("MoveNext", scaffolding);
        Assert.All(
            all.Origins,
            origin => Assert.Equal(iterator, origin.DeclaredOwner));
    }

    [Fact]
    public void
        MethodQuerySource_GeneratedExpansionOwnsIteratorLiftedBodies()
    {
        MethodDefinitionGeneratedExpansionCoverage expansion =
            ExpandIteratorSample(
                "GeneratedIteratorLiftedUnsafeSample",
                MethodDefinitionGeneratedBodyRoles.Execution,
                out MethodDefinitionHandle iterator);

        ImmutableArray<MethodDefinitionGeneratedExpansionOrigin> lifted =
        [
            .. expansion.Origins.Where(static origin =>
                origin.Kind
                    == MethodDefinitionGeneratedExpansionOriginKind
                        .LiftedExecutionBody),
        ];

        // The static lambda, the capturing lambda, and the local function.
        Assert.Equal(3, lifted.Length);
        Assert.All(
            expansion.Origins,
            origin => Assert.Equal(iterator, origin.DeclaredOwner));
    }

    [Fact]
    public void
        MethodQuerySource_GeneratedExpansionRolesSeparateRequestSetBreadth()
    {
        MethodDefinitionSourceBreadth execution =
            MethodDefinitionSourceBreadth
                .ExactMethods(MetadataTokensMethod(1))
                .IncludeGeneratedExecutionBodies();
        MethodDefinitionSourceBreadth helpers =
            MethodDefinitionSourceBreadth
                .ExactMethods(MetadataTokensMethod(1))
                .IncludeGeneratedExecutionBodies(
                    roles: MethodDefinitionGeneratedBodyRoles.UserHelper);

        Assert.Equal(
            MethodDefinitionGeneratedBodyRoles.Execution,
            execution.GeneratedRoles);
        Assert.Equal(
            MethodDefinitionGeneratedBodyRoles.Execution
                | MethodDefinitionGeneratedBodyRoles.UserHelper,
            helpers.GeneratedRoles);
        Assert.Equal(
            MethodDefinitionGeneratedBodyRoles.None,
            MethodDefinitionSourceBreadth.AllDefinitions.GeneratedRoles);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MethodDefinitionSourceBreadth
                .ExactMethods(MetadataTokensMethod(1))
                .IncludeGeneratedExecutionBodies(
                    roles: (MethodDefinitionGeneratedBodyRoles)8));
    }

    static MethodDefinitionHandle MetadataTokensMethod(int row) =>
        System.Reflection.Metadata.Ecma335.MetadataTokens
            .MethodDefinitionHandle(row);

    static MethodDefinitionGeneratedExpansionCoverage ExpandIteratorSample(
        string typeName,
        MethodDefinitionGeneratedBodyRoles roles,
        out MethodDefinitionHandle iterator,
        bool includeUnauthenticatedCandidates = false) =>
        ExpandIteratorSample(
            typeName,
            roles,
            out iterator,
            out _,
            includeUnauthenticatedCandidates);

    static MethodDefinitionGeneratedExpansionCoverage ExpandIteratorSample(
        string typeName,
        MethodDefinitionGeneratedBodyRoles roles,
        out MethodDefinitionHandle iterator,
        out MethodDefinitionHandleCoverage methodsSelected,
        bool includeUnauthenticatedCandidates = false)
    {
        string path =
            FixtureCatalog.AnalysisIterators.AssemblyPath();
        TypeDefinitionHandle type =
            FindFixtureType(path, IteratorFixtures, typeName);
        iterator = Assert.Single(
            MethodsOfType(path, type),
            method => !MethodName(path, method).StartsWith('<')
                && !MethodName(path, method).StartsWith('.'));
        AssemblyAnalysisOperation<int> operation = CreateOperation(
            path,
            MethodDefinitionSourceBreadth
                .ExactTypes(type)
                .IncludeGeneratedExecutionBodies(
                    roles: roles,
                    includeUnauthenticatedCandidates:
                        includeUnauthenticatedCandidates),
            CompleteUnsafeEvidenceDescription());

        using PdbContext context = PdbContext.OpenMetadataOnly(path);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Borrow(context);
        AssemblyAnalysisExecution<int> execution =
            Execute(session, operation);

        Assert.Null(execution.SourceReceipt.SourceFailure);
        MethodDefinitionGeneratedExpansionCoverage expansion =
            execution.SourceReceipt.Coverage.GeneratedExpansion;
        Assert.All(
            expansion.Origins,
            origin => Assert.True(
                execution.SourceReceipt.Coverage.MethodsSelected
                    .Contains(origin.Method)));
        methodsSelected = execution.SourceReceipt.Coverage.MethodsSelected;
        return expansion;
    }

    static ImmutableArray<string> OriginNames(
        MethodDefinitionGeneratedExpansionCoverage expansion,
        MethodDefinitionGeneratedBodyRoles? role = null)
    {
        string path =
            FixtureCatalog.AnalysisIterators.AssemblyPath();
        return
        [
            .. expansion.Origins
                .Where(origin => role is null || origin.Role == role)
                .Select(origin => MethodName(path, origin.Method))
                .Order(StringComparer.Ordinal),
        ];
    }

    static string MethodName(string path, MethodDefinitionHandle method)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        return reader.GetString(reader.GetMethodDefinition(method).Name);
    }
}
