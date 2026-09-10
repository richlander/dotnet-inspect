using System.Security.Cryptography;
using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Models;
using DotnetInspector.Packages;
using InertText;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Durable identity for one package-index projection over one admitted package payload.
/// The generation binds lookup and publication to the retained in-process snapshot; only
/// the authority, coordinate, digest, and projection identity cross process boundaries.
/// </summary>
internal sealed record PackageIndexCacheSubject(
    string AuthorityKey,
    string PackageId,
    string Version,
    string DigestAlgorithm,
    string DigestHexValue,
    PackageContentGenerationIdentity Generation)
{
    internal static PackageIndexCacheSubject? TryCreate(
        PackageExtractionResult resolution,
        Action<long> chargeWork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(chargeWork);

        ConfiguredPackageAuthority? authority = resolution.Authority;
        AcquiredPackageSourcePayload? payload = resolution.AcquiredPayload;
        if (authority?.PersistentCacheKey is not { } authorityKey
            || payload is null)
            return null;

        PackageContentDigest? digest = payload.GetContentDigest(
            chargeWork,
            cancellationToken);
        if (digest is null
            || !ReferenceEquals(
                digest.Generation,
                payload.Content.GenerationIdentity))
        {
            return null;
        }

        return new PackageIndexCacheSubject(
            authorityKey,
            payload.Coordinate.PackageId,
            payload.Coordinate.Version,
            digest.Algorithm,
            digest.HexValue,
            digest.Generation);
    }
}

/// <summary>
/// Result of producing the stable package-index projection for one frozen subject.
/// Only the complete form is accepted by <see cref="PackageIndexCache.Set"/>.
/// </summary>
internal abstract record PackageIndexProduction
{
    private protected PackageIndexProduction(InspectionResult result)
    {
        Result = result;
    }

    internal InspectionResult Result { get; }

    internal sealed record Complete : PackageIndexProduction
    {
        internal Complete(
            PackageIndexCacheSubject subject,
            InspectionResult result)
            : base(result)
        {
            Subject = subject;
        }

        internal PackageIndexCacheSubject Subject { get; }
    }

    internal sealed record Incomplete : PackageIndexProduction
    {
        internal Incomplete(
            InspectionResult result,
            string reason)
            : base(result)
        {
            Reason = reason;
        }

        internal string Reason { get; }
    }

    internal static PackageIndexProduction Create(
        PackageIndexCacheSubject subject,
        PackageContentGenerationIdentity producedGeneration,
        InspectionResult result,
        bool isComplete,
        string? incompleteReason = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(producedGeneration);
        ArgumentNullException.ThrowIfNull(result);

        if (!ReferenceEquals(subject.Generation, producedGeneration))
        {
            return new Incomplete(
                result,
                "Package content changed between lookup and publication.");
        }

        if (!isComplete)
        {
            return new Incomplete(
                result,
                incompleteReason ?? "Package inspection did not complete.");
        }

        if (!PackageIndexCache.IsValidProjection(
                subject,
                result,
                out string? reason))
        {
            return new Incomplete(result, reason!);
        }

        return new Complete(subject, result);
    }
}

/// <summary>
/// Persists the closed, stable package-index projection. Request-current metadata,
/// availability, external symbols, and display overlays are deliberately excluded.
/// </summary>
internal static class PackageIndexCache
{
    internal const string Category = "pkg-index-v17";
    internal const string Projection = "package-index-projection-v17";
    private const int FormatVersion = 17;
    private const int CompletionMarker = unchecked((int)0x434F4D50);
    private const int EndMarker = unchecked((int)0x454E4421);
    private const int MaxCollectionCount = 1_000_000;
    private static readonly byte[] Magic = "PKGIDX17"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static PackageIndexCache()
    {
        CoreCache.RegisterVersionedCategory("pkg-index-v", Category);
    }

    internal static InspectionResult? TryGet(PackageIndexCacheSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        byte[]? bytes = CoreCache.TryGetBytes(
            Category,
            CacheKey(subject),
            extension: "bin");
        if (bytes is null)
            return null;

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8, leaveOpen: true);
            RequireBytes(reader, Magic);
            if (reader.ReadInt32() != FormatVersion
                || ReadString(reader) != Projection
                || ReadString(reader) != subject.AuthorityKey
                || ReadString(reader) != subject.PackageId
                || ReadString(reader) != subject.Version
                || ReadString(reader) != subject.DigestAlgorithm
                || ReadString(reader) != subject.DigestHexValue
                || reader.ReadInt32() != CompletionMarker)
            {
                return null;
            }

            var result = new InspectionResult
            {
                PackageName = ReadString(reader),
                ManifestVersion = ReadNullableString(reader),
                Version = ReadString(reader),
                Description = ReadDescription(reader),
                Authors = ReadNullableString(reader),
                License = ReadNullableString(reader),
                LicenseUrl = ReadNullableString(reader),
                Repository = ReadNullableString(reader),
                RepositoryType = ReadNullableString(reader),
                RepositoryCommit = ReadNullableString(reader),
                ReadmeFile = ReadNullableString(reader),
                PackageReadmeFile = ReadNullableString(reader),
                HasReadme = ReadBoolean(reader),
                HasAgentDocumentation = ReadBoolean(reader),
                IsToolPackage = ReadBoolean(reader),
                PackageTypes = ReadStringList(reader),
                ContentDirectories = ReadStringList(reader),
                TargetFrameworks = ReadStringList(reader),
                SupportedRids = ReadStringList(reader),
                AssemblyCount = reader.ReadInt32(),
                BinarySignals = ReadBinarySignals(reader),
                IsFrameworkDependent = ReadBoolean(reader),
                HasRidSpecificAssets = ReadBoolean(reader),
                HasNativeDependencies = ReadBoolean(reader),
                ToolFormat = ReadNullableString(reader),
                IsRidSpecificPointerPackage = ReadBoolean(reader),
                ToolCommands = ReadStringList(reader),
                RuntimeIdentifierPackages = ReadRidPackages(reader),
                RuntimeTargetRid = ReadNullableString(reader),
                NativeFiles = ReadStringList(reader),
                LibraryFiles = ReadStringList(reader),
                DependencyGroups = ReadDependencyGroups(reader),
                RuntimeDependencies = ReadDependencies(reader),
            };

            if (reader.ReadInt32() != EndMarker
                || stream.Position != stream.Length
                || !IsValidProjection(subject, result, out _))
            {
                return null;
            }

            return result;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    internal static void Set(PackageIndexProduction.Complete production)
    {
        ArgumentNullException.ThrowIfNull(production);
        PackageIndexCacheSubject subject = production.Subject;
        InspectionResult result = production.Result;
        if (!IsValidProjection(subject, result, out string? reason))
        {
            throw new InvalidOperationException(
                reason ?? "The package-index publication subject changed.");
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(Projection);
            writer.Write(subject.AuthorityKey);
            writer.Write(subject.PackageId);
            writer.Write(subject.Version);
            writer.Write(subject.DigestAlgorithm);
            writer.Write(subject.DigestHexValue);
            writer.Write(CompletionMarker);

            writer.Write(result.PackageName);
            WriteNullableString(writer, result.ManifestVersion);
            writer.Write(result.Version);
            WriteDescription(writer, result.Description);
            WriteNullableString(writer, result.Authors);
            WriteNullableString(writer, result.License);
            WriteNullableString(writer, result.LicenseUrl);
            WriteNullableString(writer, result.Repository);
            WriteNullableString(writer, result.RepositoryType);
            WriteNullableString(writer, result.RepositoryCommit);
            WriteNullableString(writer, result.ReadmeFile);
            WriteNullableString(writer, result.PackageReadmeFile);
            WriteBoolean(writer, result.HasReadme);
            WriteBoolean(writer, result.HasAgentDocumentation);
            WriteBoolean(writer, result.IsToolPackage);
            WriteStringList(writer, result.PackageTypes);
            WriteStringList(writer, result.ContentDirectories);
            WriteStringList(writer, result.TargetFrameworks);
            WriteStringList(writer, result.SupportedRids);
            writer.Write(result.AssemblyCount);
            WriteBinarySignals(writer, result.BinarySignals);
            WriteBoolean(writer, result.IsFrameworkDependent);
            WriteBoolean(writer, result.HasRidSpecificAssets);
            WriteBoolean(writer, result.HasNativeDependencies);
            WriteNullableString(writer, result.ToolFormat);
            WriteBoolean(writer, result.IsRidSpecificPointerPackage);
            WriteStringList(writer, result.ToolCommands);
            WriteRidPackages(writer, result.RuntimeIdentifierPackages);
            WriteNullableString(writer, result.RuntimeTargetRid);
            WriteStringList(writer, result.NativeFiles);
            WriteStringList(writer, result.LibraryFiles);
            WriteDependencyGroups(writer, result.DependencyGroups);
            WriteDependencies(writer, result.RuntimeDependencies);
            writer.Write(EndMarker);
        }

        CoreCache.SetBytes(
            Category,
            CacheKey(subject),
            stream.ToArray(),
            extension: "bin");
    }

    internal static bool RequiresRidReverification(InspectionResult result)
        => result.RuntimeIdentifierPackages?.Any(
            package => package.Exists is null) == true;

    internal static string CacheKey(PackageIndexCacheSubject subject)
    {
        string identity = string.Join(
            '\n',
            Projection,
            subject.AuthorityKey,
            subject.PackageId,
            subject.Version,
            subject.DigestAlgorithm,
            subject.DigestHexValue);
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    internal static bool IsValidProjection(
        PackageIndexCacheSubject subject,
        InspectionResult result,
        out string? reason)
    {
        if (!PackageExtractor.IsValidPackageId(result.PackageName)
            || !string.Equals(
                result.PackageName,
                subject.PackageId,
                StringComparison.OrdinalIgnoreCase)
            || !PackageExtractor.TryNormalizePackageVersion(
                result.Version,
                out string normalizedVersion)
            || !string.Equals(
                normalizedVersion,
                subject.Version,
                StringComparison.OrdinalIgnoreCase))
        {
            reason = "The inspection result does not describe the frozen package coordinate.";
            return false;
        }

        if (result.Description is { IsTruncated: true })
        {
            reason = "A truncated package description cannot be persisted without its provenance.";
            return false;
        }

        if (result.AssemblyCount < 0
            || !IsValidBinarySignals(result.BinarySignals)
            || !IsOrdered(result.ContentDirectories, StringComparer.Ordinal)
            || !IsOrdered(result.TargetFrameworks, StringComparer.Ordinal)
            || !IsOrdered(result.SupportedRids, StringComparer.Ordinal)
            || !IsOrdered(result.NativeFiles, StringComparer.Ordinal)
            || !IsOrdered(result.LibraryFiles, StringComparer.Ordinal)
            || !IsOrdered(
                result.RuntimeIdentifierPackages,
                RidPackageComparer.Instance)
            || !IsOrdered(result.DependencyGroups, DependencyGroupComparer.Instance)
            || !AreDependenciesOrdered(result.DependencyGroups)
            || !IsOrdered(
                result.RuntimeDependencies,
                PackageDependencyComparer.Instance))
        {
            reason = "The package-index projection is invalid or noncanonical.";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool IsValidBinarySignals(PackageBinarySignals? value)
    {
        if (value is null)
            return true;

        return value.TotalBinaries >= 0
            && value.EmbeddedPdbs >= 0
            && value.InPackagePdbs >= 0
            && value.EmbeddedSourceLinkPdbs >= 0
            && value.InPackageSourceLinkPdbs >= 0
            && value.SymbolsAvailable
                == value.EmbeddedPdbs + value.InPackagePdbs
            && value.SourceLinkAvailable
                == value.EmbeddedSourceLinkPdbs
                    + value.InPackageSourceLinkPdbs
            && value.SnupkgPdbs == 0
            && value.MsdlPdbs == 0
            && value.OtherPdbs == 0
            && value.SnupkgSourceLinkPdbs == 0
            && value.MsdlSourceLinkPdbs == 0
            && value.OtherSourceLinkPdbs == 0
            && value.EmbeddedPdbs + value.InPackagePdbs
                <= value.TotalBinaries
            && value.EmbeddedSourceLinkPdbs <= value.EmbeddedPdbs
            && value.InPackageSourceLinkPdbs <= value.InPackagePdbs;
    }

    private static bool AreDependenciesOrdered(
        List<DependencyGroup>? groups)
        => groups?.All(group => IsOrdered(
            group.Dependencies,
            PackageDependencyComparer.Instance)) != false;

    private static bool IsOrdered<T>(
        IReadOnlyList<T>? values,
        IComparer<T> comparer)
    {
        if (values is null)
            return true;

        for (int i = 1; i < values.Count; i++)
        {
            if (comparer.Compare(values[i - 1], values[i]) > 0)
                return false;
        }

        return true;
    }

    private static void WriteDescription(
        BinaryWriter writer,
        InertString? description)
    {
        if (description is not { } value)
        {
            writer.Write((byte)0);
            return;
        }

        writer.Write((byte)1);
        writer.Write(value.ToString());
    }

    private static InertString? ReadDescription(BinaryReader reader)
    {
        return ReadDiscriminator(reader) switch
        {
            0 => null,
            1 => InertString.FromEncoded(TextPolicy.Prose, ReadString(reader)),
            _ => throw new InvalidDataException("Invalid description discriminator."),
        };
    }

    private static void WriteNullableString(BinaryWriter writer, string? value)
    {
        writer.Write(value is null ? (byte)0 : (byte)1);
        if (value is not null)
            writer.Write(value);
    }

    private static string? ReadNullableString(BinaryReader reader)
        => ReadDiscriminator(reader) switch
        {
            0 => null,
            1 => ReadString(reader),
            _ => throw new InvalidDataException("Invalid string discriminator."),
        };

    private static void WriteBoolean(BinaryWriter writer, bool value)
        => writer.Write(value ? (byte)1 : (byte)0);

    private static bool ReadBoolean(BinaryReader reader)
        => ReadDiscriminator(reader) switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("Invalid boolean value."),
        };

    private static byte ReadDiscriminator(BinaryReader reader)
        => reader.ReadByte();

    private static void WriteStringList(
        BinaryWriter writer,
        IReadOnlyList<string>? values)
    {
        WriteCount(writer, values?.Count);
        if (values is null)
            return;

        foreach (string value in values)
            writer.Write(value);
    }

    private static List<string>? ReadStringList(BinaryReader reader)
    {
        int? count = ReadCount(reader);
        if (count is null)
            return null;

        var values = new List<string>(count.Value);
        for (int i = 0; i < count; i++)
            values.Add(ReadString(reader));
        return values;
    }

    private static void WriteBinarySignals(
        BinaryWriter writer,
        PackageBinarySignals? value)
    {
        writer.Write(value is null ? (byte)0 : (byte)1);
        if (value is null)
            return;

        writer.Write(value.TotalBinaries);
        writer.Write(value.EmbeddedPdbs);
        writer.Write(value.InPackagePdbs);
        writer.Write(value.EmbeddedSourceLinkPdbs);
        writer.Write(value.InPackageSourceLinkPdbs);
    }

    private static PackageBinarySignals? ReadBinarySignals(BinaryReader reader)
    {
        switch (ReadDiscriminator(reader))
        {
            case 0:
                return null;
            case 1:
                break;
            default:
                throw new InvalidDataException(
                    "Invalid binary-signal discriminator.");
        }

        int total = reader.ReadInt32();
        int embeddedPdbs = reader.ReadInt32();
        int inPackagePdbs = reader.ReadInt32();
        int embeddedSourceLinkPdbs = reader.ReadInt32();
        int inPackageSourceLinkPdbs = reader.ReadInt32();
        return new PackageBinarySignals
        {
            TotalBinaries = total,
            SymbolsAvailable = embeddedPdbs + inPackagePdbs,
            SourceLinkAvailable =
                embeddedSourceLinkPdbs + inPackageSourceLinkPdbs,
            EmbeddedPdbs = embeddedPdbs,
            InPackagePdbs = inPackagePdbs,
            EmbeddedSourceLinkPdbs = embeddedSourceLinkPdbs,
            InPackageSourceLinkPdbs = inPackageSourceLinkPdbs,
        };
    }

    private static void WriteRidPackages(
        BinaryWriter writer,
        IReadOnlyList<RidPackageReference>? values)
    {
        WriteCount(writer, values?.Count);
        if (values is null)
            return;

        foreach (RidPackageReference value in values)
        {
            writer.Write(value.RuntimeIdentifier);
            writer.Write(value.PackageId);
        }
    }

    private static List<RidPackageReference>? ReadRidPackages(
        BinaryReader reader)
    {
        int? count = ReadCount(reader);
        if (count is null)
            return null;

        var values = new List<RidPackageReference>(count.Value);
        for (int i = 0; i < count; i++)
        {
            values.Add(new RidPackageReference
            {
                RuntimeIdentifier = ReadString(reader),
                PackageId = ReadString(reader),
                Exists = null,
            });
        }

        return values;
    }

    private static void WriteDependencyGroups(
        BinaryWriter writer,
        IReadOnlyList<DependencyGroup>? values)
    {
        WriteCount(writer, values?.Count);
        if (values is null)
            return;

        foreach (DependencyGroup group in values)
        {
            writer.Write(group.TargetFramework);
            WriteBoolean(writer, group.IsImplicitManifestGroup);
            WriteDependencies(writer, group.Dependencies);
        }
    }

    private static List<DependencyGroup>? ReadDependencyGroups(
        BinaryReader reader)
    {
        int? count = ReadCount(reader);
        if (count is null)
            return null;

        var values = new List<DependencyGroup>(count.Value);
        for (int i = 0; i < count; i++)
        {
            values.Add(new DependencyGroup
            {
                TargetFramework = ReadString(reader),
                IsImplicitManifestGroup = ReadBoolean(reader),
                Dependencies = ReadDependencies(reader) ?? [],
            });
        }

        return values;
    }

    private static void WriteDependencies(
        BinaryWriter writer,
        IReadOnlyList<PackageDependency>? values)
    {
        WriteCount(writer, values?.Count);
        if (values is null)
            return;

        foreach (PackageDependency value in values)
        {
            writer.Write(value.Id);
            writer.Write(value.Version);
        }
    }

    private static List<PackageDependency>? ReadDependencies(
        BinaryReader reader)
    {
        int? count = ReadCount(reader);
        if (count is null)
            return null;

        var values = new List<PackageDependency>(count.Value);
        for (int i = 0; i < count; i++)
        {
            values.Add(new PackageDependency
            {
                Id = ReadString(reader),
                Version = ReadString(reader),
            });
        }

        return values;
    }

    private static void WriteCount(BinaryWriter writer, int? count)
        => writer.Write(count ?? -1);

    private static int? ReadCount(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count == -1)
            return null;
        if (count < 0 || count > MaxCollectionCount)
            throw new InvalidDataException("Invalid collection count.");
        return count;
    }

    private static string ReadString(BinaryReader reader)
    {
        int byteCount = Read7BitEncodedInt(reader);
        long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
        if (byteCount < 0 || byteCount > remaining)
            throw new InvalidDataException("Invalid cache string length.");

        byte[] bytes = reader.ReadBytes(byteCount);
        if (bytes.Length != byteCount)
            throw new EndOfStreamException();
        return StrictUtf8.GetString(bytes);
    }

    private static int Read7BitEncodedInt(BinaryReader reader)
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            byte current = reader.ReadByte();
            if (shift == 28 && (current & 0xF0) != 0)
                throw new InvalidDataException("Invalid cache string length.");

            value |= (uint)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
                return checked((int)value);
        }

        throw new InvalidDataException("Invalid cache string length.");
    }

    private static void RequireBytes(BinaryReader reader, byte[] expected)
    {
        byte[] actual = reader.ReadBytes(expected.Length);
        Require(actual.AsSpan().SequenceEqual(expected));
    }

    private static void Require(bool condition)
    {
        if (!condition)
            throw new InvalidDataException("Invalid package-index cache entry.");
    }

    private sealed class RidPackageComparer :
        IComparer<RidPackageReference>
    {
        internal static RidPackageComparer Instance { get; } = new();

        public int Compare(RidPackageReference? x, RidPackageReference? y)
        {
            int rid = StringComparer.Ordinal.Compare(
                x?.RuntimeIdentifier,
                y?.RuntimeIdentifier);
            return rid != 0
                ? rid
                : StringComparer.Ordinal.Compare(x?.PackageId, y?.PackageId);
        }
    }

    private sealed class DependencyGroupComparer :
        IComparer<DependencyGroup>
    {
        internal static DependencyGroupComparer Instance { get; } = new();

        public int Compare(DependencyGroup? x, DependencyGroup? y)
            => StringComparer.Ordinal.Compare(
                x?.TargetFramework,
                y?.TargetFramework);
    }

    private sealed class PackageDependencyComparer :
        IComparer<PackageDependency>
    {
        internal static PackageDependencyComparer Instance { get; } = new();

        public int Compare(PackageDependency? x, PackageDependency? y)
        {
            int id = StringComparer.Ordinal.Compare(x?.Id, y?.Id);
            return id != 0
                ? id
                : StringComparer.Ordinal.Compare(x?.Version, y?.Version);
        }
    }
}
