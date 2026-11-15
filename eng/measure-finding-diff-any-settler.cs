#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:project ../src/DotnetInspector.Sections/DotnetInspector.Sections.csproj

// Measures Finding Diff Any's API axis over one Library image pair with the
// inline settlement and with the Annotated Type Map API shape family's settler
// (docs/design/annotated-type-map-api-family.md#gates, Cost), alternating the
// two per iteration, and checks that every Type's API state agrees.
// Usage: measure-finding-diff-any-settler <before.dll> <after.dll> [iterations]

using System.Diagnostics;
using System.Reflection.PortableExecutable;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: measure-finding-diff-any-settler <before.dll> <after.dll> [iterations]");
    return 2;
}

byte[] beforeBytes = File.ReadAllBytes(args[0]);
byte[] afterBytes = File.ReadAllBytes(args[1]);
int iterations = args.Length > 2 ? int.Parse(args[2]) : 20;

// Each side's Type focus is acquired through its own registration, as a host's.
var workspace = new InspectionWorkspace();
StructuralSubjectIdentity.WorkspaceSubject subject = StructuralSubjectIdentity.ForWorkspace(workspace.Identity);
Func<MetadataTypeDefinitionName, SubjectRelationFocusAuthority> Focus(string path, byte[] bytes)
{
    ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
        new AssemblyReferenceIdentity(Path.GetFileNameWithoutExtension(path), new Version(0, 0), Culture: null, PublicKeyToken: null),
        path,
        () => new MemoryStream(bytes, writable: false),
        AssemblyResolutionProvenance.Local("measure-finding-diff-any-settler"));
    return type => SubjectRelationFocusAuthority.ForAcquiredType(
        subject, assembly.Registration, assembly.Identity, assembly.Provenance, type);
}
var settler = new AnnotatedTypeMapApiShapeSettler(Focus(args[0], beforeBytes), Focus(args[1], afterBytes));

int settled = 0;
FindingDiffAnyResult Run(FindingDiffAnyApiSettler? apiSettler)
{
    using var before = new PEReader(new MemoryStream(beforeBytes, writable: false));
    using var after = new PEReader(new MemoryStream(afterBytes, writable: false));
    return FindingDiffAny.Compare(before, after, bodyReader: null, apiSettler, FindingDiffAnyAxes.Api);
}
FindingDiffAnyApiSettler counted = (before, beforeType, after, afterType, keys) =>
{
    settled++;
    return settler.Settle(before, beforeType, after, afterType, keys);
};

FindingDiffAnyResult inline = Run(null);
settled = 0;
FindingDiffAnyResult viaFamily = Run(counted);
bool agree = inline.Types.Select(type => (type.FullName, type.Api))
    .SequenceEqual(viaFamily.Types.Select(type => (type.FullName, type.Api)));
int settledTypes = settled;

var inlineSamples = new List<double>(iterations);
var familySamples = new List<double>(iterations);
for (int i = 0; i < iterations; i++)
{
    foreach (bool family in i % 2 == 0 ? (bool[])[false, true] : [true, false])
    {
        long start = Stopwatch.GetTimestamp();
        GC.KeepAlive(Run(family ? settler.Settle : null));
        (family ? familySamples : inlineSamples).Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
}

static double Median(List<double> samples)
{
    samples.Sort();
    return samples[samples.Count / 2];
}

Console.WriteLine($"api-inline\tmedian={Median(inlineSamples):F2}\tmin={inlineSamples[0]:F2}");
Console.WriteLine($"api-family\tmedian={Median(familySamples):F2}\tmin={familySamples[0]:F2}\tsettled={settledTypes}");
Console.WriteLine($"agreement\tagree={agree}\ttypes={inline.Types.Count()}");
return agree ? 0 : 1;
