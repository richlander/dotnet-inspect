using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Inspectors;

internal static class LibraryApiDiffRunner
{
    internal static ApiSurfaceProjectionLimits Limits { get; } =
        new(1, 1_000_000, 1_000_000, 1_000, 1_000_000, 10_000_000);

    internal static async Task<
        InspectionEnvelope<LibraryApiDiffOutcome>> ExecuteAsync(
            AssemblySetEntry before,
            AssemblySetEntry after,
            bool includeAll)
    {
        AssemblyContextParticipant beforeParticipant = CreateParticipant(before);
        AssemblyContextParticipant afterParticipant = CreateParticipant(after);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([beforeParticipant]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([afterParticipant]);

        return LibraryApiDiffInspection.Execute(
            beforeGroup,
            beforeParticipant,
            afterGroup,
            afterParticipant,
            includeAll ? ApiSurfaceScope.IncludeAll : ApiSurfaceScope.Public,
            Limits);
    }

    static AssemblyContextParticipant CreateParticipant(AssemblySetEntry entry)
    {
        ResolvedAssemblyReference assembly =
            AssemblySetInspectionWorkspace.TryCreateManagedAssembly(
                entry,
                out CandidateOpenFailure? failure)
            ?? throw new InvalidOperationException(
                $"API comparison not compared: '{entry.Path}' ({failure!.Kind}): {failure.Detail}");
        return new(
            assembly,
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(entry.Path)
                {
                    PreferImplementationAssemblies = true,
                    AllowPlatformAssemblyVersionRollForward = true,
                }));
    }
}
