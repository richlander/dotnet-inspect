using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public sealed record MemberSourceAttachmentRequest
{
    public MemberSourceAttachmentRequest(
        PrinterOptions? printerOptions = null,
        bool includeAuthoredParts = false,
        bool allowDecompiledFallback = true)
    {
        PrinterOptions = printerOptions;
        IncludeAuthoredParts = includeAuthoredParts;
        AllowDecompiledFallback = allowDecompiledFallback;
    }

    public PrinterOptions? PrinterOptions { get; }
    public bool IncludeAuthoredParts { get; }
    public bool AllowDecompiledFallback { get; }
}

public sealed record MemberSourceAttachment(
    MemberSubject Subject,
    AssemblyMemberSourceEntry Outcome);

public delegate ValueTask<AssemblyMemberSourceEntry>
    MemberSourceAttachmentProvider(
        AssemblyMemberSourceRequest request,
        CancellationToken cancellationToken);

internal enum MemberSourceAttachmentFailure
{
    SubjectMismatch,
}

internal abstract record MemberSourceAttachmentResult
{
    private MemberSourceAttachmentResult()
    {
    }

    internal sealed record Attached(
        MemberSourceAttachment Attachment)
        : MemberSourceAttachmentResult;

    internal sealed record Failed(
        MemberSourceAttachmentFailure Reason)
        : MemberSourceAttachmentResult;
}

internal static class MemberSourceAttachmentOperation
{
    internal static async ValueTask<MemberSourceAttachmentResult>
        ExecuteAsync(
            MemberSubject subject,
            MemberSourceAttachmentRequest request,
            MemberSourceAttachmentProvider provider,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provider);

        AssemblyMemberSourceRequest sourceRequest =
            new(
                subject.Group.DeclaringType,
                subject.Anchor,
                subject.MetadataToken,
                request.PrinterOptions);
        sourceRequest = request.IncludeAuthoredParts
            ? sourceRequest.WithAuthoredParts(
                request.AllowDecompiledFallback)
            : request.AllowDecompiledFallback
                ? sourceRequest
                : sourceRequest.WithoutDecompiledFallback();

        AssemblyMemberSourceEntry outcome =
            await provider(sourceRequest, cancellationToken)
                .ConfigureAwait(false);
        if (outcome.Request != sourceRequest
            || !outcome.Subject.Identity.IsEquivalentTo(
                AssemblyIdentity(subject.Population.Assembly)))
        {
            return new MemberSourceAttachmentResult.Failed(
                MemberSourceAttachmentFailure.SubjectMismatch);
        }

        return new MemberSourceAttachmentResult.Attached(
            new(subject, outcome));
    }

    private static AssemblyReferenceIdentity AssemblyIdentity(
        LibraryAssemblyIdentity identity) =>
        new(
            identity.Name.ToString(),
            identity.Version,
            identity.Culture?.ToString(),
            identity.PublicKeyToken?.ToString());
}
