using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;
using InspectWeb.Engine;
using InspectWeb.Engine.SourceFacade;

[SupportedOSPlatform("browser")]
public static partial class SourceExports
{
    [JSExport]
    public static string CancelMethodBodyComparison(string operationId, string reason)
    {
        BrowserTypeSourceCancellation result = BrowserTypeSourceCancellation.From(
            TypeSourceOperations.RequestCancellation(
                BrowserManagedOperationId.From(operationId),
                BrowserTypeSourceCancellation.ParseReason(reason)));
        return JsonSerializer.Serialize(
            result, BrowserSourceJsonContext.Default.BrowserTypeSourceCancellation);
    }

    [JSExport]
    public static async Task<string> QueryMethodBodyComparisonTargets(
        string operationId, string packageId, string version, string targetFramework,
        string assemblyName, string typeIdentity, string memberName, string selectorKey,
        int metadataToken)
    {
        var result = await RunMethodBodyOperation(operationId, _ =>
            WithMethodBodyParticipantAsync(packageId, version, targetFramework, assemblyName,
                (group, participant) =>
                {
                    ApiSurface surface = SelectMethodBody(() =>
                        BrowserMemberResolution.ImplementationSurface(group, participant));
                    CallGraphMemberResolution before = SelectMethodBody(() =>
                        BrowserMemberResolution.ResolveImplementationMember(
                            surface, typeIdentity, memberName, selectorKey, metadataToken));
                    MetadataMethodAddress address = RequireMethodAddress(group, participant, before.BodyToken);
                    BrowserMethodBodySelection[] methods = MethodBodyInventory(surface);
                    BrowserMethodBodySelection selection = methods.SingleOrDefault(
                        method => method.MetadataToken == before.BodyToken)
                        ?? throw new MethodBodyUnavailableException(
                            "SelectionUnavailable: the selected implementation body has no inventory identity.");
                    return new BrowserMethodBodyTargets(
                        packageId, version, targetFramework, assemblyName,
                        address.ModuleVersionId.ToString("D"), selection, methods);
                }));
        BrowserMethodBodyTargetsResult wire = result switch
        {
            BrowserManagedOperationResult<BrowserMethodBodyTargets, string, string>.Succeeded success =>
                new(1, BrowserMethodBodyResultKind.Succeeded, success.Value, null, null, null, null),
            BrowserManagedOperationResult<BrowserMethodBodyTargets, string, string>.Failed failure =>
                new(1, BrowserMethodBodyResultKind.Failed, null, MethodBodyFailureKind(failure.FailureKind),
                    failure.Error, failure.Diagnostic, null),
            BrowserManagedOperationResult<BrowserMethodBodyTargets, string, string>.Canceled canceled =>
                new(1, BrowserMethodBodyResultKind.Canceled, null, null, null, null,
                    BrowserTypeSourceCancellation.FormatReason(canceled.Reason)),
            _ => throw new InvalidOperationException("Unknown managed method-body outcome."),
        };
        return JsonSerializer.Serialize(wire, BrowserSourceJsonContext.Default.BrowserMethodBodyTargetsResult);
    }

    [JSExport]
    public static async Task<string> QueryMethodBodyComparison(string operationId, string requestJson)
    {
        var result = await RunMethodBodyOperation(operationId, token =>
        {
            BrowserMethodBodyComparisonRequest request = SelectMethodBody(() =>
            {
                BrowserMethodBodyComparisonRequest parsed = JsonSerializer.Deserialize(
                    requestJson, BrowserSourceJsonContext.Default.BrowserMethodBodyComparisonRequest)
                    ?? throw new ArgumentException("A method-body comparison request is required.");
                ValidateMethodBodyRequest(parsed);
                return parsed;
            });
            return WithMethodBodyParticipantAsync(
                request.PackageId, request.Version, request.Framework, request.Assembly,
                (group, participant) =>
                {
                    ApiSurface surface = SelectMethodBody(() =>
                        BrowserMemberResolution.ImplementationSurface(group, participant));
                    BrowserMethodBodySelection[] inventory = MethodBodyInventory(surface);
                    CallGraphMemberResolution before = Resolve(request.Before);
                    CallGraphMemberResolution after = Resolve(request.After);
                    MetadataMethodAddress beforeAddress = RequireMethodAddress(group, participant, before.BodyToken);
                    MetadataMethodAddress afterAddress = RequireMethodAddress(group, participant, after.BodyToken);
                    Guid expectedModule = Guid.Parse(request.ModuleVersionId);
                    if (beforeAddress.ModuleVersionId != expectedModule
                        || afterAddress.ModuleVersionId != expectedModule)
                    {
                        throw new MethodBodyUnavailableException(
                            $"WrongImage: inventory module {expectedModule:D} is not the retained implementation "
                            + $"module {beforeAddress.ModuleVersionId:D}; the pair was not retargeted.");
                    }
                    // Request labels are presentation input, never authority for the resolved pair.
                    request = request with
                    {
                        Before = inventory.Single(method => method.MetadataToken == before.BodyToken),
                        After = inventory.Single(method => method.MetadataToken == after.BodyToken),
                    };
                    LocalComparisonQueryResult comparison = DirectMemberComparisonQuery.Execute(
                        group,
                        new(new(participant, beforeAddress), new(participant, afterAddress),
                            [ResearchProducerKind.CSharp, ResearchProducerKind.IlBody]),
                        token);
                    return BrowserMethodBodyProjection.Project(request, comparison);

                    CallGraphMemberResolution Resolve(BrowserMethodBodySelection selection)
                    {
                        if (!inventory.Any(method => method.MetadataToken == selection.MetadataToken
                            && method.TypeIdentity == selection.TypeIdentity
                            && method.MemberName == selection.MemberName
                            && method.SelectorKey == selection.SelectorKey))
                            throw new MethodBodyUnavailableException(
                                "SelectionUnavailable: the exact selector and MethodDef are not in this implementation inventory.");
                        CallGraphMemberResolution resolved =
                            SelectMethodBody(() => BrowserMemberResolution.ResolveImplementationMember(
                                surface, selection.TypeIdentity, selection.MemberName,
                                selection.SelectorKey, selection.MetadataToken));
                        if (resolved.BodyToken != selection.MetadataToken)
                            throw new MethodBodyUnavailableException(
                                "SelectionUnavailable: the inventory body no longer resolves to its asserted MethodDef.");
                        return resolved;
                    }
                });
        });
        BrowserMethodBodyComparisonResult wire = result switch
        {
            BrowserManagedOperationResult<BrowserMethodBodyComparison, string, string>.Succeeded success =>
                new(1, BrowserMethodBodyResultKind.Succeeded, success.Value, null, null, null, null),
            BrowserManagedOperationResult<BrowserMethodBodyComparison, string, string>.Failed failure =>
                new(1, BrowserMethodBodyResultKind.Failed, null, MethodBodyFailureKind(failure.FailureKind),
                    failure.Error, failure.Diagnostic, null),
            BrowserManagedOperationResult<BrowserMethodBodyComparison, string, string>.Canceled canceled =>
                new(1, BrowserMethodBodyResultKind.Canceled, null, null, null, null,
                    BrowserTypeSourceCancellation.FormatReason(canceled.Reason)),
            _ => throw new InvalidOperationException("Unknown managed method-body outcome."),
        };
        return JsonSerializer.Serialize(wire, BrowserSourceJsonContext.Default.BrowserMethodBodyComparisonResult);
    }

    static Task<BrowserManagedOperationResult<T, string, string>> RunMethodBodyOperation<T>(
        string operationId, Func<CancellationToken, Task<T>> query)
    {
        BrowserManagedOperationId id = BrowserManagedOperationId.From(operationId);
        return TypeSourceOperations.RunAsync<T, string, string, object>(
            id, null,
            async (token, _) =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    return new BrowserManagedOperationBodyResult<T, string, string>.Succeeded(
                        await query(token).ConfigureAwait(false));
                }
                catch (MethodBodyUnavailableException error)
                {
                    return new BrowserManagedOperationBodyResult<T, string, string>.Failed(
                        error.Message, error.ToString());
                }
            },
            error => new(error.Message, error.ToString()));
    }

    static BrowserTypeSourceFailureKind MethodBodyFailureKind(BrowserManagedOperationFailureKind kind) =>
        kind switch
        {
            BrowserManagedOperationFailureKind.Expected => BrowserTypeSourceFailureKind.Expected,
            BrowserManagedOperationFailureKind.Unexpected => BrowserTypeSourceFailureKind.Unexpected,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static async Task<T> WithMethodBodyParticipantAsync<T>(
        string packageId, string version, string framework, string assembly,
        Func<AssemblyContextGroup, AssemblyContextParticipant, T> query)
    {
        if (packageId.Length == 0)
        {
            await using BrowserPlatformScopeResolution platform =
                SelectMethodBody(() => BrowserPlatformWorkspace.LeaseRetainedAssembly(framework, version, assembly));
            return platform.Scope.UseParticipant(platform.Participant, query);
        }
        await using BrowserScopeLease<BrowserInspectionScope> lease =
            SelectMethodBody(() => BrowserPackageWorkspace.LeaseRetainedPackageScope(packageId, version, framework));
        BrowserInspectionScope scope = lease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserWorkspaceParticipant implementation = SelectMethodBody(() =>
            scope.ImplementationParticipant(
                scope.SurfaceParticipant(coordinate, coordinate.CompileAsset(assembly))));
        return scope.UseImplementationParticipant(implementation, query);
    }

    static MetadataMethodAddress RequireMethodAddress(
        AssemblyContextGroup group, AssemblyContextParticipant participant, int token) =>
        AssemblyContextMethodAddressQuery.ExecuteParticipant(group, participant, token) switch
        {
            AssemblyContextEntry<MetadataMethodAddress>.Available available => available.Value,
            AssemblyContextEntry<MetadataMethodAddress>.Rejected rejected =>
                throw new MethodBodyUnavailableException(
                    $"AddressRejected: {rejected.Failure.Kind}: {rejected.Failure.Detail}"),
            AssemblyContextEntry<MetadataMethodAddress>.Failed failed =>
                throw new MethodBodyUnavailableException($"AddressFailed: {failed.Error.Message}", failed.Error),
            _ => throw new InvalidOperationException("Unknown method-address projection outcome."),
        };

    static BrowserMethodBodySelection[] MethodBodyInventory(ApiSurface surface)
    {
        if (surface.InspectionFailures.Count > 0)
            throw new MethodBodyUnavailableException(
                "InventoryFailed: " + string.Join("; ", surface.InspectionFailures.Select(failure => failure.ToString())));
        var methods = new Dictionary<int, BrowserMethodBodySelection>();
        foreach (ApiType type in surface.Types)
        {
            string identity = type.DefinitionName?.ToEscapedFullName()
                ?? throw new MethodBodyUnavailableException($"InventoryFailed: '{type.FullName}' has no definition identity.");
            foreach (ApiMember member in type.Members)
            foreach (CallGraphMemberBodySelector body in CallGraphMemberResolver.CreateBodySelectors(type, member))
            {
                if ((body.BodyToken & unchecked((int)0xff000000)) != 0x06000000)
                    continue;
                string label = $"{type.FullName} / {member.Signature ?? member.Name}";
                if (body.MemberName != member.Name)
                    label += $" [{body.MemberName}]";
                methods.TryAdd(body.BodyToken,
                    new(identity, body.MemberName, body.SelectorKey, body.BodyToken, label));
            }
        }
        return [.. methods.Values];
    }

    static void ValidateMethodBodyRequest(BrowserMethodBodyComparisonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Framework);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Assembly);
        if (!Guid.TryParse(request.ModuleVersionId, out _))
            throw new ArgumentException("WrongImage: a valid inventory module version ID is required.");
        Validate(request.Before);
        Validate(request.After);
        static void Validate(BrowserMethodBodySelection selection)
        {
            ArgumentNullException.ThrowIfNull(selection);
            ArgumentException.ThrowIfNullOrWhiteSpace(selection.TypeIdentity);
            ArgumentException.ThrowIfNullOrWhiteSpace(selection.MemberName);
            ArgumentException.ThrowIfNullOrWhiteSpace(selection.SelectorKey);
            if ((selection.MetadataToken & unchecked((int)0xff000000)) != 0x06000000
                || (selection.MetadataToken & 0x00ffffff) == 0)
                throw new ArgumentException("SelectionUnavailable: an inventory MethodDef is required.");
        }
    }

    static T SelectMethodBody<T>(Func<T> select)
    {
        try
        {
            return select();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or JsonException or FormatException)
        {
            throw new MethodBodyUnavailableException(error.Message, error);
        }
    }

    sealed class MethodBodyUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
}
