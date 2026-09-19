using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Net;
using DotnetInspect.Cli.CommandLine;
using CSharpText.MemberSlicing;
using DotnetInspect.Cli.Inspectors;
using ILInspector.Metadata;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using Markout;
using Markout.Formatting;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;

using Decompiler = ILInspector.Decompiler;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Shared helpers for type and member commands.
/// </summary>
public partial class ApiCommand
{

    // ===== Method Source Resolution =====

    /// <param name="Source">Resolved source, or null when none could be resolved.</param>
    /// <param name="PdbPath">The acquired portable PDB path, when one was acquired.</param>
    /// <param name="MemberHasNoBody">
    /// True when the member carries no IL body, so <paramref name="Source"/> is absent because
    /// there is nothing to show rather than because resolution failed (issue #3299).
    /// </param>
    /// <param name="MemberHasNoPdbDeclaration">
    /// True when the member has a body but its PDB source range does not identify one declaration
    /// to isolate.
    /// </param>
    /// <param name="MemberSourceTooComplex">
    /// True when verified source exceeded the bounded lexical-complexity limit.
    /// </param>
    /// <param name="MemberSourceCoordinatesInvalid">
    /// True when portable-PDB sequence-point coordinates cannot address the verified source.
    /// </param>
    /// <param name="PdbSourceUnavailableReason">
    /// Visible explanation when PDB source acquisition failed for another reason.
    /// </param>
    internal sealed record ResolvedMethodSource(
        MethodSourceContext? Source,
        string? PdbPath,
        bool MemberHasNoBody = false,
        bool MemberHasNoPdbDeclaration = false,
        bool MemberSourceTooComplex = false,
        bool MemberSourceCoordinatesInvalid = false,
        string? PdbSourceUnavailableReason = null);

    internal static async Task<ResolvedMethodSource> ResolveMethodSourceAsync(
        string dllPath, string typeName, string methodName, int overloadIndex,
        ApiOptions options, HttpClient httpClient, VerboseLogger logger, bool fetchSource = true,
        bool publicOnly = true, int sourceMetadataToken = 0,
        string? memberMetadataAssemblyPath = null, int memberMetadataToken = 0,
        ResolvedAssemblyReference? sourceAssembly = null,
        string? fallbackPackageName = null,
        string? fallbackPackageVersion = null)
    {
        try
        {
            // A member with no IL body has no PDB source to resolve, whatever the PDB and
            // SourceLink situation is. The selected MethodDef token belongs to the assembly that
            // supplied the API member, which may differ from the runtime facade opened for PDB
            // lookup. Preserve that identity instead of applying the token to the wrong image;
            // only when no selected MethodDef identity is available, use the same name/overload
            // fallback as source lookup
            // (issue #3299).
            bool? memberHasBody = ResolveMemberBodyState(
                dllPath,
                typeName,
                methodName,
                overloadIndex,
                publicOnly,
                memberMetadataAssemblyPath,
                memberMetadataToken,
                logger.Log);
            if (memberHasBody == false)
            {
                return new ResolvedMethodSource(
                    null,
                    null,
                    MemberHasNoBody: true);
            }

            using var service = SourceLinkService.Open(dllPath, logger.Log);
            var context = service.Context;

            // Acquire PDB if needed (same flow as SourceEnricher)
            if (context.NeedsPdb)
            {
                var (pkgName, pkgVersion) = !string.IsNullOrEmpty(options.PackagePath)
                    ? PackageExtractor.ParsePackageReference(options.PackagePath)
                    : (null, null);
                pkgName = fallbackPackageName ?? pkgName;
                pkgVersion = fallbackPackageVersion ?? pkgVersion;

                if (sourceAssembly is null)
                {
                    await SourceEnricher.AcquirePdbAsync(
                        context,
                        httpClient,
                        pkgName,
                        pkgVersion,
                        isPlatformAssembly:
                            !string.IsNullOrEmpty(
                                options.PlatformAssembly),
                        logger.Log,
                        sourceOptions: options.SourceOptions);
                }
                else
                {
                    await SourceEnricher.AcquirePdbAsync(
                        context,
                        sourceAssembly,
                        httpClient,
                        logger.Log,
                        sourceOptions: options.SourceOptions,
                        fallbackPackageName: pkgName,
                        fallbackPackageVersion: pkgVersion);
                }
            }

            // Capture the acquired portable PDB path now so the decompiler can reuse it for local
            // names even when SourceLink/source resolution below fails (PDB available, source not).
            string? pdbPath = context.PortablePdbPath;

            if (!fetchSource)
                return new ResolvedMethodSource(null, pdbPath);
            if (!service.HasPdb)
            {
                return new ResolvedMethodSource(
                    null,
                    pdbPath,
                    PdbSourceUnavailableReason: NoPortablePdbReason);
            }

            var methodInfo = service.ResolveMethodSource(
                typeName,
                methodName,
                overloadIndex,
                publicOnly,
                sourceMetadataToken);
            if (methodInfo == null)
            {
                return new ResolvedMethodSource(
                    null,
                    pdbPath,
                    PdbSourceUnavailableReason: NoPdbSourceMappingReason);
            }

            // Honor the source the portable PDB records when it is present locally: a non-reproducible
            // (local dev) build keeps a real local path whose exact compiled bytes may exist only here,
            // so the remote SourceLink URL would 404 or differ. The checksum authenticates the on-disk
            // bytes against the portable PDB; remote SourceLink is the fallback for reproducible builds.
            string? content = null;
            SourceChecksumVerification checksumVerification =
                SourceChecksumVerification.Unavailable;
            var localBytes = DotnetInspector.Services.PdbSourceHouse.TryReadVerifiedLocalSource(
                methodInfo.FilePath, methodInfo.ChecksumAlgorithm, methodInfo.Checksum);
            byte[]? repoBytes;
            if (localBytes != null)
            {
                checksumVerification = SourceLinkService.VerifyChecksum(
                    methodInfo.ChecksumAlgorithm,
                    methodInfo.Checksum,
                    localBytes);
                content = NormalizePdbSourceLineEndings(
                    SourceLinkService.DecodeSourceText(localBytes));
            }
            // Opt-in (--repo): read the committed blob at the SourceLink commit from a local clone,
            // authenticated by the same PDB checksum, before touching the network. Useful for a
            // reproducible build whose sources are private or simply already cloned on this machine.
            else if (options.SourceRepositories.Length > 0
                && (repoBytes = DotnetInspector.Services.LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
                    methodInfo.SourceUrl, methodInfo.ChecksumAlgorithm, methodInfo.Checksum,
                    options.SourceRepositories)) != null)
            {
                checksumVerification = SourceLinkService.VerifyChecksum(
                    methodInfo.ChecksumAlgorithm,
                    methodInfo.Checksum,
                    repoBytes);
                content = NormalizePdbSourceLineEndings(
                    SourceLinkService.DecodeSourceText(repoBytes));
            }
            else if (methodInfo.SourceUrl != null)
            {
                var fetcher = new SourceFetch(DotnetInspector.Networking.HttpClientFactory.SharedUntrustedFetch);
                var fetch = await PdbSourceHouse.FetchVerifiedSourceTextAsync(
                    fetcher,
                    methodInfo.SourceUrl,
                    methodInfo.ChecksumAlgorithm,
                    methodInfo.Checksum);
                content = fetch.Text is null
                    ? null
                    : NormalizePdbSourceLineEndings(fetch.Text);
                checksumVerification = fetch.ChecksumVerification;
                if (fetch.Failure is not null)
                    logger.LogWarning(fetch.Failure);
            }

            if (content == null)
            {
                return new ResolvedMethodSource(
                    null,
                    pdbPath,
                    PdbSourceUnavailableReason: NoMatchingPdbSourceReason);
            }

            return SliceResolvedMethodSource(
                content,
                methodInfo.StartLine,
                methodInfo.EndLine,
                methodName,
                methodInfo.SourceUrl ?? methodInfo.FilePath,
                pdbPath,
                methodInfo.SequencePointStartLines,
                methodInfo.ChecksumAlgorithm,
                methodInfo.Checksum,
                checksumVerification);
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Failed to resolve method source for {typeName}.{methodName}: {ex.Message}");
            return new ResolvedMethodSource(
                null,
                null,
                PdbSourceUnavailableReason: PdbSourceInspectionFailedReason);
        }
    }

    internal static bool? ResolveMemberBodyState(
        string dllPath,
        string typeName,
        string methodName,
        int overloadIndex,
        bool publicOnly,
        string? memberMetadataAssemblyPath,
        int memberMetadataToken,
        Action<string>? log)
    {
        bool hasMemberToken =
            memberMetadataToken != 0
            && memberMetadataAssemblyPath is { Length: > 0 };
        bool tokenAddressesLookupImage =
            hasMemberToken
            && LibraryMetadataService
                .ReferenceTreePathComparer(OperatingSystem.IsWindows())
                .Equals(
                    Path.GetFullPath(dllPath),
                    Path.GetFullPath(memberMetadataAssemblyPath!));

        if (hasMemberToken)
        {
            using var memberContext = PdbContext.OpenMetadataOnly(
                tokenAddressesLookupImage
                    ? dllPath
                    : memberMetadataAssemblyPath!,
                tokenAddressesLookupImage ? log : null);
            return memberContext.MethodHasBody(memberMetadataToken);
        }

        using var lookupContext = PdbContext.OpenMetadataOnly(dllPath, log);
        return lookupContext.MethodHasBody(
            typeName,
            methodName,
            overloadIndex,
            publicOnly);
    }

    internal static string NormalizePdbSourceLineEndings(string content)
        // Normalize only CR/LF forms. Other characters recognized by string.ReplaceLineEndings,
        // including form feed, are not C# physical line breaks and must not shift PDB coordinates.
        => content.Replace("\r\n", "\n").Replace('\r', '\n');

    internal static ResolvedMethodSource SliceResolvedMethodSource(
        string content,
        int startLine,
        int endLine,
        string methodName,
        string sourceLocation,
        string? pdbPath,
        IReadOnlyList<int>? visibleSequencePointStartLines = null,
        string? checksumAlgorithm = null,
        byte[]? checksum = null,
        SourceChecksumVerification checksumVerification =
            SourceChecksumVerification.Unavailable)
    {
        try
        {
            string? sourceCode = MemberTextSlicer.ExtractMemberText(
                content,
                startLine,
                endLine,
                methodName,
                visibleSequencePointStartLines);

            // The PDB range does not identify one declaration: report no source rather than
            // a type header, initializer, or structurally unknown span.
            return sourceCode is null
                ? new ResolvedMethodSource(
                    null,
                    pdbPath,
                    MemberHasNoPdbDeclaration: true)
                : new ResolvedMethodSource(
                    new MethodSourceContext(
                        sourceCode,
                        sourceLocation,
                        checksumAlgorithm,
                        checksum is null ? null : Convert.ToHexString(checksum),
                        checksumVerification),
                    pdbPath);
        }
        catch (CSharpTextComplexityException)
        {
            return new ResolvedMethodSource(
                null,
                pdbPath,
                MemberSourceTooComplex: true);
        }
        catch (InvalidMemberTextCoordinatesException)
        {
            return new ResolvedMethodSource(
                null,
                pdbPath,
                MemberSourceCoordinatesInvalid: true);
        }
    }

}
