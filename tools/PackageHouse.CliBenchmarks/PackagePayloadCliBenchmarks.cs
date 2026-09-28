using System.IO.Compression;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.PackageHouseCliBenchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Method)]
public class PackagePayloadCliBenchmarks
{
    private const string PackageId = "avalonia";
    private const string PackageVersion = "12.1.2";
    private const string EntryPath = "lib/net10.0/Avalonia.Base.dll";
    private const int TransferBufferSize = 64 * 1024;
    private const int TransfersPerInvoke = 640;

    private PackageHouseSettlement.Acquired _acquired = null!;
    private IPackageContent _content = null!;
    private readonly CountingSinkStream _sink = new();
    private DirectoryInfo _temporaryDirectory = null!;
    private string _archivePath = null!;
    private long _expectedLength;
    private string _archiveHash = null!;
    private string _entryHash = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _archivePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "avalonia.12.1.2.nupkg");
        if (!File.Exists(_archivePath))
            throw new FileNotFoundException("The benchmark package is unavailable.", _archivePath);

        _temporaryDirectory =
            Directory.CreateTempSubdirectory("package-house-cli-benchmark-");
        string extractedRoot = Path.Combine(
            _temporaryDirectory.FullName,
            "extracted");
        Directory.CreateDirectory(extractedRoot);
        ExtractRequiredEntries(_archivePath, extractedRoot);

        var authorization = new UniformPackageSourceAuthorization(
            [PackageSource.NuGetOrg]);
        PackageSourceAuthorization sources =
            authorization.AuthorizeSourcesFor(PackageId);
        ConfiguredPackageAuthority authority =
            sources.Authorities.Single();
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateGallery(authority.Association);
        await using PackageSourceSettlementLease sourceRoot =
            PackageSourceSettlementService.IssueLease(_ => client);

        var house = new PackageHouse(
            authorization,
            new PackagePayloadAcquisitionPlan(
                (_, producer) =>
                {
                    var content = new FileSystemPackageContent(
                        extractedRoot,
                        _archivePath,
                        fromCache: true,
                        producerKey: producer.Key,
                        requiresArchiveTreeMatch: false);
                    return new SingleContentStore(content);
                }));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    PackageVersion)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire));
        PackageHouseSettlement settlement = await house.ExecuteAsync(
            request,
            sourceRoot.IssueOperationLease(
                CancellationToken.None,
                request.Operation.RequestTimeout,
                request.Operation.OperationTimeout));
        _acquired = settlement as PackageHouseSettlement.Acquired
            ?? throw new InvalidOperationException(
                $"PackageHouse returned {settlement.GetType().Name}.");
        _content = _acquired.Payload.Content;

        string extractedEntry = Path.Combine(
            extractedRoot,
            EntryPath.Replace('/', Path.DirectorySeparatorChar));
        _expectedLength = new FileInfo(extractedEntry).Length;
        _archiveHash = HashFile(_archivePath);

        byte[] eager = ReadEagerPayload();
        byte[] pulled = await ReadPulledPayloadAsync();
        if (!eager.AsSpan().SequenceEqual(pulled)
            || eager.LongLength != _expectedLength)
        {
            throw new InvalidOperationException(
                "The eager and pull paths did not produce the same exact entry.");
        }

        _entryHash = Convert.ToHexStringLower(SHA256.HashData(eager));
    }

    [Benchmark(
        Baseline = true,
        OperationsPerInvoke = TransfersPerInvoke)]
    public long EagerMaterializeThenWrite()
    {
        long written = 0;
        for (int index = 0; index < TransfersPerInvoke; index++)
        {
            byte[] payload = ReadEagerPayload();
            _sink.Reset();
            _sink.Write(payload);
            written += _sink.BytesWritten;
        }

        return written;
    }

    [Benchmark(OperationsPerInvoke = TransfersPerInvoke)]
    public long PullThenWrite()
    {
        long written = 0;
        for (int index = 0; index < TransfersPerInvoke; index++)
        {
            using PackageHousePayloadRead input =
                _acquired.OpenPayloadRead(
                    EntryPath,
                    _expectedLength);
            _sink.Reset();
            input.CopyTo(_sink, TransferBufferSize);
            written += _sink.BytesWritten;
        }

        return written;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _temporaryDirectory?.Delete(recursive: true);
    }

    internal static async Task<int> ValidateAsync(TextWriter output)
    {
        var benchmark = new PackagePayloadCliBenchmarks();
        try
        {
            await benchmark.SetupAsync();
            output.WriteLine(
                "package\tversion\tentry\texpanded_bytes\t"
                + "archive_sha256\tentry_sha256\ttransfer_buffer_bytes\t"
                + "transfers_per_invoke");
            output.WriteLine(
                $"{PackageId}\t{PackageVersion}\t{EntryPath}\t"
                + $"{benchmark._expectedLength}\t{benchmark._archiveHash}\t"
                + $"{benchmark._entryHash}\t{TransferBufferSize}\t"
                + $"{TransfersPerInvoke}");
            return 0;
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    private byte[] ReadEagerPayload()
    {
        if (!_content.TryOpenEntry(
                EntryPath,
                _expectedLength,
                out Stream? input))
        {
            throw new FileNotFoundException(
                "The benchmark package entry is unavailable.",
                EntryPath);
        }

        using (input)
        {
            byte[] payload =
                GC.AllocateUninitializedArray<byte>(
                    checked((int)_expectedLength));
            input.ReadExactly(payload);
            if (input.ReadByte() != -1)
            {
                throw new InvalidDataException(
                    "The eager package entry exceeded its declared length.");
            }

            return payload;
        }
    }

    private async Task<byte[]> ReadPulledPayloadAsync()
    {
        await using PackageHousePayloadRead input =
            _acquired.OpenPayloadRead(
                EntryPath,
                _expectedLength);
        using var output = new MemoryStream(
            checked((int)_expectedLength));
        await input.CopyToAsync(
            output,
            TransferBufferSize,
            CancellationToken.None);
        return output.ToArray();
    }

    private static void ExtractRequiredEntries(
        string archivePath,
        string extractedRoot)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        Extract(archive, EntryPath, extractedRoot);
        ZipArchiveEntry nuspec = archive.Entries.Single(entry =>
            !entry.FullName.Contains('/')
            && entry.FullName.EndsWith(
                ".nuspec",
                StringComparison.OrdinalIgnoreCase));
        Extract(archive, nuspec.FullName, extractedRoot);
    }

    private static void Extract(
        ZipArchive archive,
        string entryPath,
        string extractedRoot)
    {
        ZipArchiveEntry entry = archive.GetEntry(entryPath)
            ?? throw new InvalidDataException(
                $"The benchmark archive does not contain '{entryPath}'.");
        string destination = Path.Combine(
            extractedRoot,
            entryPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        entry.ExtractToFile(destination);
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private sealed class SingleContentStore(
        IPackageContent content) : IPackageStore
    {
        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null)
        {
            if (!packageName.Equals(
                    PackageId,
                    StringComparison.OrdinalIgnoreCase)
                || !version.Equals(
                    PackageVersion,
                    StringComparison.OrdinalIgnoreCase)
                || allowedSourceKeys?.Contains(
                    content.ProducerKey,
                    StringComparer.Ordinal) is not true)
            {
                return null;
            }

            log?.Invoke($"Using benchmark package: {packageName} {version}");
            return content;
        }

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "The benchmark package must be acquired from its prepared cache.");
    }

    private sealed class CountingSinkStream : Stream
    {
        internal long BytesWritten { get; private set; }

        internal void Reset() => BytesWritten = 0;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => BytesWritten;

        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            BytesWritten = checked(BytesWritten + count);
        }

        public override void Write(ReadOnlySpan<byte> buffer) =>
            BytesWritten = checked(BytesWritten + buffer.Length);
    }
}
