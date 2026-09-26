using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;

namespace DotnetInspect.Web.Interop.Source.Operations;

[SupportedOSPlatform("browser")]
internal static class MethodBodyComparisonOperations
{
    internal static Task<BrowserMethodBodyComparison> RunMethodBodyComparison(
        string requestJson,
        CancellationToken token)
    {
        BrowserMethodBodyComparisonRequest request =
            MethodBodyOperations.Select(() =>
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
        return MethodBodyOperations.WithParticipantAsync(
            request.PackageId,
            request.Version,
            request.Framework,
            request.Assembly,
            (group, participant) =>
            {
                ApiSurface surface = MethodBodyOperations.Select(() =>
                    BrowserMemberResolution.ImplementationSurface(
                        group,
                        participant));
                BrowserMethodBodySelection[] inventory =
                    MethodBodyOperations.Inventory(surface);
                CallGraphMemberResolution before = Resolve(request.Before);
                CallGraphMemberResolution after = Resolve(request.After);
                MetadataMethodAddress beforeAddress =
                    MethodBodyOperations.RequireAddress(
                        group,
                        participant,
                        before.BodyToken);
                MetadataMethodAddress afterAddress =
                    MethodBodyOperations.RequireAddress(
                        group,
                        participant,
                        after.BodyToken);
                Guid expectedModule = Guid.Parse(request.ModuleVersionId);
                if (beforeAddress.ModuleVersionId != expectedModule
                    || afterAddress.ModuleVersionId != expectedModule)
                {
                    throw new MethodBodyUnavailableException(
                        $"WrongImage: inventory module {expectedModule:D} "
                        + "is not the retained implementation module "
                        + $"{beforeAddress.ModuleVersionId:D}; the pair "
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
                            new(participant, beforeAddress),
                            new(participant, afterAddress),
                            [
                                ResearchProducerKind.CSharp,
                                ResearchProducerKind.IlBody,
                            ]),
                        token);
                return BrowserMethodBodyProjection.Project(
                    request,
                    comparison);

                CallGraphMemberResolution Resolve(
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
                    CallGraphMemberResolution resolved =
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
            });
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
