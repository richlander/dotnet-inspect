using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Authenticated generated-method owners for one declared Type. An omitted
/// method has no unique authenticated owner. Coverage retains the existing
/// expansion's completion and work evidence; a bounded expansion issues no
/// owner map, so its unsettled remainder cannot select a guessed Member.
/// </summary>
public sealed record TypeGeneratedMethodOwnership(
    ImmutableDictionary<MethodDefinitionHandle, MethodDefinitionHandle> Owners,
    MethodDefinitionGeneratedExpansionCoverage Coverage)
{
    /// <summary>
    /// Resolves one Type's generated methods through the same targeted
    /// lifted-owner and state-machine expansion as Method Query Source.
    /// TypeGeneratedMethodOwnershipTests gates this facade's owner agreement.
    /// </summary>
    public static TypeGeneratedMethodOwnership Resolve(
        PEReader image,
        TypeDefinitionHandle type,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        MetadataReader reader = MetadataFormatAdmission.GetMetadataReader(image, MetadataReaderOptions.None);
        TypeDefinition definition = reader.GetTypeDefinition(type);
        ImmutableArray<MethodDefinitionHandle> roots = [.. definition.GetMethods()
            .Where(method => !CompilerGeneratedNames.IsLocalFunctionOrLambda(
                reader.GetString(reader.GetMethodDefinition(method).Name)))];
        var work = new MethodDefinitionGeneratedExpansionWork(
            MethodDefinitionGeneratedExpansionLimits.AllDefinitions);
        var attribution = new DeclaredSourceAttribution(
            reader.GetString(reader.GetModuleDefinition().Name),
            reader, image, generatedExpansionWork: work);
        MethodDefinitionGeneratedExpansionResult expansion =
            attribution.ExpandGeneratedExecutionBodies(
                roots,
                MethodDefinitionGeneratedBodyRoles.All,
                [type]);
        cancellationToken.ThrowIfCancellationRequested();
        var owners = ImmutableDictionary.CreateBuilder<MethodDefinitionHandle, MethodDefinitionHandle>();
        if (!expansion.Coverage.IsBounded)
        {
            foreach (var group in expansion.Coverage.Origins.GroupBy(origin => origin.Method))
            {
                MethodDefinitionHandle[] candidates = [.. group.Select(origin => origin.DeclaredOwner).Distinct()];
                if (candidates.Length == 1
                    && reader.GetMethodDefinition(candidates[0]).GetDeclaringType() == type)
                {
                    owners.Add(group.Key, candidates[0]);
                }
            }
        }
        return new(owners.ToImmutable(), expansion.Coverage);
    }
}
