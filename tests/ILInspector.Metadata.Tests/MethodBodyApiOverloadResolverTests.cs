using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace ILInspector.Metadata.Tests;

public sealed class MethodBodyApiOverloadResolverTests
{
    [Fact]
    public void ExtensionProbeDistinguishesSelectedTypeDeclarations()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(SelectedExtensionContainer).Assembly.Location);
        int selectedTypeToken = Assert.IsType<int>(
            session.MethodBodies.FindUniqueLookupTypeToken(
                typeof(SelectedExtensionContainer).FullName!));

        Assert.False(
            session.MethodBodies.DeclaresExtensionMethodOutsideType(
                nameof(SelectedExtensionContainer.SelectedOnly),
                selectedTypeToken));
        Assert.True(
            session.MethodBodies.DeclaresExtensionMethodOutsideType(
                nameof(SelectedExtensionContainer.Shared),
                selectedTypeToken));
    }

    public static IEnumerable<object[]> BclAssemblies()
    {
        yield return [typeof(string).Assembly.Location];
        yield return [typeof(Enumerable).Assembly.Location];
        yield return [typeof(JsonSerializer).Assembly.Location];
    }

    [Theory]
    [MemberData(nameof(BclAssemblies))]
    [Trait("Speed", "Slow")]
    public void ResolverMatchesRichSelectionForEveryOrdinaryOverload(
        string assemblyPath)
    {
        foreach (bool includeAll in new[] { false, true })
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            ApiSurface surface =
                ApiSurfaceExtractor.Extract(peReader, includeAll);
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(assemblyPath);
            int comparedGroups = 0;

            foreach (ApiType type in surface.Types)
            {
                if (type.DefinitionName is null)
                    continue;

                foreach (IGrouping<string, ApiMember> group
                    in type.Members
                        .Where(static member =>
                            member.Kind == "method"
                            && member.MetadataToken.HasValue)
                        .GroupBy(
                            static member => member.Name,
                            StringComparer.OrdinalIgnoreCase)
                        .Where(static group => group.Count() > 1))
                {
                    string memberName = group.First().Name;
                    IReadOnlyList<MemberTargetCandidate> candidates =
                        MemberTargetResolver.GetCandidates(
                            type,
                            new(
                                memberName,
                                memberName,
                                OverloadIndex: null,
                                DigestPrefix: null,
                                GenericArity: null));
                    bool directSelectionIsSupported =
                        candidates.Count > 1
                        && !session.MethodBodies
                            .DeclaresExtensionMethod(memberName)
                        && candidates.All(static candidate =>
                            candidate.Member.Kind == "method"
                            && candidate.Member.MetadataToken.HasValue);

                    for (int ordinal = 1;
                        ordinal <= candidates.Count;
                        ordinal++)
                    {
                        MethodBodySelection? direct =
                            session.MethodBodies
                                .ResolveApiMethodOverload(
                                    type.DefinitionName
                                        .ToMetadataFullName(),
                                    memberName,
                                    ordinal - 1,
                                    includeAll);
                        if (!directSelectionIsSupported)
                        {
                            Assert.Null(direct);
                            continue;
                        }

                        MemberTargetResolution rich =
                            MemberTargetResolver.Resolve(
                                type,
                                new(
                                    memberName,
                                    memberName,
                                    ordinal,
                                    DigestPrefix: null,
                                    GenericArity: null));
                        Assert.NotNull(direct);
                        Assert.NotNull(rich.Target?.Body);
                        Assert.True(
                            rich.Target.Body.MetadataToken
                                == direct.MetadataToken,
                            $"{type.DefinitionName.ToMetadataFullName()}."
                                + $"{memberName}:{ordinal} selected "
                                + $"0x{direct.MetadataToken:X8}, expected "
                                + $"0x{rich.Target.Body.MetadataToken:X8}. "
                                + string.Join(
                                    " | ",
                                    candidates.Select(candidate =>
                                        $"0x{candidate.Member.MetadataToken:X8} "
                                            + candidate.Member.Signature)));
                    }

                    comparedGroups++;
                }
            }

            Assert.True(
                comparedGroups > 0,
                $"No overloaded ordinary methods were compared in "
                    + $"{Path.GetFileName(assemblyPath)}.");
        }
    }
}

public sealed class ExtensionReceiver;

public static class SelectedExtensionContainer
{
    public static void SelectedOnly(this ExtensionReceiver receiver)
    {
    }

    public static void Shared(this ExtensionReceiver receiver)
    {
    }
}

public static class OtherExtensionContainer
{
    public static void Shared(this ExtensionReceiver receiver)
    {
    }
}
