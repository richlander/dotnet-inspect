using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.Planning;

namespace ILInspector.Analysis.Tests;

public partial class UnsafeEvidencePresenceTests
{
    [Fact]
    public void
        MethodQuerySource_GeneratedExpansionAttributesStateMachineMethodsOutsideRoles()
    {
        MethodDefinitionGeneratedExpansionCoverage expansion =
            ExpandIteratorSample(
                "GeneratedIteratorFinallyUnsafeSample",
                MethodDefinitionGeneratedBodyRoles.Execution,
                out MethodDefinitionHandle iterator,
                out MethodDefinitionHandleCoverage selected);

        Assert.Equal(["MoveNext"], OriginNames(expansion));
        Assert.Contains(
            expansion.AttributedOutsideRoles,
            origin => origin.Role
                    == MethodDefinitionGeneratedBodyRoles.UserHelper
                && IteratorMethodName(origin.Method) == "<>m__Finally1");
        Assert.Contains(
            expansion.AttributedOutsideRoles,
            origin => origin.Role
                    == MethodDefinitionGeneratedBodyRoles.Scaffolding
                && IteratorMethodName(origin.Method) == ".ctor");
        Assert.All(
            expansion.AttributedOutsideRoles,
            origin =>
            {
                Assert.Equal(iterator, origin.DeclaredOwner);
                Assert.NotEqual(
                    MethodDefinitionGeneratedBodyRoles.Execution,
                    origin.Role);
                // Attributed but outside the requested roles, so the
                // Execution-only request never visits the finally helper.
                Assert.False(selected.Contains(origin.Method));
            });
        Assert.Empty(expansion.UnauthenticatedCandidates);
    }

    [Fact]
    public void
        MethodQuerySource_GeneratedExpansionClassifiesEveryReachedNestedMethod()
    {
        foreach (MethodDefinitionGeneratedBodyRoles roles in
            new[]
            {
                MethodDefinitionGeneratedBodyRoles.Execution,
                MethodDefinitionGeneratedBodyRoles.All,
            })
        {
            MethodDefinitionGeneratedExpansionCoverage expansion =
                ExpandIteratorSample(
                    "GeneratedIteratorLiftedUnsafeSample",
                    roles,
                    out _,
                    out _);

            ImmutableArray<MethodDefinitionHandle> reached =
                ReachedNestedMethods(
                    "GeneratedIteratorLiftedUnsafeSample",
                    out TypeDefinitionHandle root);
            // The static local function is declared on the selected Type
            // itself, so it is an origin the nested walk does not reach.
            ImmutableArray<MethodDefinitionHandle> classified =
            [
                .. expansion.Origins
                    .Select(static origin => origin.Method)
                    .Concat(expansion.AttributedOutsideRoles.Select(
                        static origin => origin.Method))
                    .Concat(expansion.UnauthenticatedCandidates)
                    .Where(method => DeclaringType(method) != root),
            ];

            // Every method of every reached compiler-generated nested Type
            // has exactly one outcome.
            Assert.Equal(
                reached.Order(MethodRowComparer.Instance),
                classified.Order(MethodRowComparer.Instance));
            // The static-lambda cache and the closure have no owner edge for
            // their constructors; nothing else is unauthenticated.
            Assert.NotEmpty(expansion.UnauthenticatedCandidates);
            Assert.All(
                expansion.UnauthenticatedCandidates,
                method => Assert.StartsWith(
                    ".",
                    IteratorMethodName(method)));
        }
    }

    [Fact]
    public void
        MethodQuerySource_GeneratedExpansionAddsRequestedUnauthenticatedCandidates()
    {
        MethodDefinitionGeneratedExpansionCoverage excluded =
            ExpandIteratorSample(
                "GeneratedIteratorLiftedUnsafeSample",
                MethodDefinitionGeneratedBodyRoles.All,
                out _,
                out MethodDefinitionHandleCoverage withoutCandidates);
        MethodDefinitionGeneratedExpansionCoverage included =
            ExpandIteratorSample(
                "GeneratedIteratorLiftedUnsafeSample",
                MethodDefinitionGeneratedBodyRoles.All,
                out _,
                out MethodDefinitionHandleCoverage withCandidates,
                includeUnauthenticatedCandidates: true);

        Assert.Equal(
            excluded.UnauthenticatedCandidates,
            included.UnauthenticatedCandidates);
        Assert.Equal(excluded.Origins, included.Origins);
        Assert.All(
            included.UnauthenticatedCandidates,
            method =>
            {
                Assert.False(withoutCandidates.Contains(method));
                Assert.True(withCandidates.Contains(method));
            });
        Assert.Equal(
            withoutCandidates.Count
                + included.UnauthenticatedCandidates.Length,
            withCandidates.Count);
    }

    static ImmutableArray<MethodDefinitionHandle> ReachedNestedMethods(
        string typeName,
        out TypeDefinitionHandle root)
    {
        string path =
            FixtureCatalog.AnalysisIterators.AssemblyPath();
        root = FindFixtureType(path, IteratorFixtures, typeName);
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        var methods = ImmutableArray.CreateBuilder<MethodDefinitionHandle>();
        var parents = new Stack<TypeDefinitionHandle>();
        parents.Push(root);
        while (parents.Count > 0)
        {
            foreach (TypeDefinitionHandle nested
                in reader.GetTypeDefinition(parents.Pop()).GetNestedTypes())
            {
                TypeDefinition type = reader.GetTypeDefinition(nested);
                if (!reader.GetString(type.Name).StartsWith('<'))
                    continue;
                parents.Push(nested);
                methods.AddRange(type.GetMethods());
            }
        }
        return methods.ToImmutable();
    }

    static TypeDefinitionHandle DeclaringType(MethodDefinitionHandle method)
    {
        using FileStream stream =
            File.OpenRead(FixtureCatalog.AnalysisIterators.AssemblyPath());
        using var peReader = new PEReader(stream);
        return peReader.GetMetadataReader()
            .GetMethodDefinition(method)
            .GetDeclaringType();
    }

    static string IteratorMethodName(MethodDefinitionHandle method) =>
        MethodName(
            FixtureCatalog.AnalysisIterators.AssemblyPath(),
            method);

    sealed class MethodRowComparer : IComparer<MethodDefinitionHandle>
    {
        internal static MethodRowComparer Instance { get; } = new();

        public int Compare(
            MethodDefinitionHandle x,
            MethodDefinitionHandle y) =>
            MetadataTokens.GetRowNumber(x)
                .CompareTo(MetadataTokens.GetRowNumber(y));
    }
}
