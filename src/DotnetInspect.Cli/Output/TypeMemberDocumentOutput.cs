using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class TypeMemberDocumentOutput
{
    internal static bool IsSelected(ApiOptions options) =>
        options is TypeOptions && options.IncludeSections?.Any(
            static section => section is SectionNames.Members
                or SectionNames.DocumentedMembers) == true;

    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(5_000, 100_000, 1_000, 10_000, 1_000_000, 20_000_000);

    internal static async Task<TypeOptions?> AttachAsync(
        ApiType type,
        TypeOptions options,
        string assemblyPath,
        CancellationToken cancellationToken)
    {
        if (options.Count)
        {
            return options with
            {
                MemberSectionCount = Math.Min(
                    AdmittedMembers(type, options).Count(),
                    options.Limit ?? int.MaxValue),
            };
        }
        if (type.DefinitionName is null)
        {
            CommandError.Write("Member sections require one exact Type definition.");
            return null;
        }
        var plan = new TypeDocumentInspectionPlan(type.DefinitionName, s_bounds,
            accessibility: options.IncludeAll
                ? TypeMemberGroupAccessibilityFilter.All
                : TypeMemberGroupAccessibilityFilter.Public,
            includeHidden: options.IncludeAll);
        TypeDocumentInspectionOutcome outcome;
        MemberDocumentationAttachmentOutcome? documentation = null;
        if (options.IncludeSections!.Contains(SectionNames.DocumentedMembers))
        {
            var envelope = await ExactLibraryInspectionExecutor.ExecuteComposedAsync<
                InspectionEnvelope<TypeDocumentAttachmentCompositionResult>>(
                assemblyPath, "documented Type Member sections",
                session => session.ExecuteTypeDocumentAttachmentCompositionAsync(
                    plan, new(DocumentationDemand.CompiledXml), cancellationToken),
                includeCompiledDocumentation: true, cancellationToken: cancellationToken);
            if (envelope is null)
                return null;
            TypeCommand.WriteInspectionDiagnostics(envelope.Diagnostics);
            outcome = envelope.Content.Inspection;
            documentation = envelope.Content.Documentation;
        }
        else
        {
            var envelope = await ExactLibraryInspectionExecutor.ExecuteAsync<
                InspectionEnvelope<TypeDocumentInspectionOutcome>>(
                assemblyPath, "Type Member sections",
                session => session.ExecuteTypeDocument(plan, cancellationToken), cancellationToken);
            if (envelope is null)
                return null;
            TypeCommand.WriteInspectionDiagnostics(envelope.Diagnostics);
            outcome = envelope.Content;
        }
        if (outcome is not TypeDocumentInspectionOutcome.Available available)
        {
            CommandError.Write(outcome switch
            {
                TypeDocumentInspectionOutcome.Rejected rejected =>
                    $"Type Member inspection was rejected ({rejected.Reason}).",
                TypeDocumentInspectionOutcome.Incomplete incomplete =>
                    $"Type Member inspection reached {incomplete.Bound} ({incomplete.Measured}/{incomplete.Limit}).",
                TypeDocumentInspectionOutcome.Failed failed =>
                    $"Type Member inspection failed ({failed.Reason}).",
                _ => "Type Member inspection did not complete.",
            });
            return null;
        }
        return options with
        {
            MemberDeclarations = available.Document,
            MemberDocumentation = documentation,
        };
    }

    private static IEnumerable<ApiMember> AdmittedMembers(ApiType type, TypeOptions options) =>
        ApiOutputFormatter.GroupMembersByKind(
                type, options.MemberFilter, options.UnsafeOnly, options.KindFilter)
            .SelectMany(static group => group.Value)
            .Where(member => (options.IncludeAll || !member.IsHidden)
                && (member.DeclaringTypeDefinitionName is null
                    || member.DeclaringTypeDefinitionName == type.DefinitionName));

    private static IEnumerable<(MemberDeclaration Declaration, ApiMember Selector)> Declarations(
        ApiType type, TypeOptions options)
    {
        var admitted = AdmittedMembers(type, options)
            .Where(static member => member.DeclarationMetadataToken is not null || member.MetadataToken is not null)
            .ToDictionary(static member => (member.DeclarationMetadataToken ?? member.MetadataToken)!.Value);
        return (options.MemberDeclarations?.Members ?? [])
            .Where(member => admitted.ContainsKey(member.Subject.MetadataToken))
            .Take(options.Limit ?? int.MaxValue)
            .Select(member => (member, admitted[member.Subject.MetadataToken]));
    }

    // CLI selectors are issued by the existing selector identity owner. Associate
    // them by declaration token, as MemberGroupDocumentOutput does for overloads;
    // the document's exact subject and declaration order remain authoritative.
    private static string Anchor(ApiType type, ApiMember selector) =>
        MarkoutInline.Code(ApiMemberIdentity.GetMemberAnchor(type, selector)
            .Format(MemberAnchorFormat.StableSelector));

    internal static List<TypeMemberRow>? Rows(ApiType type, TypeOptions options) =>
        options.MemberDeclarations is null ? null
            : Declarations(type, options).Select(member => new TypeMemberRow(
                member.Declaration.Subject.Group.Category.ToString(),
                Anchor(type, member.Selector),
                MarkoutInline.Code(MemberGroupPresentation.FormatOverload(member.Declaration)))).ToList();

    internal static List<DocumentedTypeMemberRow>? DocumentedRows(ApiType type, TypeOptions options)
    {
        if (options.MemberDeclarations is null)
            return null;
        var attachments = (options.MemberDocumentation
            as MemberDocumentationAttachmentOutcome.Available)?.Attachments
            .ToDictionary(static attachment => attachment.Subject);
        return Declarations(type, options).Select(member => new DocumentedTypeMemberRow(
            member.Declaration.Subject.Group.Category.ToString(),
            Anchor(type, member.Selector),
            MarkoutInline.Code(MemberGroupPresentation.FormatOverload(member.Declaration)),
            attachments is not null
                ? MemberDocumentOutput.DescribeDocumentation(attachments[member.Declaration.Subject].Outcome)
                : $"Documentation attachment failed ({(options.MemberDocumentation as MemberDocumentationAttachmentOutcome.Failed)?.Reason})."))
            .ToList();
    }
}
