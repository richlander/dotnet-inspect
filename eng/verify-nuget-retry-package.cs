using System.IO.Compression;
using System.Security.Cryptography;

const string SignatureEntry = ".signature.p7s";

try
{
    if (args is ["--self-test"])
    {
        RunSelfTest();
        return;
    }

    if (args.Length == 0 || args.Length % 2 != 0)
    {
        throw new InvalidOperationException(
            "Usage: verify-nuget-retry-package.cs " +
            "<retained.nupkg> <published.nupkg> [...]");
    }

    for (int index = 0; index < args.Length; index += 2)
        VerifyRetryIdentity(args[index], args[index + 1]);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}

static void VerifyRetryIdentity(string retainedPath, string publishedPath)
{
    PackageContents retained = ReadPackage(
        retainedPath,
        requireRepositorySignature: false);
    PackageContents published = ReadPackage(
        publishedPath,
        requireRepositorySignature: true);

    if (retained.Entries.Count != published.Entries.Count)
    {
        throw new InvalidOperationException(
            $"{Path.GetFileName(publishedPath)} has a different payload entry count.");
    }

    foreach ((string name, EntryIdentity expected) in retained.Entries)
    {
        if (!published.Entries.TryGetValue(name, out EntryIdentity actual))
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(publishedPath)} is missing payload entry '{name}'.");
        }

        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(publishedPath)} payload entry '{name}' differs.");
        }
    }

    Console.WriteLine(
        $"Verified repository-signed retry identity for " +
        $"{Path.GetFileName(retainedPath)}.");
}

static PackageContents ReadPackage(
    string path,
    bool requireRepositorySignature)
{
    using ZipArchive archive = ZipFile.OpenRead(path);
    Dictionary<string, EntryIdentity> entries =
        new(StringComparer.Ordinal);
    int signatureCount = 0;

    foreach (ZipArchiveEntry entry in archive.Entries)
    {
        if (entry.FullName.Equals(
                SignatureEntry,
                StringComparison.OrdinalIgnoreCase))
        {
            signatureCount++;
            continue;
        }

        using Stream stream = entry.Open();
        string digest = Convert.ToHexString(SHA256.HashData(stream));
        if (!entries.TryAdd(entry.FullName, new(entry.Length, digest)))
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(path)} contains duplicate entry " +
                $"'{entry.FullName}'.");
        }
    }

    if (requireRepositorySignature)
    {
        if (signatureCount != 1)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(path)} must contain exactly one " +
                $"{SignatureEntry} entry.");
        }
    }
    else if (signatureCount != 0)
    {
        throw new InvalidOperationException(
            $"{Path.GetFileName(path)} retained candidate package is already signed.");
    }

    return new(entries);
}

static void RunSelfTest()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"verify-nuget-retry-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);

    try
    {
        string retained = Path.Combine(directory, "retained.nupkg");
        string signedEquivalent = Path.Combine(directory, "signed-equivalent.nupkg");
        string changedPayload = Path.Combine(directory, "changed-payload.nupkg");
        string missingPayload = Path.Combine(directory, "missing-payload.nupkg");
        string unsignedPublished = Path.Combine(directory, "unsigned-published.nupkg");
        string signedRetained = Path.Combine(directory, "signed-retained.nupkg");

        CreatePackage(retained, signed: false, changed: false, omitTool: false);
        CreatePackage(
            signedEquivalent,
            signed: true,
            changed: false,
            omitTool: false);
        CreatePackage(changedPayload, signed: true, changed: true, omitTool: false);
        CreatePackage(missingPayload, signed: true, changed: false, omitTool: true);
        CreatePackage(
            unsignedPublished,
            signed: false,
            changed: false,
            omitTool: false);
        CreatePackage(signedRetained, signed: true, changed: false, omitTool: false);

        VerifyRetryIdentity(retained, signedEquivalent);
        ExpectFailure(
            () => VerifyRetryIdentity(retained, changedPayload),
            "differs");
        ExpectFailure(
            () => VerifyRetryIdentity(retained, missingPayload),
            "entry count");
        ExpectFailure(
            () => VerifyRetryIdentity(retained, unsignedPublished),
            SignatureEntry);
        ExpectFailure(
            () => VerifyRetryIdentity(signedRetained, signedEquivalent),
            "already signed");

        Console.WriteLine("NuGet retry package verifier self-test passed.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void CreatePackage(
    string path,
    bool signed,
    bool changed,
    bool omitTool)
{
    using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
    WriteEntry(
        archive,
        "dotnet-inspect.nuspec",
        changed ? "changed package" : "candidate package",
        CompressionLevel.NoCompression);
    if (!omitTool)
    {
        WriteEntry(
            archive,
            "tools/net10.0/any/dotnet-inspect.dll",
            "tool payload",
            CompressionLevel.SmallestSize);
    }

    if (signed)
        WriteEntry(archive, SignatureEntry, "repository signature", CompressionLevel.Fastest);
}

static void WriteEntry(
    ZipArchive archive,
    string name,
    string content,
    CompressionLevel compression)
{
    ZipArchiveEntry entry = archive.CreateEntry(name, compression);
    entry.LastWriteTime = new DateTimeOffset(2025, 1, 2, 3, 4, 6, TimeSpan.Zero);
    using StreamWriter writer = new(entry.Open());
    writer.Write(content);
}

static void ExpectFailure(Action action, string messageFragment)
{
    try
    {
        action();
    }
    catch (Exception ex) when (
        ex.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    throw new InvalidOperationException(
        $"Expected failure containing '{messageFragment}'.");
}

internal sealed record PackageContents(
    IReadOnlyDictionary<string, EntryIdentity> Entries);

internal readonly record struct EntryIdentity(long Length, string Sha256);
