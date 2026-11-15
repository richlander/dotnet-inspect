using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public abstract record AssemblyContextFindingDiffAnyTypeOutcome
{
    private AssemblyContextFindingDiffAnyTypeOutcome() { }
    public sealed record Compared(FindingDiffAnyTypeResult Result) : AssemblyContextFindingDiffAnyTypeOutcome;
    public sealed record Rejected(string Reason) : AssemblyContextFindingDiffAnyTypeOutcome;
}

/// <summary>
/// Compares one exact Type through the participants' sessions. Both sessions
/// own their images until every yielded Member step and terminal disposal has
/// completed; no reader or partially prepared comparison escapes this query.
/// </summary>
public static class AssemblyContextFindingDiffAnyTypeQuery
{
    public static async Task<AssemblyContextFindingDiffAnyTypeOutcome> ExecuteAsync(
        AssemblyContextGroup beforeGroup, AssemblyContextParticipant beforeParticipant,
        AssemblyContextGroup afterGroup, AssemblyContextParticipant afterParticipant,
        string typeIdentifier, TimeSpan budget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeGroup);
        ArgumentNullException.ThrowIfNull(beforeParticipant);
        ArgumentNullException.ThrowIfNull(afterGroup);
        ArgumentNullException.ThrowIfNull(afterParticipant);
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, TimeSpan.Zero);
        MetadataTypeDefinitionName type = MetadataTypeDefinitionName.ParseSerialized(typeIdentifier) switch
        {
            MetadataTypeDefinitionNameResult.Valid valid => valid.Name,
            _ => throw new ArgumentException("An exact escaped Type definition identity is required.", nameof(typeIdentifier))
        };
        cancellationToken.ThrowIfCancellationRequested();
        var before = await beforeGroup.UseAndReleaseAssemblySessionAsync(
            beforeParticipant.Assembly,
            async (beforeSession, _) =>
            {
                var after = await afterGroup.UseAndReleaseAssemblySessionAsync(
                    afterParticipant.Assembly,
                    async (afterSession, _) =>
                    {
                        try
                        {
                            // Session owners await this entire task before releasing
                            // the readers lent to the Type comparison.
                            return await beforeSession.InspectImage(beforeImage =>
                                afterSession.InspectImage(afterImage => CompareAsync(
                                    beforeImage, afterImage, type, budget, cancellationToken)));
                        }
                        catch (BadImageFormatException error)
                        {
                            return new AssemblyContextFindingDiffAnyTypeOutcome.Rejected(error.Message);
                        }
                    });
                return Unwrap(after);
            });
        return Unwrap(before);
    }

    static async Task<AssemblyContextFindingDiffAnyTypeOutcome> CompareAsync(
        PEReader before, PEReader after, MetadataTypeDefinitionName type,
        TimeSpan budget, CancellationToken cancellationToken)
    {
        TypeDefinitionHandle a = Select(before, type), b = Select(after, type);
        if (a.IsNil && b.IsNil)
            return new AssemblyContextFindingDiffAnyTypeOutcome.Rejected("The selected Type is absent from both endpoint images.");
        using var query = new FindingDiffAnyTypeQuery(before, a, after, b, cancellationToken);
        while (!query.Step(budget, cancellationToken))
            await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return new AssemblyContextFindingDiffAnyTypeOutcome.Compared(query.Result!);
    }

    static TypeDefinitionHandle Select(PEReader image, MetadataTypeDefinitionName type)
        => MetadataTypeDeclarationProbe.ProbeDefinition(image.GetMetadataReader(), type) switch
        {
            TypeDeclarationResult.Defined defined => MetadataTokens.TypeDefinitionHandle(defined.Definition.Value & 0xffffff),
            TypeDeclarationResult.DefinitionKindUnavailable defined => MetadataTokens.TypeDefinitionHandle(defined.Definition.Value & 0xffffff),
            TypeDeclarationResult.Missing => default,
            _ => throw new BadImageFormatException("The selected Type does not resolve uniquely in an endpoint image.")
        };

    static AssemblyContextFindingDiffAnyTypeOutcome Unwrap(AssemblyImageAccessResult<AssemblyContextFindingDiffAnyTypeOutcome> access)
        => access switch
        {
            AssemblyImageAccessResult<AssemblyContextFindingDiffAnyTypeOutcome>.Available available => available.Value,
            AssemblyImageAccessResult<AssemblyContextFindingDiffAnyTypeOutcome>.Rejected rejected =>
                new AssemblyContextFindingDiffAnyTypeOutcome.Rejected(rejected.Failure.ToString()),
            _ => throw new InvalidOperationException("Unknown assembly image access result.")
        };
}
