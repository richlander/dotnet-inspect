using System.Reflection;
using System.Text.Json;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;
using QuerySpace;
using QuerySpace.Composition;

namespace DotnetInspector.Sections.Tests;

public sealed partial class ExactTypeInspectionOperationTests
{
    [Fact]
    public async Task
        TypeInspection_JsonElementMembersShareRowsCountAndReceiverFacet()
    {
        byte[] systemTextJson =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        var store = new InMemoryPackageStore();
        await CommitPackageAsync(
            store,
            PackageId,
            ("lib/net11.0/System.Text.Json.dll", systemTextJson));
        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextInput input = SelectedContextInput(PackageId);
        await using var coordinator = new WorkspaceReplacementCoordinator();
        (WorkspaceRealizationCandidate candidate,
            WorkspaceDeclarationContext context) =
            await PrepareSelectedContextCandidateAsync(
                coordinator,
                input,
                LoadOptions(client, store));
        await ActivateAsync(coordinator, candidate);
        using WorkspaceRealizationOperationLease authority =
            await AdmitAsync(coordinator);
        var subject =
            new SelectedContextExactTypeInspectionRequest(
                "System.Text.Json.JsonElement");

        InspectionEnvelope<TypeInspectionContent> allRowsEnvelope =
            TypeInspectionOperation.Execute(
                authority,
                context,
                new(
                    subject,
                    TypeInspectionMembersQuery.CreateRequest(
                        QuerySpaceTerminalRequirement.Rows)));
        TypeMembersOutcome.Rows allRows = Rows(allRowsEnvelope);
        TypeMembersOutcome.Count allCount =
            Count(
                TypeInspectionOperation.Execute(
                    authority,
                    context,
                    new(
                        subject,
                        TypeInspectionMembersQuery.CreateRequest(
                            QuerySpaceTerminalRequirement.Count))));
        PortableQueryIntent extensionIntent =
            TypeInspectionMembersQuery.CreateRowIntent(
                TypeMemberReceiver.Extension);
        TypeMembersOutcome.Rows extensionRows =
            Rows(
                TypeInspectionOperation.Execute(
                    authority,
                    context,
                    new(
                        subject,
                        TypeInspectionMembersQuery.CreateRequest(
                            QuerySpaceTerminalRequirement.Rows,
                            extensionIntent))));
        TypeMembersOutcome.Count extensionCount =
            Count(
                TypeInspectionOperation.Execute(
                    authority,
                    context,
                    new(
                        subject,
                        TypeInspectionMembersQuery.CreateRequest(
                            QuerySpaceTerminalRequirement.Count,
                            extensionIntent))));
        PortableQueryIntent nonExtensionIntent =
            TypeInspectionMembersQuery.CreateRowIntent(
                TypeMemberReceiver.Extension,
                exclude: true);
        TypeMembersOutcome.Rows nonExtensionRows =
            Rows(
                TypeInspectionOperation.Execute(
                    authority,
                    context,
                    new(
                        subject,
                        TypeInspectionMembersQuery.CreateRequest(
                            QuerySpaceTerminalRequirement.Rows,
                            nonExtensionIntent))));

        Assert.Equal(62, allRows.Items.Length);
        Assert.Equal(allRows.Items.Length, allCount.Value);
        Assert.Equal(allRows.Binding, allCount.Binding);
        Assert.Equal(5, extensionRows.Items.Length);
        Assert.Equal(extensionRows.Items.Length, extensionCount.Value);
        Assert.Equal(extensionRows.Binding, extensionCount.Binding);
        Assert.Equal(57, nonExtensionRows.Items.Length);
        Assert.Equal(
            allRows.Items.Length,
            extensionRows.Items.Length + nonExtensionRows.Items.Length);
        Assert.Equal(
            allRows.Items
                .Where(row => row.Receiver
                    is TypeMemberReceiver.Extension)
                .Select(row => row.Declaration),
            extensionRows.Items.Select(row => row.Declaration));
        Assert.All(
            extensionRows.Items,
            row =>
            {
                Assert.Equal(
                    TypeMemberReceiver.Extension,
                    row.Receiver);
                Assert.Equal(
                    TypeMemberRowKind.AttachedExtension,
                    row.RowKind);
                Assert.Equal(
                    "System.Text.Json",
                    row.Declaration.DeclaringType.Definition.Namespace);
                Assert.Equal(
                    ["JsonSerializer"],
                    row.Declaration.DeclaringType.Definition.Segments);
                Assert.Equal(
                    allRows.Binding.Subject,
                    row.AttachedReceiver);
                Assert.StartsWith(
                    "M:System.Text.Json.JsonSerializer.",
                    row.Declaration.Member.CanonicalSignature,
                    StringComparison.Ordinal);
            });
        Assert.DoesNotContain(
            nonExtensionRows.Items,
            row => row.Receiver is TypeMemberReceiver.Extension);
        Assert.Contains(
            allRows.Items,
            row => row.Receiver is TypeMemberReceiver.Static);
        Assert.Contains(
            allRows.Items,
            row => row.Receiver is TypeMemberReceiver.This);

        PortableQueryIntent headIntent = PortableQueryIntent.Create(
            [],
            [],
            [PortableQueryStage.Head(3)],
            []);
        TypeMembersOutcome.Rows headRows =
            Rows(
                TypeInspectionOperation.Execute(
                    authority,
                    context,
                    new(
                        subject,
                        TypeInspectionMembersQuery.CreateRequest(
                            QuerySpaceTerminalRequirement.Rows,
                            headIntent))));
        TypeMembersOutcome.Count headCount =
            Count(
                TypeInspectionOperation.Execute(
                    authority,
                    context,
                    new(
                        subject,
                        TypeInspectionMembersQuery.CreateRequest(
                            QuerySpaceTerminalRequirement.Count,
                            headIntent))));
        Assert.Equal(3, headRows.Items.Length);
        Assert.Equal(headRows.Items.Length, headCount.Value);
        Assert.Equal(headRows.Binding, headCount.Binding);

        PortableQueryIntent invalidWindow = PortableQueryIntent.Create(
            [],
            [],
            [PortableQueryStage.Window(1000, 1001)],
            []);
        TypeInspectionDocument rejectedDocument =
            Assert.IsType<TypeInspectionContent.Completed>(
                TypeInspectionOperation.Execute(
                    authority,
                    context,
                    new(
                        subject,
                        TypeInspectionMembersQuery.CreateRequest(
                            QuerySpaceTerminalRequirement.Rows,
                            invalidWindow)))
                .Content)
            .Document;
        TypeMembersSelectionFailure selectionFailure =
            Assert.IsType<TypeMembersOutcome.Rejected>(
                rejectedDocument.Members)
            .Failure;
        Assert.Equal(62, selectionFailure.AvailableCount);

        TypeMembersOutcome.Rows declaringExtensionRows =
            Rows(
                TypeInspectionOperation.Execute(
                    authority,
                    context,
                    new(
                        new(
                            "System.Text.Json.JsonSerializer"),
                        TypeInspectionMembersQuery.CreateRequest(
                            QuerySpaceTerminalRequirement.Rows,
                            extensionIntent))));
        Assert.NotEmpty(declaringExtensionRows.Items);
        Assert.All(
            declaringExtensionRows.Items,
            row =>
            {
                Assert.Equal(
                    TypeMemberReceiver.Extension,
                    row.Receiver);
                Assert.Equal(
                    TypeMemberRowKind.Declared,
                    row.RowKind);
                Assert.Null(row.AttachedReceiver);
            });

        TypeInspectionDocument notRequested =
            Assert.IsType<TypeInspectionContent.Completed>(
                TypeInspectionOperation.Execute(
                    authority,
                    context,
                    new(subject))
                .Content)
            .Document;
        Assert.IsType<TypeMembersOutcome.NotRequested>(
            notRequested.Members);

        string json = JsonSerializer.Serialize(
            allRowsEnvelope,
            TypeInspectionJsonContext.Default
                .InspectionEnvelopeTypeInspectionContent);
        using JsonDocument serialized = JsonDocument.Parse(json);
        Assert.Equal(
            "completed",
            serialized.RootElement
                .GetProperty("content")
                .GetProperty("kind")
                .GetString());
        Assert.Equal(
            allRows.Items.Length,
            serialized.RootElement
                .GetProperty("content")
                .GetProperty("document")
                .GetProperty("members")
                .GetProperty("items")
                .GetArrayLength());
    }

    [Fact]
    public async Task TypeInspection_SubjectNonSuccessDoesNotBecomeEmptyRows()
    {
        var store = new InMemoryPackageStore();
        await CommitPackageAsync(
            store,
            PackageId,
            ("lib/net11.0/Types.dll",
                BuildAssembly(
                    "Types",
                    "Available.Type",
                    typeof(IDisposable))));
        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextInput input = SelectedContextInput(PackageId);
        await using var coordinator = new WorkspaceReplacementCoordinator();
        (WorkspaceRealizationCandidate candidate,
            WorkspaceDeclarationContext context) =
            await PrepareSelectedContextCandidateAsync(
                coordinator,
                input,
                LoadOptions(client, store));
        await ActivateAsync(coordinator, candidate);
        using WorkspaceRealizationOperationLease authority =
            await AdmitAsync(coordinator);

        InspectionEnvelope<TypeInspectionContent> envelope =
            TypeInspectionOperation.Execute(
                authority,
                context,
                new(
                    new("Missing.Type"),
                    TypeInspectionMembersQuery.CreateRequest(
                        QuerySpaceTerminalRequirement.Rows)));

        TypeInspectionDocument document =
            Assert.IsType<TypeInspectionContent.Completed>(
                envelope.Content)
            .Document;
        Assert.Equal(
            ExactTypeInspectionOutcome.NotFound,
            document.Subject.Inspection.Outcome);
        Assert.Equal(
            TypeMembersUnavailableReason.SubjectUnavailable,
            Assert.IsType<TypeMembersOutcome.Unavailable>(
                document.Members)
            .Reason);
    }

    [Fact]
    public void TypeInspectionEnvelope_CarriesNoLiveAuthorityOrContent()
    {
        var visited = new HashSet<Type>();
        Visit(typeof(InspectionEnvelope<TypeInspectionContent>));

        void Visit(Type type)
        {
            if (!visited.Add(type)
                || type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(Guid)
                || type == typeof(Version))
            {
                return;
            }

            Assert.False(typeof(Stream).IsAssignableFrom(type));
            Assert.False(typeof(Delegate).IsAssignableFrom(type));
            Assert.False(typeof(InspectionWorkspace).IsAssignableFrom(type));
            Assert.False(
                typeof(WorkspaceRealizationOperationLease)
                    .IsAssignableFrom(type));
            Assert.False(
                typeof(AssemblyContextGroup).IsAssignableFrom(type));
            Assert.False(type.IsByRefLike);
            foreach (Type argument in type.GetGenericArguments())
                Visit(argument);
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain(
                    "Path",
                    property.Name,
                    StringComparison.OrdinalIgnoreCase);
                Visit(property.PropertyType);
            }
        }
    }

    private static TypeMembersOutcome.Rows Rows(
        InspectionEnvelope<TypeInspectionContent> envelope)
    {
        Assert.DoesNotContain(
            envelope.Diagnostics,
            diagnostic => diagnostic.Severity
                is InspectionDiagnosticSeverity.Error);
        return Assert.IsType<TypeMembersOutcome.Rows>(
            Assert.IsType<TypeInspectionContent.Completed>(
                envelope.Content)
            .Document.Members);
    }

    private static TypeMembersOutcome.Count Count(
        InspectionEnvelope<TypeInspectionContent> envelope)
    {
        Assert.DoesNotContain(
            envelope.Diagnostics,
            diagnostic => diagnostic.Severity
                is InspectionDiagnosticSeverity.Error);
        return Assert.IsType<TypeMembersOutcome.Count>(
            Assert.IsType<TypeInspectionContent.Completed>(
                envelope.Content)
            .Document.Members);
    }
}
