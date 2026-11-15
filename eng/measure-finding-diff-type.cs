#:project ../src/DotnetInspector.Queries/DotnetInspector.Queries.csproj

// NativeAOT selected-Type operation over retained images, including pairing,
// authenticated generated-owner discovery, census, anchors and API settlement.
// Usage: measure-finding-diff-type before.dll after.dll full-type-name [samples]
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using DotnetInspector.Queries;
using ILInspector.Metadata;

if (args.Length is < 3 or > 4)
    throw new ArgumentException("Expected before.dll after.dll full-type-name [samples].");
int count = args.Length == 4 ? int.Parse(args[3]) : 20;
ArgumentOutOfRangeException.ThrowIfLessThan(count, 2);
using var before = new PEReader(File.OpenRead(args[0]));
using var after = new PEReader(File.OpenRead(args[1]));
TypeDefinitionHandle a = Select(before), b = Select(after);
foreach (bool stepped in new[] { false, true })
{
    List<double> times = [], allocations = [];
    string? fingerprint = null;
    int members = 0, changed = 0;
    for (int i = -1; i < count; i++)
    {
        var measurement = Measure(stepped);
        double elapsed = measurement.Elapsed;
        long bytes = measurement.Bytes;
        string current = measurement.Digest;
        if (fingerprint is not null && current != fingerprint)
            throw new InvalidOperationException("Result changed between measurements.");
        fingerprint = current;
        members = measurement.Members;
        changed = measurement.Changed;
        if (i >= 0) { times.Add(elapsed); allocations.Add(bytes); }
    }
    times.Sort();
    allocations.Sort();
    Console.WriteLine($"type={args[2]} stepped={stepped} samples={count} median-ms={times[count / 2]:F3} p95-ms={times[(int)Math.Ceiling(count * .95) - 1]:F3} bytes={allocations[count / 2]:F0} members={members} changed={changed} sha256={fingerprint}");
}
return;

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
(double Elapsed, long Bytes, string Digest, int Members, int Changed) Measure(bool stepped)
{
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    long start = Stopwatch.GetTimestamp();
    using var query = new FindingDiffAnyTypeQuery(before, a, after, b);
    while (!query.Step(stepped ? TimeSpan.Zero : TimeSpan.MaxValue)) { }
    double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    FindingDiffAnyTypeResult result = query.Result!;
    string content = string.Join('\n', result.Members.Select(member =>
        $"{member.BeforeFingerprint}/{member.AfterFingerprint}/{member.Api}/{member.Body}"))
        + $"\n{result.Api}/{result.Body}/{result.ResidualApi}/{result.ResidualBody}";
    string current = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    return (elapsed, bytes, current, result.Members.Length,
        result.Members.Count(member => member.Api == FindingDiffAnyState.Changed || member.Body == FindingDiffAnyState.Changed));
}

TypeDefinitionHandle Select(PEReader image)
{
    MetadataReader reader = image.GetMetadataReader();
    return reader.TypeDefinitions.Single(handle =>
        MetadataDeclarationQuery.GetTypeDefinitionName(reader, handle).ToMetadataFullName() == args[2]);
}
