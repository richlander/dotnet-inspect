using QuerySpace;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Definitions;

/// <summary>Exact Diff adoption of the existing committed packet/query contract.</summary>
public static class WorkspaceDiffShareProjection
{
    public static WorkspaceSharePacket Create(
        WorkspaceSharePacket presentation,
        PortableLibraryIdentity library,
        WorkspaceDiffIntent intent)
    {
        if (presentation.FormatVersion != WorkspaceSharePacketCodec.LegacyFormatVersion
            || presentation.SourceView is not null
            || presentation.Libraries.Count != 1
            || presentation.Libraries[0] != intent.Asset
            || presentation.Lens is not ("compare" or "library:compare" or "api")
            || presentation.Section is not (null or "compare"))
            throw new ArgumentException("Exact Diff capture requires one exact Library and Compare subject.");
        WorkspaceShareTab after = presentation.Tabs[presentation.ActiveTabIndex];
        if (after.SourceKind != WorkspaceShareSourceKind.Package || after.Version is null || after.Framework is null)
            throw new ArgumentException("Exact Diff capture requires an exact Gallery After coordinate.");
        bool memberSelected = presentation.MemberAnchor is not null || presentation.MemberSignature is not null;
        if (presentation.Lens != (presentation.Type is null ? "library:compare" : memberSelected ? "api" : "compare"))
            throw new ArgumentException("Diff presentation must select the matching subject and facet.");
        if (intent.Body is not null && MemberTargetSelector.Parse(intent.Body).DigestPrefix != presentation.MemberAnchor)
            throw new ArgumentException("The Diff body selector must belong to the retained Member anchor.");
        PortableRetainedSubjectContext context = presentation.Type is null
            ? new PortableRetainedSubjectContext.Library(library)
            : presentation.MemberAnchor is null && presentation.MemberSignature is null
                ? new PortableRetainedSubjectContext.EscapedType(library, presentation.Type)
                : new PortableRetainedSubjectContext.EscapedMember(library, presentation.Type,
                    presentation.MemberAnchor, presentation.MemberSignature);
        PortableSubjectRequest subject = context.Kind switch
        {
            PortableRetainedSubjectContextKind.Library => new PortableSubjectRequest.Library(),
            PortableRetainedSubjectContextKind.Type => new PortableSubjectRequest.Type(),
            _ => new PortableSubjectRequest.Member(),
        };
        string facet = subject.Kind switch
        {
            PortableSubjectRequestKind.Library => "library.compare",
            PortableSubjectRequestKind.Type => "type.compare",
            _ => "member.compare",
        };
        if (subject.Kind == PortableSubjectRequestKind.Member && presentation.Section != "compare")
            throw new ArgumentException("An exact Member Diff must select its Compare section.");
        var states = new List<WorkspaceShareViewState>
        {
            new(null, new PortableSubjectRequest.Workspace(), null, null),
        };
        for (int index = 0; index < presentation.Tabs.Count; index++)
            states.Add(index == presentation.ActiveTabIndex
                ? new(index, subject, context, facet, [0])
                : new(index, null, null, null));
        var packet = WorkspaceSharePacket.CreateV4([.. presentation.Tabs], [.. presentation.Contexts], [],
            presentation.ActiveTabIndex, presentation.SelectedContextIndex ?? throw new ArgumentException("A selected context is required."), [.. states], [intent.ToIdentity()]);
        _ = WorkspaceSharePacketTransposer.ToCommittedDefinitions(packet);
        return packet;
    }

    public static WorkspaceDiffShare Read(WorkspaceSharePacket packet)
    {
        if (packet.FormatVersion != WorkspaceSharePacketCodec.Format4Version
            || packet.FocusedTabIndex is not int active
            || packet.Queries.Count != 1
            || packet.Queries[0].Vocabulary != WorkspaceDiffIntent.QueryId)
            throw new ArgumentException("This packet is not a supported exact Diff presentation.");
        _ = WorkspaceSharePacketTransposer.ToCommittedDefinitions(packet);
        WorkspaceDiffIntent intent = WorkspaceDiffIntent.FromIntent(PortableQueryPayloadCodec.Decode(packet.Queries[0].Payload));
        WorkspaceShareViewState state = packet.ViewStates[active + 1];
        PortableLibraryIdentity library;
        string? type = null, anchor = null, signature = null;
        switch (state.Context)
        {
            case PortableRetainedSubjectContext.Library value:
                library = value.LibraryIdentity; break;
            case PortableRetainedSubjectContext.EscapedType value:
                library = value.LibraryIdentity; type = value.EscapedTypeIdentity; break;
            case PortableRetainedSubjectContext.Type value:
                library = value.LibraryIdentity; type = value.TypeIdentity.ToEscapedFullName(); break;
            case PortableRetainedSubjectContext.EscapedMember value:
                library = value.LibraryIdentity; type = value.EscapedTypeIdentity;
                anchor = value.MemberAnchor; signature = value.MemberSignature; break;
            case PortableRetainedSubjectContext.Member value:
                library = value.LibraryIdentity; type = value.TypeIdentity.ToEscapedFullName();
                anchor = value.MemberAnchor; signature = value.MemberSignature; break;
            default: throw new ArgumentException("Exact Diff has no exact retained Library subject.");
        }
        var presentation = new WorkspaceSharePacket([.. packet.Tabs], [.. packet.Contexts], active,
            packet.SelectedContextIndex ?? throw new ArgumentException("Exact Diff requires a selected context."),
            type is null ? "library:compare" : anchor is not null || signature is not null ? "api" : "compare", type, anchor, signature,
            anchor is not null || signature is not null ? "compare" : null, [intent.Asset]);
        // This consumer admits only the complete representable subset it can restore.
        // It never discards dormant state, another query, or a structural association.
        if (WorkspaceSharePacketCodec.Encode(Create(presentation, library, intent)) != WorkspaceSharePacketCodec.Encode(packet))
            throw new ArgumentException("The complete exact Diff state cannot be restored by this presentation consumer.");
        return new(presentation, library, intent);
    }
}

public sealed record WorkspaceDiffShare(
    WorkspaceSharePacket Presentation,
    PortableLibraryIdentity Library,
    WorkspaceDiffIntent Intent);
