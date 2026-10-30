using System.Buffers;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;

using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Commands;

public static partial class TypeCommand
{
    static readonly ApiSurfaceExtractionBounds s_directExactTypeBounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

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
        bool requiresExtensionPresence =
            RequiresExtensionPresence(options);
        bool requiresCompleteTypeSignature =
            options.Discover?.Contains(
                SectionNames.TypeInfo,
                StringComparer.OrdinalIgnoreCase)
                == true;
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
        var inspectionPlan = new TypeSubjectInspectionPlan(
            typeName,
            s_directExactTypeBounds,
            declarations,
            new MetadataExactTypeInput(typeName),
            requiresCompleteTypeSignature
                ? TypeSubjectInspectionDemand.CompleteSignature
                : TypeSubjectInspectionDemand.Declaration);
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
        TypeSubjectExtensionPresenceInspectionResult? combined =
            requiresExtensionPresence
                ? TypeSubjectInspectionOperation
                    .ExecuteWithExtensionPresence(
                        assembly,
                        inspectionPlan,
                        options.IncludeAll,
                        cancellationToken)
                : null;
        InspectionEnvelope<TypeSubjectInspectionOutcome> envelope =
            combined?.Document
            ?? TypeSubjectInspectionOperation.Execute(
                assembly,
                inspectionPlan,
                cancellationToken);
        if (envelope.Content
            is TypeSubjectInspectionOutcome.Rejected
            {
                Reason:
                    TypeSubjectInspectionRejection.TypeNotFound
                    or TypeSubjectInspectionRejection.TypeAmbiguous,
            })
        {
            return null;
        }
        if (envelope.Content
            is not TypeSubjectInspectionOutcome.Available available)
        {
            WriteInspectionDiagnostics(envelope.Diagnostics);
            CommandError.Write(
                "The exact Type discovery inspection did not complete.");
            return 1;
        }
        // A degraded image (for example, incomplete root adjacency) keeps
        // the rich route's established disclosure.
        if (!envelope.Diagnostics.IsEmpty
            || !CanProjectDirectLibraryDiscovery(
                available.Document.Subject,
                options.IncludeAll))
        {
            return null;
        }
        TypeMemberSelectorCounts? selectorCounts =
            available.Document.Declarations switch
            {
                TypeSubjectMemberGroups.Available declarationsAvailable
                    when requiresMemberApplicability =>
                        declarationsAvailable.Population.SelectorCounts
                        ?? throw new InvalidOperationException(
                            "Exact Type discovery did not return selector Counts."),
                TypeSubjectMemberGroups.NotRequested
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
        if (requiresExtensionPresence)
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
            available.Document.CompleteSignature
                ?.NestedTypeCount
                ?? 0,
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
            DirectDiscoveryManifest(type, acquisition),
            // Minimal non-tree discovery lists categories, not their
            // members, so it never discloses Unsafe Members.
            skipBareUnsafeApplicability: true);
    }

    static int? TryExecuteDirectLibraryExactTypeInfo(
        TypeOptions options,
        Action<Action<IBufferWriter<byte>>>? writeUtf8,
        CancellationToken cancellationToken)
    {
        if (writeUtf8 is null
            || !CanExecuteDirectLibraryExactTypeInfo(
                options,
                out string assemblyPath,
                out MetadataTypeDefinitionName typeName))
        {
            return null;
        }

        var inspectionPlan = new TypeSignatureInspectionPlan(
            typeName,
            s_directExactTypeBounds,
            exactInput: new MetadataExactTypeInput(typeName));
        ResolvedAssemblyReference assembly;
        try
        {
            assembly = ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local(
                    "direct Library Type Info"));
        }
        catch (Exception failure) when (
            failure is IOException
                or UnauthorizedAccessException
                or BadImageFormatException)
        {
            return null;
        }

        // The Library row presents the descriptor's normalized path, as the
        // established renderer does, rather than the option as typed.
        if (assembly.Path is not { } libraryPath)
            return null;

        InspectionEnvelope<TypeSignatureInspectionOutcome> envelope =
            TypeSignatureInspectionOperation.Execute(
                assembly,
                inspectionPlan,
                cancellationToken);
        if (envelope.Content
            is TypeSignatureInspectionOutcome.Rejected rejected)
        {
            WriteInspectionDiagnostics(envelope.Diagnostics);
            CommandError.Write(
                rejected.Reason
                    is TypeSubjectInspectionRejection
                        .TypeAmbiguous
                    ? "The exact Type Info request matched more than one Type."
                    : "The exact Type Info Type was not found.");
            return 1;
        }
        if (envelope.Content
            is not TypeSignatureInspectionOutcome.Available available)
        {
            WriteInspectionDiagnostics(envelope.Diagnostics);
            CommandError.Write(
                "The exact Type Info inspection did not complete.");
            return 1;
        }

        TypeSignature signature = available.Signature;
        TypeSubject subject = signature.Subject;
        if (!CanProjectDirectLibraryTypeInfo(
                signature,
                options.IncludeAll))
        {
            WriteInspectionDiagnostics(envelope.Diagnostics);
            CommandError.Write(
                "The exact Type Info Type is outside the selected API scope.");
            return 1;
        }

        string kind = TypeKind(subject);
        TypeAttributes attributes = subject.Attributes;
        var modifierBasis = new ApiType
        {
            Kind = kind,
            IsAbstract =
                (attributes & TypeAttributes.Abstract) != 0,
            IsSealed =
                (attributes & TypeAttributes.Sealed) != 0,
            IsStatic =
                kind == "class"
                && (attributes & TypeAttributes.Abstract) != 0
                && (attributes & TypeAttributes.Sealed) != 0,
            IsByRefLike = subject.IsByRefLike,
            IsReadOnly = subject.IsReadOnly,
        };
        IReadOnlyList<string> modifiers =
            ILInspector.Research.ResearchViews.TypeModifiers(
                modifierBasis);
        var presentation = new ExactTypeInfoTsvDocument(
            signature,
            kind,
            modifiers,
            libraryPath,
            SourceKind.Library);

        WriteInspectionDiagnostics(envelope.Diagnostics);
        writeUtf8(
            output => ExactTypeInfoTsvPresentation.Write(
                output,
                presentation,
                showHeader: !options.NoHeader));
        return envelope.Diagnostics.Any(
            static diagnostic =>
                diagnostic.Severity
                    == InspectionDiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    static bool RequiresMemberApplicability(TypeOptions options) =>
        options.Discover is null or { Length: 0 }
        || RequiresAuditMemberApplicability(options);

    // Same-image extension methods are body-backed Members of the rich
    // ApiType, so every Member-applicability request needs their presence.
    static bool RequiresExtensionPresence(TypeOptions options) =>
        RequiresMemberApplicability(options);

    static bool RequiresAuditMemberApplicability(
        TypeOptions options) =>
        options.Discover?.Any(
            section =>
                section.Equals(
                    SectionCategoryNames.Audit,
                    StringComparison.OrdinalIgnoreCase)
                || section.Equals(
                    SectionNames.SafetyFacts,
                    StringComparison.OrdinalIgnoreCase)
                || section.Equals(
                    SectionNames.UnsafeMembers,
                    StringComparison.OrdinalIgnoreCase)) == true;

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
        if (ILInspector.Research.ResearchViews
                .TypeModifiers(type).Count > 0)
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
        if (type.NestedTypeCount > 0)
        {
            manifest.RecordField(
                SectionNames.TypeInfo,
                "Nested Types");
        }
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
                BodyBacked: 0,
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
        assemblyPath = "";
        typeName = null!;
        if (!options.EffectiveDiscovery
            || options.EnvelopeOutput
            || options.DiscoverDeferredToListing
            || options.SelectDeferredToListing
            || options.PackagePath is not null
            || options.PackageRangeAddress is not null
            || options.PlatformAssembly is not null
            || options.ProjectPath is not null
            || options.ProjectAssetsPath is not null
            || options.UserVerbosity != Verbosity.Minimal
            || options.Tree
            || options.IncludeSections is { Count: > 0 }
            || options.TypeFilter is not null
            || options.MemberFilter.Count > 0
            || options.KindFilter.Count > 0
            || options.UnsafeOnly
            || options.BodyKindQuery.HasFilter
            || options.CloneCandidateQuery.HasPredicates
            || options.Discover is { Length: > 0 }
                && options.Discover.Any(
                    section =>
                        !section.Equals(
                            SectionNames.TypeInfo,
                            StringComparison.OrdinalIgnoreCase)
                        && !section.Equals(
                            SectionCategoryNames.Audit,
                            StringComparison.OrdinalIgnoreCase)
                        && !section.Equals(
                            SectionNames.SafetyFacts,
                            StringComparison.OrdinalIgnoreCase)
                        && !section.Equals(
                            SectionNames.UnsafeMembers,
                            StringComparison.OrdinalIgnoreCase))
            || !TryGetDirectLibraryExactType(
                options,
                out assemblyPath,
                out typeName))
        {
            return false;
        }

        return true;
    }

    static bool CanExecuteDirectLibraryExactTypeInfo(
        TypeOptions options,
        out string assemblyPath,
        out MetadataTypeDefinitionName typeName)
    {
        if (options.EnvelopeOutput
            || options.EffectiveDiscovery
            || options.Discover is not null
            || options.SelectDeferredToListing
            || options.DiscoverDeferredToListing
            || options.UserVerbosity != Verbosity.Minimal
            || !options.Tsv
            || options.JsonOutput
            || options.Jsonl
            || options.PlainText
            || options.MarkdownExplicitlySet
            || options.Tree
            || options.ShapeOutput
            || options.Count
            || options.Print
            || options.Value
            || options.Urls
            || options.Paths
            || options.LineWindowExplicitlySet
            || options.Rows is not null
            || options.Limit is not null
            || options.Columns is { Length: > 0 }
            || options.Fields is { Length: > 0 }
            || options.IncludeSections is not { Count: 1 } sections
            || !sections.Contains(SectionNames.TypeInfo)
            || options.TypeFilter is not null
            || options.MemberFilter.Count > 0
            || options.KindFilter.Count > 0
            || options.UnsafeOnly
            || options.BodyKindQuery.HasFilter
            || options.CloneCandidateQuery.HasPredicates
            || options.ShowDocs
            || options.ShowSamples
            || options.CompanionOutput != CompanionOutput.None
            || !TryGetDirectLibraryExactType(
                options,
                out assemblyPath,
                out typeName))
        {
            assemblyPath = "";
            typeName = null!;
            return false;
        }

        return true;
    }

    static bool TryGetDirectLibraryExactType(
        TypeOptions options,
        out string assemblyPath,
        out MetadataTypeDefinitionName typeName)
    {
        assemblyPath = options.AssemblyPath ?? "";
        typeName = null!;
        if (options.PackagePath is not null
            || options.PackageRangeAddress is not null
            || options.PlatformAssembly is not null
            || options.ProjectPath is not null
            || options.ProjectAssetsPath is not null
            || string.IsNullOrWhiteSpace(options.TypeName)
            || string.IsNullOrWhiteSpace(assemblyPath)
            || !File.Exists(assemblyPath))
        {
            return false;
        }

        // Reflection grammar treats C# generic notation as a literal name;
        // the rich route owns that query even when it round-trips.
        if (options.TypeName.Contains('<')
            || MetadataTypeDefinitionName.ParseSerialized(options.TypeName)
                is not MetadataTypeDefinitionNameResult.Valid valid
            || valid.Name.Namespace.Length == 0
            || !string.Equals(
                valid.Name.ToEscapedFullName(),
                options.TypeName,
                StringComparison.Ordinal))
        {
            return false;
        }

        typeName = valid.Name;
        return true;
    }

    static bool CanProjectDirectLibraryDiscovery(
        TypeSubject subject,
        bool includeAll) =>
        subject.Category
            is MetadataTypeDeclarationCategory.Class
                or MetadataTypeDeclarationCategory.Interface
                or MetadataTypeDeclarationCategory.Delegate
        && !subject.IsCompilerGenerated
        && (includeAll
            || !subject.IsHidden
                && subject.DeclaringTypeDefinitionToken is null
                && (subject.Attributes
                    & TypeAttributes.VisibilityMask)
                    == TypeAttributes.Public);

    static bool CanProjectDirectLibraryTypeInfo(
        TypeSignature signature,
        bool includeAll)
    {
        TypeSubject subject = signature.Subject;
        return
        !subject.IsCompilerGenerated
        && (includeAll
            || !subject.IsHidden
                && signature
                    .AreEnclosingTypesExternallyReachable
                && IsPublicTypeVisibility(
                    subject.Attributes
                        & TypeAttributes.VisibilityMask));
    }

    static bool IsPublicTypeVisibility(
        TypeAttributes visibility) =>
        visibility
            is TypeAttributes.Public
                or TypeAttributes.NestedPublic
                or TypeAttributes.NestedFamily
                or TypeAttributes.NestedFamORAssem;

    static ApiType ProjectDirectLibraryDiscoveryType(
        TypeSubject subject,
        MetadataTypeDeclarationBaseKind baseKind,
        int interfaceCount,
        int nestedTypeCount,
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
        string kind = TypeKind(subject, baseKind);
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
            NestedTypeCount = nestedTypeCount,
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

    static string TypeKind(
        TypeSubject subject,
        MetadataTypeDeclarationBaseKind baseKind)
    {
        if (subject.Category
            is MetadataTypeDeclarationCategory.Interface)
        {
            return "interface";
        }
        if (subject.Category
            is MetadataTypeDeclarationCategory.Delegate)
        {
            return "delegate";
        }
        if (baseKind
            is MetadataTypeDeclarationBaseKind.Enum)
        {
            return "enum";
        }
        if (baseKind
            is MetadataTypeDeclarationBaseKind.ValueType)
        {
            return "struct";
        }
        if (subject.DefinesCoreLibraryRoot
            && subject.Type.ToMetadataFullName()
                == "System.MulticastDelegate")
        {
            return "delegate";
        }
        return subject.Category switch
        {
            MetadataTypeDeclarationCategory.Class => "class",
            _ => throw new InvalidOperationException(
                "Unsupported exact Type discovery declaration category."),
        };
    }

    static string TypeKind(TypeSubject subject) =>
        subject.DefinesCoreLibraryRoot
            && subject.Type.ToMetadataFullName()
                == "System.MulticastDelegate"
            ? "delegate"
            : subject.Category switch
        {
            MetadataTypeDeclarationCategory.Class => "class",
            MetadataTypeDeclarationCategory.Interface =>
                "interface",
            MetadataTypeDeclarationCategory.Struct => "struct",
            MetadataTypeDeclarationCategory.Enum => "enum",
            MetadataTypeDeclarationCategory.Delegate =>
                "delegate",
            _ => throw new InvalidOperationException(
                "Unsupported exact Type declaration category."),
        };

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
        int methodLikeCount = 0;
        ApiMember? accessorBackedPlaceholder = null;
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
            var member =
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
                };
            members.Add(member);
            if (ApiMemberSectionDescriptors.IsMethodLike(kind))
            {
                methodLikeCount =
                    checked(methodLikeCount + count.Count);
            }
            else if (accessorBackedPlaceholder is null
                && count.Kind
                    is MemberGroupCategory.Property
                        or MemberGroupCategory.Event)
            {
                accessorBackedPlaceholder = member;
            }
        }
        if (selectorCounts.Traits.BodyBacked > methodLikeCount)
        {
            if (accessorBackedPlaceholder is null)
            {
                throw new InvalidOperationException(
                    "Exact Type discovery reported accessor-backed Members without a property or event population.");
            }
            // This applicability-only Type needs accessor presence, not identity.
            if (accessorBackedPlaceholder.Kind == "property")
            {
                accessorBackedPlaceholder.GetterToken =
                    MetadataTokens.GetToken(
                        MetadataTokens.MethodDefinitionHandle(1));
            }
            else
            {
                accessorBackedPlaceholder.AdderToken =
                    MetadataTokens.GetToken(
                        MetadataTokens.MethodDefinitionHandle(1));
            }
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
