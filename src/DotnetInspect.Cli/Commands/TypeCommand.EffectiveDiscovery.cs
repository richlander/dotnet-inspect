using System.Reflection;

using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspect.Cli.Commands;

public static partial class TypeCommand
{
    static int? TryExecuteDirectLibraryEffectiveDiscovery(
        TypeOptions options,
        SectionPipeline<ApiType> memberPipeline,
        CancellationToken cancellationToken)
    {
        if (!CanExecuteDirectLibraryEffectiveDiscovery(
                options,
                out string assemblyPath,
                out MetadataTypeDefinitionName typeName))
        {
            return null;
        }

        bool requiresMemberApplicability =
            RequiresMemberApplicability(options);
        TypeMemberGroupPopulationRequest? declarations =
            requiresMemberApplicability
                ? new(
                    count: null,
                    selectorCounts: new(),
                    accessibility: options.IncludeAll
                        ? TypeMemberGroupAccessibilityFilter.All
                        : TypeMemberGroupAccessibilityFilter.Public,
                    includeHidden: options.IncludeAll)
                : null;
        var inspectionPlan = new TypeDocumentInspectionPlan(
            typeName,
            DirectLibraryInspectionCommand.s_bounds,
            declarations);
        ResolvedAssemblyReference assembly;
        try
        {
            assembly = ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local(
                    "direct Library Type discovery"));
        }
        catch (Exception failure) when (
            failure is IOException
                or UnauthorizedAccessException
                or BadImageFormatException)
        {
            return null;
        }
        TypeDocumentExtensionPresenceInspectionResult? combined =
            requiresMemberApplicability
                ? TypeDocumentInspectionOperation
                    .ExecuteWithExtensionPresence(
                        assembly,
                        inspectionPlan,
                        cancellationToken)
                : null;
        InspectionEnvelope<TypeDocumentInspectionOutcome> envelope =
            combined?.Document
            ?? TypeDocumentInspectionOperation.Execute(
                assembly,
                inspectionPlan,
                cancellationToken);
        if (envelope.Content
            is TypeDocumentInspectionOutcome.Rejected
            {
                Reason:
                    TypeDocumentInspectionRejection.TypeNotFound
                    or TypeDocumentInspectionRejection.TypeAmbiguous,
            })
        {
            return null;
        }
        if (envelope.Content
            is not TypeDocumentInspectionOutcome.Available available)
        {
            WriteInspectionDiagnostics(envelope.Diagnostics);
            CommandError.Write(
                "The exact Type discovery inspection did not complete.");
            return 1;
        }
        if (!CanProjectDirectLibraryDiscovery(
                available.Document.Subject))
        {
            return null;
        }
        TypeMemberSelectorCounts? selectorCounts =
            available.Document.Declarations switch
            {
                TypeDocumentDeclarations.Available declarationsAvailable
                    when requiresMemberApplicability =>
                        declarationsAvailable.Population.SelectorCounts
                        ?? throw new InvalidOperationException(
                            "Exact Type discovery did not return selector Counts."),
                TypeDocumentDeclarations.NotRequested
                    when !requiresMemberApplicability =>
                        EmptySelectorCounts(),
                _ => null,
            };
        if (selectorCounts is null)
        {
            WriteInspectionDiagnostics(envelope.Diagnostics);
            CommandError.Write(
                "The exact Type discovery Member-group inspection did not complete.");
            return 1;
        }

        bool hasExtensionMethods = false;
        if (requiresMemberApplicability)
        {
            TypeExtensionMethodPresenceInspectionOutcome extensionPresence =
                combined!.ExtensionPresence;
            if (extensionPresence
                is not TypeExtensionMethodPresenceInspectionOutcome
                    .Available availablePresence)
            {
                return null;
            }
            hasExtensionMethods = availablePresence.Exists;
        }

        WriteInspectionDiagnostics(envelope.Diagnostics);
        ApiType type = ProjectDirectLibraryDiscoveryType(
            available.Document.Subject,
            available.Document.BaseKind,
            available.Document.InterfaceCount,
            selectorCounts,
            hasExtensionMethods,
            assemblyPath);
        TypeOptions effectiveOptions = options with
        {
            DllPath = assemblyPath,
        };
        var acquisition = new ApiCommand.TypeAcquisitionContext(
            assemblyPath,
            null,
            null,
            assemblyPath,
            null);
        return ApiCommand.ExecuteEffectiveDiscovery(
            type,
            memberPipeline,
            effectiveOptions,
            acquisition,
            DirectDiscoverySchema(memberPipeline),
            DirectDiscoveryManifest(type, acquisition));
    }

    static bool RequiresMemberApplicability(TypeOptions options) =>
        options.Discover is null or { Length: 0 };

    static DocumentSchema DirectDiscoverySchema(
        SectionPipeline<ApiType> memberPipeline)
    {
        DocumentSchema schema =
            ApiViewContext.Default
                .GetSchemaInfo<TypeView>()!
                .ToDocumentSchema();
        foreach (string section in memberPipeline.SelectableSectionNames)
        {
            if (schema.GetSection(section) is null)
                schema.AddSection(section);
        }
        return schema;
    }

    static RenderedSectionManifest DirectDiscoveryManifest(
        ApiType type,
        ApiCommand.TypeAcquisitionContext acquisition)
    {
        var manifest = new RenderedSectionManifest();
        manifest.RecordTable(
            SectionNames.TypeInfo,
            ["Field", "Value"]);
        manifest.RecordField(SectionNames.TypeInfo, "Type");
        manifest.RecordField(SectionNames.TypeInfo, "Kind");
        if (type.IsStatic
            || type.Kind == "class"
                && (type.IsAbstract || type.IsSealed)
            || type.Kind == "struct"
                && (type.IsReadOnly || type.IsByRefLike))
        {
            manifest.RecordField(
                SectionNames.TypeInfo,
                "Modifiers");
        }
        if (type.BaseType is not null)
            manifest.RecordField(SectionNames.TypeInfo, "Base");
        if (type.TypeParameters.Count > 0)
        {
            manifest.RecordField(
                SectionNames.TypeInfo,
                "Type Parameters");
        }
        if (type.Interfaces.Count > 0)
            manifest.RecordField(SectionNames.TypeInfo, "Interfaces");
        if (acquisition.FoundIn is not null)
            manifest.RecordField(SectionNames.TypeInfo, "Library");
        if (acquisition.PackageName is not null)
            manifest.RecordField(SectionNames.TypeInfo, "Package");
        if (acquisition.PackageVersion is not null)
            manifest.RecordField(SectionNames.TypeInfo, "Version");
        if (acquisition.SelectedTfm is not null)
            manifest.RecordField(SectionNames.TypeInfo, "TFM");
        if (acquisition.ApiSource is not null)
            manifest.RecordField(SectionNames.TypeInfo, "Source");
        return manifest;
    }

    static TypeMemberSelectorCounts EmptySelectorCounts() =>
        new(
            [],
            new(
                All: 0,
                Static: 0,
                Instance: 0,
                Virtual: 0,
                Interface: 0,
                Extensions: 0));

    static bool CanExecuteDirectLibraryEffectiveDiscovery(
        TypeOptions options,
        out string assemblyPath,
        out MetadataTypeDefinitionName typeName)
    {
        assemblyPath = options.AssemblyPath ?? "";
        typeName = null!;
        if (!options.EffectiveDiscovery
            || options.EnvelopeOutput
            || options.DiscoverDeferredToListing
            || options.TypeFilter is not null
            || options.MemberFilter.Count > 0
            || options.KindFilter.Count > 0
            || options.UnsafeOnly
            || options.BodyKindQuery.HasFilter
            || options.CloneCandidateQuery.HasPredicates
            || options.Discover is { Length: > 0 }
                && options.Discover.Any(
                    section => !section.Equals(
                        SectionNames.TypeInfo,
                        StringComparison.OrdinalIgnoreCase))
            || string.IsNullOrWhiteSpace(options.TypeName)
            || string.IsNullOrWhiteSpace(assemblyPath)
            || !File.Exists(assemblyPath))
        {
            return false;
        }

        if (MetadataTypeDefinitionName.ParseSerialized(options.TypeName)
                is not MetadataTypeDefinitionNameResult.Valid valid
            || valid.Name.Namespace.Length == 0)
        {
            return false;
        }

        typeName = valid.Name;
        return true;
    }

    static bool CanProjectDirectLibraryDiscovery(TypeSubject subject) =>
        subject.Category
            is MetadataTypeDeclarationCategory.Class
                or MetadataTypeDeclarationCategory.Interface
                or MetadataTypeDeclarationCategory.Delegate;

    static ApiType ProjectDirectLibraryDiscoveryType(
        TypeSubject subject,
        MetadataTypeDeclarationBaseKind baseKind,
        int interfaceCount,
        TypeMemberSelectorCounts selectorCounts,
        bool hasExtensionMethods,
        string assemblyPath)
    {
        MetadataTypeDefinitionName typeName = subject.Type;
        TypeAttributes attributes = subject.Attributes;
        bool isAbstract =
            (attributes & TypeAttributes.Abstract) != 0;
        bool isSealed =
            (attributes & TypeAttributes.Sealed) != 0;
        string kind = subject.Category switch
        {
            MetadataTypeDeclarationCategory.Class => "class",
            MetadataTypeDeclarationCategory.Interface => "interface",
            MetadataTypeDeclarationCategory.Delegate => "delegate",
            _ => throw new InvalidOperationException(
                "Unsupported exact Type discovery declaration category."),
        };
        return new()
        {
            Namespace = typeName.Namespace.Length == 0
                ? null
                : typeName.Namespace,
            Name = string.Join(".", typeName.Segments),
            MetadataName = typeName.ToNestedMetadataName(),
            DefinitionName = typeName,
            IntroducedTypeParameterCounts =
                IntroducedTypeParameterCounts(subject),
            MetadataToken = subject.TypeDefinitionToken,
            Accessibility = TypeAccessibility(attributes),
            Kind = kind,
            IsAbstract = isAbstract,
            IsSealed = isSealed,
            IsStatic =
                kind == "class"
                && isAbstract
                && isSealed,
            IsByRefLike = subject.IsByRefLike,
            BaseType =
                baseKind
                    is MetadataTypeDeclarationBaseKind.Other
                    ? "<base>"
                    : null,
            Interfaces =
                interfaceCount > 0
                    ? ["<interface>"]
                    : [],
            TypeParameters =
            [
                .. subject.Signature.GenericParameters.Select(
                    parameter => new TypeParameter
                    {
                        Name = parameter.Name.ToString(),
                    }),
            ],
            Members = DiscoveryMembers(
                selectorCounts,
                hasExtensionMethods),
            SourceAssemblyPath = assemblyPath,
        };
    }

    static List<int> IntroducedTypeParameterCounts(
        TypeSubject subject)
    {
        var counts = new int[subject.Type.Segments.Length];
        foreach (TypeDocumentGenericParameter parameter
            in subject.Signature.GenericParameters)
        {
            counts[parameter.DefinitionSegmentIndex] =
                checked(
                    counts[parameter.DefinitionSegmentIndex]
                    + 1);
        }
        return [.. counts];
    }

    static List<ApiMember> DiscoveryMembers(
        TypeMemberSelectorCounts selectorCounts,
        bool hasExtensionMethods)
    {
        List<ApiMember> members = [];
        foreach (TypeMemberKindCount count in selectorCounts.Kinds)
        {
            if (count.Count == 0)
                continue;

            string kind = count.Kind switch
            {
                MemberGroupCategory.Method => "method",
                MemberGroupCategory.Constructor => "constructor",
                MemberGroupCategory.Operator => "operator",
                MemberGroupCategory.Finalizer => "finalizer",
                MemberGroupCategory.ExplicitInterfaceImplementation =>
                    "explicit-interface-implementation",
                MemberGroupCategory.Property => "property",
                MemberGroupCategory.Field => "field",
                MemberGroupCategory.Event => "event",
                _ => throw new InvalidOperationException(
                    "Unknown exact Type discovery Member-group category."),
            };
            members.Add(
                new ApiMember
                {
                    Name = "<member>",
                    Kind = kind,
                    Signature = "<member>",
                    Attributes = ["<attribute>"],
                    IsFinalizer =
                        count.Kind
                            == MemberGroupCategory.Finalizer,
                    IsExplicitInterfaceImplementation =
                        count.Kind
                            == MemberGroupCategory
                                .ExplicitInterfaceImplementation,
                });
        }
        if (hasExtensionMethods)
        {
            members.Add(
                new ApiMember
                {
                    Name = "<extension>",
                    Kind = "extension-method",
                    Signature = "<extension>",
                    IsExtension = true,
                });
        }
        return members;
    }

    static string? TypeAccessibility(TypeAttributes attributes) =>
        (attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public or TypeAttributes.NestedPublic =>
                null,
            TypeAttributes.NotPublic or TypeAttributes.NestedAssembly =>
                "internal",
            TypeAttributes.NestedPrivate =>
                "private",
            TypeAttributes.NestedFamily =>
                "protected",
            TypeAttributes.NestedFamANDAssem =>
                "private protected",
            TypeAttributes.NestedFamORAssem =>
                "protected internal",
            _ => null,
        };
}
