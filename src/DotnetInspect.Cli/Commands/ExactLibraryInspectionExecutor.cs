using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

using CSharpText;
using DotnetInspect.Cli.Output;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.DocumentationHouse.Direct;
using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal sealed class ExactLibraryInspectionSession(
    LibraryReference reference,
    LibraryContentOwner owner)
{
    private static readonly DocumentationHouseLimits
        s_documentationLimits =
            new(
                maximumCompiledXmlContributions: 1,
                maximumCompiledXmlBytes: 8 * 1024 * 1024,
                XmlDocumentationReadLimits.Default);

    public InspectionEnvelope<LibraryInspectionOutcome>? Execute(
        LibraryInspectionPlan plan,
        CancellationToken cancellationToken)
    {
        LibraryOperationLeaseIssueOutcome leaseIssue =
            owner.IssueOperationLease(reference);
        if (leaseIssue
            is not LibraryOperationLeaseIssueOutcome.Issued issued)
        {
            CommandError.Write(
                "The exact Library owner could not issue the inspection "
                    + "operation lease.");
            return null;
        }

        using LibraryOperationLease lease = issued.Lease;
        var request = new LibraryInspectionRequest(reference, plan);
        return LibraryInspectionOperation.Execute(
            request,
            lease,
            cancellationToken);
    }

    public InspectionEnvelope<MemberGroupDocumentInspectionOutcome>?
        ExecuteMemberGroupDocument(
            MemberOverloadPopulationInspectionPlan plan,
            CancellationToken cancellationToken)
    {
        LibraryOperationLeaseIssueOutcome leaseIssue =
            owner.IssueOperationLease(reference);
        if (leaseIssue
            is not LibraryOperationLeaseIssueOutcome.Issued issued)
        {
            CommandError.Write(
                "The exact Library owner could not issue the member-group "
                    + "inspection operation lease.");
            return null;
        }

        using LibraryOperationLease lease = issued.Lease;
        return MemberGroupDocumentInspectionOperation.Execute(
            new(reference, plan),
            lease,
            cancellationToken);
    }

    public InspectionEnvelope<MemberDocumentInspectionOutcome>?
        ExecuteMemberDocument(
            MemberDocumentInspectionPlan plan,
            CancellationToken cancellationToken)
    {
        LibraryOperationLeaseIssueOutcome leaseIssue =
            owner.IssueOperationLease(reference);
        if (leaseIssue
            is not LibraryOperationLeaseIssueOutcome.Issued issued)
        {
            CommandError.Write(
                "The exact Library owner could not issue the Member "
                    + "inspection operation lease.");
            return null;
        }

        using LibraryOperationLease lease = issued.Lease;
        return MemberDocumentInspectionOperation.Execute(
            new(reference, plan),
            lease,
            cancellationToken);
    }

    public async ValueTask<
        InspectionEnvelope<MemberDocumentInspectionOutcome>?>
        ExecuteMemberDocumentAsync(
            MemberDocumentInspectionPlan plan,
            CancellationToken cancellationToken)
    {
        LibraryOperationLeaseIssueOutcome leaseIssue =
            owner.IssueOperationLease(reference);
        if (leaseIssue
            is not LibraryOperationLeaseIssueOutcome.Issued issued)
        {
            CommandError.Write(
                "The exact Library owner could not issue the Member "
                    + "inspection operation lease.");
            return null;
        }

        using LibraryOperationLease lease = issued.Lease;
        return await MemberDocumentInspectionOperation.ExecuteAsync(
                new(reference, plan),
                lease,
                (ids, demand, token) =>
                    DirectLibraryDocumentationQuery.ExecuteManyAsync(
                        reference,
                        owner,
                        ids,
                        demand,
                        ApiSurfaceExtractionScope
                            .PublicWithNonPublicTypes,
                        plan.Bounds,
                        s_documentationLimits,
                        TimeSpan.FromSeconds(10),
                        token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<
        InspectionEnvelope<MemberGroupDocumentInspectionOutcome>?>
        ExecuteMemberGroupDocumentAsync(
            MemberOverloadPopulationInspectionPlan plan,
            MemberDocumentationAttachmentRequest
                returnedRowDocumentation,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(
            returnedRowDocumentation);
        LibraryOperationLeaseIssueOutcome leaseIssue =
            owner.IssueOperationLease(reference);
        if (leaseIssue
            is not LibraryOperationLeaseIssueOutcome.Issued issued)
        {
            CommandError.Write(
                "The exact Library owner could not issue the member-group "
                    + "inspection operation lease.");
            return null;
        }

        using LibraryOperationLease lease = issued.Lease;
        var request = new MemberGroupDocumentInspectionRequest(
            reference,
            plan,
            returnedRowDocumentation);
        return await MemberGroupDocumentInspectionOperation.ExecuteAsync(
                request,
                lease,
                (ids, demand, token) =>
                    DirectLibraryDocumentationQuery.ExecuteManyAsync(
                        reference,
                        owner,
                        ids,
                        demand,
                        ApiSurfaceExtractionScope
                            .PublicWithNonPublicTypes,
                        plan.Bounds,
                        s_documentationLimits,
                        TimeSpan.FromSeconds(10),
                        token),
                cancellationToken)
            .ConfigureAwait(false);
    }
}

internal static class ExactLibraryInspectionExecutor
{
    private const long MaxAssemblyImageBytes =
        512L * 1024 * 1024;
    private const int MaxCompiledDocumentationBytes =
        8 * 1024 * 1024;

    public static async Task<T?> ExecuteAsync<T>(
        string assemblyPath,
        string provenanceLabel,
        Func<ExactLibraryInspectionSession, T?> inspect,
        CancellationToken cancellationToken,
        AssemblyContextLibraryRole role = AssemblyContextLibraryRole.ApiOnly)
        where T : class
    {
        AssemblyDescriptorSelectionResult selection;
        try
        {
            selection = ResolvedAssemblyReference.SelectFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local(
                    provenanceLabel));
        }
        catch (Exception failure)
            when (failure is IOException
                or UnauthorizedAccessException)
        {
            CommandError.Write(
                "The direct Library file could not be read.");
            return null;
        }
        if (selection
            is not AssemblyDescriptorSelectionResult.Ready ready)
        {
            WriteSelectionFailure(selection);
            return null;
        }

        ExceptionDispatchInfo? primaryFailure = null;
        List<string> cleanupFailures = [];
        AssemblyContextLibraryInspectionRun<T>? run = null;
        var workspace = new InspectionWorkspace();
        AssemblyContextGroup? group = null;
        try
        {
            var participant = new AssemblyContextParticipant(
                ready.Reference,
                NoResolverAssemblyBindingPolicy.Instance);
            group = workspace.CreateAssemblyContextGroup(
                [participant],
                new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes =
                        MaxAssemblyImageBytes,
                });
            run = await AssemblyContextLibraryInspection.ExecuteAsync(
                    AssemblyContextLibraryAdapter.MaterializeAsync(
                        group,
                        participant,
                        role,
                        new AssemblyContextLibraryMaterializationLimits(
                            MaxAssemblyImageBytes,
                            MaxAssemblyImageBytes),
                        cancellationToken),
                    (reference, owner) =>
                        inspect(
                            new ExactLibraryInspectionSession(
                                reference,
                                owner)),
                    cleanupFailures)
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(failure);
        }
        finally
        {
            try
            {
                group?.Dispose();
            }
            catch
            {
                cleanupFailures.Add(
                    "The ephemeral assembly group could not retire.");
            }

            try
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                cleanupFailures.Add(
                    "The ephemeral inspection Workspace could not retire.");
            }
        }

        foreach (string failure in cleanupFailures)
            CommandError.Write(failure);
        primaryFailure?.Throw();

        if (cleanupFailures.Count > 0)
            return null;
        if (run?.Failure is { } terminalFailure)
        {
            CommandError.Write(terminalFailure);
            return null;
        }

        return run?.Result;
    }

    public static async Task<T?> ExecuteComposedAsync<T>(
        string assemblyPath,
        string provenanceLabel,
        Func<ExactLibraryInspectionSession, ValueTask<T?>> inspect,
        CancellationToken cancellationToken,
        AssemblyContextLibraryRole role =
            AssemblyContextLibraryRole.ApiOnly)
        where T : class
    {
        AssemblyDescriptorSelectionResult selection;
        try
        {
            selection = ResolvedAssemblyReference.SelectFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local(
                    provenanceLabel));
        }
        catch (Exception failure)
            when (failure is IOException
                or UnauthorizedAccessException)
        {
            CommandError.Write(
                "The direct Library file could not be read.");
            return null;
        }
        if (selection
            is not AssemblyDescriptorSelectionResult.Ready ready)
        {
            WriteSelectionFailure(selection);
            return null;
        }

        AssemblyContextLibraryCompiledXml? compiledXml =
            ReadCompiledXml(assemblyPath);
        ExceptionDispatchInfo? primaryFailure = null;
        List<string> cleanupFailures = [];
        AssemblyContextLibraryInspectionRun<T>? run = null;
        var workspace = new InspectionWorkspace();
        AssemblyContextGroup? group = null;
        try
        {
            var participant = new AssemblyContextParticipant(
                ready.Reference,
                NoResolverAssemblyBindingPolicy.Instance);
            group = workspace.CreateAssemblyContextGroup(
                [participant],
                new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes =
                        MaxAssemblyImageBytes,
                });
            run =
                await AssemblyContextLibraryInspection
                    .ExecuteComposedAsync(
                        AssemblyContextLibraryAdapter.MaterializeAsync(
                            group,
                            participant,
                            role,
                            new AssemblyContextLibraryMaterializationLimits(
                                MaxAssemblyImageBytes,
                                MaxAssemblyImageBytes),
                            portablePdb: null,
                            compiledXml,
                            cancellationToken),
                        (reference, owner) =>
                            inspect(
                                new ExactLibraryInspectionSession(
                                    reference,
                                    owner)),
                        cleanupFailures)
                    .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(failure);
        }
        finally
        {
            try
            {
                group?.Dispose();
            }
            catch
            {
                cleanupFailures.Add(
                    "The ephemeral assembly group could not retire.");
            }

            try
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                cleanupFailures.Add(
                    "The ephemeral inspection Workspace could not retire.");
            }
        }

        foreach (string failure in cleanupFailures)
            CommandError.Write(failure);
        primaryFailure?.Throw();

        if (cleanupFailures.Count > 0)
            return null;
        if (run?.Failure is { } terminalFailure)
        {
            CommandError.Write(terminalFailure);
            return null;
        }

        return run?.Result;
    }

    private static AssemblyContextLibraryCompiledXml? ReadCompiledXml(
        string assemblyPath)
    {
        string xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        if (!File.Exists(xmlPath))
            return null;

        var file = new FileInfo(xmlPath);
        if (file.Length > MaxCompiledDocumentationBytes)
        {
            throw new InvalidOperationException(
                "The direct Library compiled documentation exceeds the "
                    + $"{MaxCompiledDocumentationBytes}-byte limit.");
        }

        byte[] content;
        try
        {
            content = File.ReadAllBytes(xmlPath);
        }
        catch (Exception failure)
            when (failure is IOException
                or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "The direct Library compiled documentation could not be read.",
                failure);
        }
        return new(
            ImmutableArray.CreateRange(content),
            Path.GetFileName(xmlPath));
    }

    private static void WriteSelectionFailure(
        AssemblyDescriptorSelectionResult selection)
    {
        switch (selection)
        {
            case AssemblyDescriptorSelectionResult.Descriptorless:
                CommandError.Write(
                    "The selected Library is not a managed assembly.");
                break;
            case AssemblyDescriptorSelectionResult.Rejected rejected:
                CommandError.Write(
                    "The selected Library could not be admitted as a "
                        + "managed assembly.",
                    [$"Admission kind: {rejected.Failure.Kind}"]);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown direct Library descriptor selection result.");
        }
    }
}
