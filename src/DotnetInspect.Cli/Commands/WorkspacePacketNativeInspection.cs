using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Presentation;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using InertText;

namespace DotnetInspect.Cli.Commands;

internal static class WorkspacePacketNativeInspection
{
    private static readonly InspectionEnvelopeJsonContract<
        LibraryInspectionOutcome> s_libraryJson =
        new("library-inspection", 1,
            LibraryInspectionJsonContext.Default.LibraryInspectionOutcome);

    private static readonly InspectionEnvelopeJsonContract<
        TypeOverviewDocumentInspectionOutcome> s_typeJson =
        new("type-overview", 1,
            TypeMemberInspectionDocumentJsonContext.Default
                .TypeOverviewDocumentInspectionOutcome);

    internal static ValueTask<int> ExecuteAsync(
        WorkspaceOptions options,
        CompleteRestorationSelectedSubject.Admitted target,
        DotnetInspector.Libraries.LibraryOperationLease lease,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(target switch
        {
            CompleteRestorationSelectedSubject.AdmittedLibrary =>
                Library(options, target, lease, cancellationToken),
            CompleteRestorationSelectedSubject.AdmittedType type =>
                Type(options, type, lease, cancellationToken),
            _ => Unavailable("The saved native subject is unsupported."),
        });

    private static int Library(
        WorkspaceOptions options,
        CompleteRestorationSelectedSubject.Admitted target,
        DotnetInspector.Libraries.LibraryOperationLease lease,
        CancellationToken cancellationToken)
    {
        LibraryTypeHierarchyPresentationPlan presentation =
            LibraryTypeHierarchyPresentation.CreateDefaultPlan(
                LibraryTypeHierarchyPresentationFormat.Tree);
        LibraryInspectionPlan plan =
            LibraryTypeHierarchyCommand.CreateInspectionPlan(presentation);
        if (options.Count)
            plan = new(
                new LibraryTypePopulationRequest(
                    presentation.Types.Accessibility,
                    new LibraryTypePopulationCountRequest()),
                plan.Bounds);
        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            LibraryInspectionOperation.Execute(
                new(target.Occurrence.Library, plan),
                lease,
                cancellationToken);
        TypeCommand.WriteInspectionDiagnostics(envelope.Diagnostics);
        if (envelope.Diagnostics.Any(static diagnostic =>
                diagnostic.Severity == InspectionDiagnosticSeverity.Error))
            return Unavailable("The Library inspection reported errors.");
        if (envelope.Content is not LibraryInspectionOutcome.Available available)
            return Unavailable($"The Library inspection returned {envelope.Content}.");
        if (options.Count)
        {
            if (available.Document.Types?.Count
                is not LibraryTypePopulationCountOutcome.Counted counted)
                return Unavailable("The Library Type count is unavailable.");
            CountOutput.WriteCount(counted.Total);
            return 0;
        }
        if (LibraryTypeHierarchyCommand.DescribeRowsFailure(
                available.Document.Types?.Rows) is { } rowsFailure)
            return Unavailable(rowsFailure);
        if (!LibraryTypeHierarchyCommand.Items.TrySelect(
                available.Document,
                options.RowSelection,
                out LibraryDocument document))
            return 1;
        if (options.Format == OutputFormat.Json)
            return InspectionEnvelopeOutput.TryWrite(
                new InspectionEnvelope<LibraryInspectionOutcome>(
                    new LibraryInspectionOutcome.Available(document),
                    envelope.Share,
                    envelope.Diagnostics),
                s_libraryJson,
                includeEnvelope: false) ? 0 : 1;
        try
        {
            LibraryTypeHierarchyPresentation.Write(
                document,
                presentation,
                Console.Out,
                new InertString(
                    TextPolicy.Field,
                    target.Occurrence.Library.ApiAssembly
                        .AssemblyIdentity!.Identity.Name),
                [new("source", SourceKind.Platform)]);
            return 0;
        }
        catch (InvalidOperationException failure)
        {
            return Unavailable(failure.Message);
        }
    }

    private static int Type(
        WorkspaceOptions options,
        CompleteRestorationSelectedSubject.AdmittedType target,
        DotnetInspector.Libraries.LibraryOperationLease lease,
        CancellationToken cancellationToken)
    {
        TypeOverviewHierarchyPresentationPlan presentation =
            TypeOverviewHierarchyPresentation.CreateCompactPlan(
                TypeOverviewHierarchyPresentationFormat.Tree,
                includeNonPublic: false);
        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome> envelope =
            TypeOverviewDocumentInspectionOperation.Execute(
                new(
                    target.Occurrence.Library,
                    TypeOverviewHierarchyPresentation.CreateInspectionPlan(
                        target.Subject.Name,
                        presentation,
                        TypeInspectionPolicies.CompactOverview.ExtractionBounds)),
                lease,
                cancellationToken);
        TypeCommand.WriteInspectionDiagnostics(envelope.Diagnostics);
        if (envelope.Diagnostics.Any(static diagnostic =>
                diagnostic.Severity == InspectionDiagnosticSeverity.Error))
            return Unavailable("The Type overview reported errors.");
        if (envelope.Content
            is not TypeOverviewDocumentInspectionOutcome.Available available)
            return Unavailable($"The Type overview returned {envelope.Content}.");
        if (available.Document.Members.Rows
            is not TypeMemberGroupRowsOutcome.Read { Continuation: null })
            return Unavailable("The Type overview Member-group Rows are incomplete.");
        if (options.RowSelection is not null)
            return Unavailable(
                "Semantic row selection is not supported by the Type overview hierarchy.");
        if (options.Format == OutputFormat.Json)
            return InspectionEnvelopeOutput.TryWrite(
                envelope, s_typeJson, includeEnvelope: false) ? 0 : 1;
        try
        {
            TypeOverviewHierarchyPresentation.Write(
                available.Document, presentation, Console.Out);
            return 0;
        }
        catch (InvalidOperationException failure)
        {
            return Unavailable(failure.Message);
        }
    }

    private static int Unavailable(string message)
    {
        CommandError.Write(message);
        return 1;
    }
}
