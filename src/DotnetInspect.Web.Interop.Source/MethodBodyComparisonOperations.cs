using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspect.Web.Interop.Source.Operations;

[SupportedOSPlatform("browser")]
internal static class MethodBodyComparisonOperations
{
    internal static Task<BrowserMethodBodyComparison> RunMethodBodyComparison(
        string requestJson,
        CancellationToken token)
    {
        BrowserMethodBodyComparisonRequest request = ParseRequest(requestJson);
        return MethodBodyOperations.WithParticipantAsync(
            request.PackageId,
            request.Version,
            request.Framework,
            request.Assembly,
            (group, participant) =>
                Compare(request, group, participant, token));
    }

    internal static Task<BrowserMethodBodyComparison>
        RunRetainedMethodBodyComparison(
            string retainedDefinitionId,
            string realizationId,
            string navigationId,
            string requestJson,
            CancellationToken token)
    {
        BrowserMethodBodyComparisonRequest request =
            ParseRequest(requestJson);
        return MethodBodyOperations.WithRetainedPackageParticipantAsync(
            retainedDefinitionId,
            realizationId,
            navigationId,
            request.PackageId,
            request.Version,
            request.Framework,
            request.Assembly,
            (group, participant) =>
                Compare(request, group, participant, token),
            token);
    }

    static BrowserMethodBodyComparisonRequest ParseRequest(
        string requestJson)
    {
        return MethodBodyOperations.Select(() =>
        {
            BrowserMethodBodyComparisonRequest parsed =
                JsonSerializer.Deserialize(
                    requestJson,
                    BrowserSourceJsonContext.Default
                        .BrowserMethodBodyComparisonRequest)
                ?? throw new ArgumentException(
                    "A method-body comparison request is required.");
            ValidateRequest(parsed);
            return parsed;
        });
    }

    static BrowserMethodBodyComparison Compare(
        BrowserMethodBodyComparisonRequest request,
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        CancellationToken token)
    {
        ApiSurface surface = MethodBodyOperations.Select(() =>
            BrowserMemberResolution.ImplementationSurface(
                group,
                participant));
        BrowserMethodBodySelection[] inventory =
            MethodBodyOperations.Inventory(surface);
        AssemblyContextMemberBody before = Resolve(request.Before);
        AssemblyContextMemberBody after = Resolve(request.After);
        ProjectedMethodAddress beforeDesignation =
            MethodBodyOperations.RequireDesignation(
                group,
                participant,
                before.BodyToken);
        ProjectedMethodAddress afterDesignation =
            MethodBodyOperations.RequireDesignation(
                group,
                participant,
                after.BodyToken);
        Guid expectedModule = Guid.Parse(request.ModuleVersionId);
        if (beforeDesignation.ModuleVersionId != expectedModule
            || afterDesignation.ModuleVersionId != expectedModule)
        {
            throw new MethodBodyUnavailableException(
                $"WrongImage: inventory module {expectedModule:D} "
                + "is not the retained implementation module "
                + $"{beforeDesignation.ModuleVersionId:D}; the pair "
                + "was not retargeted.");
        }

        request = request with
        {
            Before = inventory.Single(method =>
                method.MetadataToken == before.BodyToken),
            After = inventory.Single(method =>
                method.MetadataToken == after.BodyToken),
        };
        LocalComparisonQueryResult comparison =
            DirectMemberComparisonQuery.Execute(
                group,
                new(
                    new(participant, beforeDesignation),
                    new(participant, afterDesignation),
                    [
                        ResearchProducerKind.CSharp,
                        ResearchProducerKind.IlBody,
                    ]),
                token);
        return BrowserMethodBodyProjection.Project(
            request,
            comparison);

        AssemblyContextMemberBody Resolve(
            BrowserMethodBodySelection selection)
        {
            if (!inventory.Any(method =>
                method.MetadataToken == selection.MetadataToken
                && method.TypeIdentity == selection.TypeIdentity
                && method.MemberName == selection.MemberName
                && method.SelectorKey == selection.SelectorKey))
            {
                throw new MethodBodyUnavailableException(
                    "SelectionUnavailable: the exact selector and "
                    + "MethodDef are not in this implementation "
                    + "inventory.");
            }
            AssemblyContextMemberBody resolved =
                MethodBodyOperations.Select(() =>
                    BrowserMemberResolution
                        .ResolveImplementationMember(
                            surface,
                            selection.TypeIdentity,
                            selection.MemberName,
                            selection.SelectorKey,
                            selection.MetadataToken));
            if (resolved.BodyToken != selection.MetadataToken)
            {
                throw new MethodBodyUnavailableException(
                    "SelectionUnavailable: the inventory body no "
                    + "longer resolves to its asserted MethodDef.");
            }
            return resolved;
        }
    }

    static void ValidateRequest(
        BrowserMethodBodyComparisonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Framework);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Assembly);
        if (!Guid.TryParse(request.ModuleVersionId, out _))
        {
            throw new ArgumentException(
                "WrongImage: a valid inventory module version ID is required.");
        }
        Validate(request.Before);
        Validate(request.After);

        static void Validate(BrowserMethodBodySelection selection)
        {
            ArgumentNullException.ThrowIfNull(selection);
            ArgumentException.ThrowIfNullOrWhiteSpace(
                selection.TypeIdentity);
            ArgumentException.ThrowIfNullOrWhiteSpace(
                selection.MemberName);
            ArgumentException.ThrowIfNullOrWhiteSpace(
                selection.SelectorKey);
            if ((selection.MetadataToken
                    & unchecked((int)0xff000000)) != 0x06000000
                || (selection.MetadataToken & 0x00ffffff) == 0)
            {
                throw new ArgumentException(
                    "SelectionUnavailable: an inventory MethodDef "
                    + "is required.");
            }
        }
    }
}
