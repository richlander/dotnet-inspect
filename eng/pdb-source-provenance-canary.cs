#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:project ../src/Inspector.Artifacts/Inspector.Artifacts.csproj
#:property EnablePreviewFeatures=true
#:property NoWarn=CA2252

using System.Diagnostics;
using ILInspector.Metadata;
using Inspector.Artifacts;

if (args.Length != 2)
{
    throw new ArgumentException(
        "Usage: dotnet run eng/pdb-source-provenance-canary.cs -- "
        + "<assembly-path> <portable-pdb-path>");
}

byte[] image = File.ReadAllBytes(args[0]);
byte[] pdbImage = File.ReadAllBytes(args[1]);
ResolvedAssemblyReference assembly = CreateArtifactBoundAssembly(image);

long openStart = Stopwatch.GetTimestamp();
using PdbContext context = PdbContext.OpenMetadataOnly(assembly);
context.LoadPdbFromStream(new MemoryStream(pdbImage, writable: false));
TimeSpan open = Stopwatch.GetElapsedTime(openStart);

long inspectStart = Stopwatch.GetTimestamp();
PdbSourceProvenanceOutcome outcome = context.InspectSourceProvenance();
TimeSpan inspect = Stopwatch.GetElapsedTime(inspectStart);
var available = outcome as PdbSourceProvenanceOutcome.Available
    ?? throw new InvalidOperationException(outcome.ToString());
PdbSourceProvenanceResult result = available.Result;

Console.WriteLine($"open-ms={open.TotalMilliseconds:F3}");
Console.WriteLine($"inspect-ms={inspect.TotalMilliseconds:F3}");
Console.WriteLine($"documents={result.Receipt.DocumentCount}");
Console.WriteLine($"types={result.Receipt.TypeCount}");
Console.WriteLine($"associations={result.Receipt.AssociationCount}");
Console.WriteLine($"marker-rows={result.Receipt.MarkerRowCount}");
Console.WriteLine(
    $"checksum-bytes={result.Receipt.ChecksumBytesExamined}");
Console.WriteLine(
    $"ordinary-evidence-only="
    + result.Receipt.OrdinaryEvidenceOnlyCount);
Console.WriteLine(
    $"generated-evidence-only="
    + result.Receipt.GeneratedEvidenceOnlyCount);
Console.WriteLine($"mixed-evidence={result.Receipt.MixedEvidenceCount}");
Console.WriteLine($"unknown={result.Receipt.UnknownCount}");

foreach (var group in result.Documents
    .Where(static document => document.GeneratedPath is not null)
    .GroupBy(static document =>
        (Assembly: document.GeneratedPath!.GeneratorAssembly.ToString(),
         Type: document.GeneratedPath.GeneratorType.ToString()))
    .OrderBy(static group => group.Key.Assembly)
    .ThenBy(static group => group.Key.Type))
{
    Console.WriteLine(
        $"generator={group.Key.Assembly}|{group.Key.Type}|{group.Count()}");
}

foreach (var group in result.Types
    .SelectMany(static type => type.DirectMarkers)
    .Where(static marker =>
        marker is
        {
            Kind: PdbGenerationMarkerKind.GeneratedCode,
            Disposition: PdbGenerationMarkerDisposition.Valid,
        })
    .GroupBy(static marker =>
        (Tool: marker.DeclaredTool?.ToString(),
         Version: marker.DeclaredVersion?.ToString()))
    .OrderBy(static group => group.Key.Tool)
    .ThenBy(static group => group.Key.Version))
{
    Console.WriteLine(
        $"generated-code={group.Key.Tool}|{group.Key.Version}|{group.Count()}");
}

static ResolvedAssemblyReference CreateArtifactBoundAssembly(byte[] image)
{
    var authority = new ArtifactGenerationAuthority();
    ArtifactAdmissionAuthorization admission =
        authority.CreateAdmissionAuthorization();
    ArtifactContribution contribution;
    using (ArtifactContributionScope scope =
           authority.BeginContribution(admission))
    {
        contribution = scope.Register(
            CanaryArtifactProvenance.Instance,
            _ => new MemoryStream(image, writable: false));
    }
    authority.CreateRetainedContent(
        contribution.Registration,
        _ => new MemoryStream(image, writable: false));
    authority.CompleteAdmission(admission);
    return ResolvedAssemblyReference.CreateFromArtifactIfManaged(
            contribution.Registration,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("canary"))
        ?? throw new InvalidOperationException(
            "The canary assembly does not contain supported managed metadata.");
}

sealed class CanaryArtifactProvenance : IArtifactProvenance
{
    public static CanaryArtifactProvenance Instance { get; } = new();
}
