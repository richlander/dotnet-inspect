using ILInspector.Decompiler;
using ILInspector.Metadata;
using Inspector.Findings;
using ILInspector.Research;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using Markout;
using System.Collections.Immutable;
using System.Text.Json;
using InertText;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Tests;

public partial class SectionPipelineTests
{
    [Fact]
    public void LibraryReferencesSection_DemandsTypedAssemblyReferencesQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        HashSet<string> references =
            new(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.References,
            };

        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            references);

        Assert.Equal([AssemblyReferencesQuery.Definition], required);
    }

    [Fact]
    public void LibraryInfoAndEcosystemDependenciesShareAssemblyReferencesQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        string[] boundSections = pipeline.QueryBoundSections
            .Where(binding => ReferenceEquals(
                binding.Query,
                AssemblyReferencesQuery.Definition))
            .Select(binding => binding.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains(SectionNames.LibraryInfo, boundSections);
        Assert.Contains(SectionNames.EcosystemDependencies, boundSections);
        HashSet<InspectionQueryDefinition> required =
            pipeline.GetRequiredQueries(
                Verbosity.Minimal,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    SectionNames.LibraryInfo,
                    SectionNames.EcosystemDependencies,
                });
        Assert.Single(
            required,
            query => ReferenceEquals(
                query,
                AssemblyReferencesQuery.Definition));
    }

    [Fact]
    public void LibraryIdentifierConfusionSection_DemandsTypedAssemblyReferencesQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        HashSet<string> identifierAudit =
            new(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.IdentifierConfusion,
            };

        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            identifierAudit);

        Assert.Equal([AssemblyReferencesQuery.Definition], required);
    }

    [Fact]
    public void AssemblyReferencesQuery_ReturnsDirectReferencesFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);

        var result = Assert.IsType<AssemblyReferencesResult.Available>(
            AssemblyReferencesQuery.Execute(session));

        Assert.Equal(
            session.AssemblyReferenceIdentities().OrderBy(reference => reference.Name),
            result.Identities.OrderBy(reference => reference.Name));
    }

    [Fact]
    public void LibraryInfoAndExtensionMethodsSections_ShareTypedExtensionMethodsQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        string[] boundSections = pipeline.QueryBoundSections
            .Where(binding => ReferenceEquals(
                binding.Query,
                ExtensionMethodsQuery.Definition))
            .Select(binding => binding.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> sections =
            new(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.LibraryInfo,
                SectionNames.ExtensionMethods,
            };

        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            sections);

        Assert.Equal(
            [SectionNames.ExtensionMethods, SectionNames.LibraryInfo],
            boundSections);
        Assert.Equal(
            [
                AssemblyReferencesQuery.Definition,
                ClassifiedMethodsQuery.Definition,
                CustomAttributesQuery.Definition,
                ExtensionMethodsQuery.Definition,
                ResourcesQuery.Definition,
                TypeForwardersQuery.Definition,
            ],
            required.OrderBy(query => query.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void LibraryInfoAndCustomAttributesSections_ShareTypedCustomAttributesQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        string[] boundSections = pipeline.QueryBoundSections
            .Where(binding => ReferenceEquals(
                binding.Query,
                CustomAttributesQuery.Definition))
            .Select(binding => binding.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> sections =
            new(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.LibraryInfo,
                SectionNames.CustomAttributes,
            };

        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            sections);

        Assert.Equal(
            [SectionNames.CustomAttributes, SectionNames.LibraryInfo],
            boundSections);
        Assert.Equal(
            [
                AssemblyReferencesQuery.Definition,
                ClassifiedMethodsQuery.Definition,
                CustomAttributesQuery.Definition,
                ExtensionMethodsQuery.Definition,
                ResourcesQuery.Definition,
                TypeForwardersQuery.Definition,
            ],
            required.OrderBy(query => query.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void LibraryInfoAndResourcesSections_ShareTypedResourcesQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        string[] boundSections = pipeline.QueryBoundSections
            .Where(binding => ReferenceEquals(
                binding.Query,
                ResourcesQuery.Definition))
            .Select(binding => binding.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> sections =
            new(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.LibraryInfo,
                SectionNames.Resources,
            };

        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            sections);

        Assert.Equal(
            [SectionNames.LibraryInfo, SectionNames.Resources],
            boundSections);
        Assert.Equal(
            [
                AssemblyReferencesQuery.Definition,
                ClassifiedMethodsQuery.Definition,
                CustomAttributesQuery.Definition,
                ExtensionMethodsQuery.Definition,
                ResourcesQuery.Definition,
                TypeForwardersQuery.Definition,
            ],
            required.OrderBy(query => query.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void LibraryInfoAndTypeForwardersSections_ShareTypedTypeForwardersQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        string[] boundSections = pipeline.QueryBoundSections
            .Where(binding => ReferenceEquals(
                binding.Query,
                TypeForwardersQuery.Definition))
            .Select(binding => binding.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> sections =
            new(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.LibraryInfo,
                SectionNames.TypeForwarders,
            };

        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            sections);

        Assert.Equal(
            [SectionNames.LibraryInfo, SectionNames.TypeForwarders],
            boundSections);
        Assert.Equal(
            [
                AssemblyReferencesQuery.Definition,
                ClassifiedMethodsQuery.Definition,
                CustomAttributesQuery.Definition,
                ExtensionMethodsQuery.Definition,
                ResourcesQuery.Definition,
                TypeForwardersQuery.Definition,
            ],
            required.OrderBy(query => query.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void TypeForwardersQuery_ReturnsMetadataOrderedForwardersFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(AssemblyInspectionSession).Assembly.Location);

        var result = Assert.IsType<TypeForwardersResult.Available>(
            TypeForwardersQuery.Execute(session));

        Assert.Contains(
            result.Forwarders,
            forwarder =>
                forwarder.TypeName == "ILInspector.Metadata.SignatureBlobGuard"
                && forwarder.TargetAssembly == "ILInspector.MetadataPrimitives");
        Assert.Equal(session.TypeForwarders(), result.Forwarders);
    }

    [Fact]
    public void TypeForwardersQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(AssemblyInspectionSession).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [TypeForwardersQuery.Definition],
            context);
        var forwarders = Assert.IsType<TypeForwardersResult.Available>(
            results.Get(TypeForwardersQuery.Definition));

        Assert.Contains(
            forwarders.Forwarders,
            forwarder => forwarder.TypeName == "ILInspector.Metadata.SignatureBlobGuard");
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void TypeForwardersQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [TypeForwardersQuery.Definition],
            context);
        var failure = Assert.IsType<TypeForwardersResult.Failed>(
            results.Get(TypeForwardersQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void TypeForwardersQuery_DisposedBorrowedSessionRemainsTyped()
    {
        using var lender = PdbContext.Open(
            typeof(AssemblyInspectionSession).Assembly.Location);
        var session = AssemblyInspectionSession.Borrow(lender);
        session.Dispose();

        var failure = Assert.IsType<TypeForwardersResult.Failed>(
            TypeForwardersQuery.Execute(session));

        Assert.IsType<ObjectDisposedException>(failure.Error);
    }

    [Fact]
    public void TypeForwardersQuery_RetainedImageFailureDoesNotReopenPath()
    {
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        string reopenCanary = typeof(AssemblyInspectionSession).Assembly.Location;
        using (var canarySession = AssemblyInspectionSession.Open(reopenCanary))
        {
            Assert.NotEmpty(canarySession.TypeForwarders());
        }

        using var context = new InspectionQueryContext
        {
            AssemblyPath = reopenCanary,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };
        metadataContext.Dispose();

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [TypeForwardersQuery.Definition],
            context);
        var failure = Assert.IsType<TypeForwardersResult.Failed>(
            results.Get(TypeForwardersQuery.Definition));

        Assert.IsType<ObjectDisposedException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void TypeForwardersQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<TypeForwardersResult.Failed>(
            TypeForwardersQuery.Execute(session));
        LibraryMetadataService.ApplyTypeForwardersResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<TypeForwarderInfo>.Failed>(
            model.TypeForwarderInspection!.Value);
        Assert.Null(model.TypeForwarders);
        Assert.Equal("Type Forwarders", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void UnionTypesQuery_ReturnsMetadataOrderedUnionsFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(SampleDiscoveredUnion).Assembly.Location);

        var result = Assert.IsType<UnionTypesResult.Available>(
            UnionTypesQuery.Execute(session));

        Assert.Contains(
            result.Unions,
            union => union.TypeName == typeof(SampleDiscoveredUnion).FullName);
        Assert.Equal(
            session.UnionTypes().Select(union =>
                (union.TypeName,
                    union.Kind,
                    union.ImplementsIUnion,
                    Cases: string.Join('\n', union.CaseTypes))),
            result.Unions.Select(union =>
                (union.TypeName,
                    union.Kind,
                    union.ImplementsIUnion,
                    Cases: string.Join('\n', union.CaseTypes))));
    }

    [Fact]
    public void UnionTypesQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(SampleDiscoveredUnion).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [UnionTypesQuery.Definition],
            context);
        var unions = Assert.IsType<UnionTypesResult.Available>(
            results.Get(UnionTypesQuery.Definition));

        Assert.Contains(
            unions.Unions,
            union => union.TypeName == typeof(SampleDiscoveredUnion).FullName);
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void UnionTypesQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [UnionTypesQuery.Definition],
            context);
        var failure = Assert.IsType<UnionTypesResult.Failed>(
            results.Get(UnionTypesQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void UnionTypesQuery_DisposedBorrowedSessionRemainsTyped()
    {
        using var lender = PdbContext.Open(
            typeof(SampleDiscoveredUnion).Assembly.Location);
        var session = AssemblyInspectionSession.Borrow(lender);
        session.Dispose();

        var failure = Assert.IsType<UnionTypesResult.Failed>(
            UnionTypesQuery.Execute(session));

        Assert.IsType<ObjectDisposedException>(failure.Error);
    }

    [Fact]
    public void UnionTypesQuery_RetainedImageFailureDoesNotReopenPath()
    {
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        string reopenCanary = typeof(SampleDiscoveredUnion).Assembly.Location;
        using (var canarySession = AssemblyInspectionSession.Open(reopenCanary))
        {
            Assert.Contains(
                canarySession.UnionTypes(),
                union => union.TypeName == typeof(SampleDiscoveredUnion).FullName);
        }

        using var context = new InspectionQueryContext
        {
            AssemblyPath = reopenCanary,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };
        metadataContext.Dispose();

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [UnionTypesQuery.Definition],
            context);
        var failure = Assert.IsType<UnionTypesResult.Failed>(
            results.Get(UnionTypesQuery.Definition));

        Assert.IsType<ObjectDisposedException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void UnionTypesQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<UnionTypesResult.Failed>(
            UnionTypesQuery.Execute(session));
        LibraryMetadataService.ApplyUnionTypesResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<UnionTypeInfo>.Failed>(
            model.UnionTypeInspection!.Value);
        Assert.Null(model.UnionTypes);
        Assert.Equal("Union Types", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void ClassifiedMethodsQuery_ReturnsMetadataOrderedMethodsFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath());

        var result = Assert.IsType<ClassifiedMethodsResult.Available>(
            ClassifiedMethodsQuery.Execute(session));

        Assert.Contains(
            result.Methods,
            method => method.MethodName == "PointerNoneMethod"
                && method.Classification == MethodClassification.Unsafe);
        Assert.Equal(session.ClassifiedMethods(), result.Methods);
    }

    [Fact]
    public void ClassifiedMethodsQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath());
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ClassifiedMethodsQuery.Definition],
            context);
        var methods = Assert.IsType<ClassifiedMethodsResult.Available>(
            results.Get(ClassifiedMethodsQuery.Definition));

        Assert.Contains(
            methods.Methods,
            method => method.MethodName == "PointerNoneMethod");
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void ClassifiedMethodsQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ClassifiedMethodsQuery.Definition],
            context);
        var failure = Assert.IsType<ClassifiedMethodsResult.Failed>(
            results.Get(ClassifiedMethodsQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void ClassifiedMethodsQuery_DisposedBorrowedSessionRemainsTyped()
    {
        using var lender = PdbContext.Open(
            typeof(SampleUnsafeClass).Assembly.Location);
        var session = AssemblyInspectionSession.Borrow(lender);
        session.Dispose();

        var failure = Assert.IsType<ClassifiedMethodsResult.Failed>(
            ClassifiedMethodsQuery.Execute(session));

        Assert.IsType<ObjectDisposedException>(failure.Error);
    }

    [Fact]
    public void ClassifiedMethodsQuery_RetainedImageFailureDoesNotReopenPath()
    {
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        string reopenCanary = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        using (var canarySession = AssemblyInspectionSession.Open(reopenCanary))
        {
            var canary = Assert.IsType<ClassifiedMethodsResult.Available>(
                ClassifiedMethodsQuery.Execute(canarySession));
            Assert.Contains(
                canary.Methods,
                method => method.MethodName == "PointerNoneMethod");
        }

        using var context = new InspectionQueryContext
        {
            AssemblyPath = reopenCanary,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };
        metadataContext.Dispose();

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ClassifiedMethodsQuery.Definition],
            context);
        var failure = Assert.IsType<ClassifiedMethodsResult.Failed>(
            results.Get(ClassifiedMethodsQuery.Definition));

        Assert.IsType<ObjectDisposedException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void ClassifiedMethodsQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<ClassifiedMethodsResult.Failed>(
            ClassifiedMethodsQuery.Execute(session));
        LibraryMetadataService.ApplyClassifiedMethodsResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<ClassifiedMethodObservation>.Failed>(
            model.ClassifiedMethodInspection!.Value);
        Assert.Null(model.PInvokeMethods);
        Assert.Null(model.AsyncMethods);
        Assert.Equal("Classified Methods", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void AuditMetadataQuery_ReturnsFactsFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(MethodClassificationScannerTests).Assembly.Location);

        var result = Assert.IsType<AuditMetadataResult.Available>(
            AuditMetadataQuery.Execute(session));

        Assert.True(result.Metadata.PInvokeMethodCount >= 2);
    }

    [Fact]
    public void AuditMetadataQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(MethodClassificationScannerTests).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [AuditMetadataQuery.Definition],
            context);
        var metadata = Assert.IsType<AuditMetadataResult.Available>(
            results.Get(AuditMetadataQuery.Definition));

        Assert.True(metadata.Metadata.PInvokeMethodCount >= 2);
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void AuditMetadataQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [AuditMetadataQuery.Definition],
            context);
        var failure = Assert.IsType<AuditMetadataResult.Failed>(
            results.Get(AuditMetadataQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void AuditMetadataQuery_DisposedBorrowedSessionRemainsTyped()
    {
        using var lender = PdbContext.Open(
            typeof(MethodClassificationScannerTests).Assembly.Location);
        var session = AssemblyInspectionSession.Borrow(lender);
        session.Dispose();

        var failure = Assert.IsType<AuditMetadataResult.Failed>(
            AuditMetadataQuery.Execute(session));

        Assert.IsType<ObjectDisposedException>(failure.Error);
    }

    [Fact]
    public void AuditMetadataQuery_RetainedImageFailureDoesNotReopenPath()
    {
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        string reopenCanary =
            typeof(MethodClassificationScannerTests).Assembly.Location;
        using (var canarySession = AssemblyInspectionSession.Open(reopenCanary))
        {
            var canary = Assert.IsType<AuditMetadataResult.Available>(
                AuditMetadataQuery.Execute(canarySession));
            Assert.True(canary.Metadata.PInvokeMethodCount >= 2);
        }

        using var context = new InspectionQueryContext
        {
            AssemblyPath = reopenCanary,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };
        metadataContext.Dispose();

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [AuditMetadataQuery.Definition],
            context);
        var failure = Assert.IsType<AuditMetadataResult.Failed>(
            results.Get(AuditMetadataQuery.Definition));

        Assert.IsType<ObjectDisposedException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void AuditMetadataQuery_FailureStillComposesModelDerivedSignals()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection
        {
            HasSourceLink = false,
            PdbLocation = "standalone",
        };

        var result = Assert.IsType<AuditMetadataResult.Failed>(
            AuditMetadataQuery.Execute(session));
        LibraryMetadataService.ApplyAuditMetadataResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.Null(model.AuditMetadata);
        Assert.NotNull(model.AuditSignals);
        Assert.Contains(
            model.AuditSignals,
            signal => signal.Signal == "SourceLink"
                && signal.Value == "Not found");
    }

    [Fact]
    public void SwitchesQuery_ReturnsOrderedCompositeSwitchesFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(DotnetInspector.Fixtures.AppContextSwitchFixture).Assembly.Location);

        var result = Assert.IsType<SwitchesResult.Available>(
            SwitchesQuery.Execute(session));

        Assert.Contains(
            result.Switches,
            item => item is
            {
                Kind: "AppContext",
                Switch: "DotnetInspector.Fixtures.AppContextOnly",
            });
        Assert.Single(
            result.Switches,
            item => item.Switch == "DotnetInspector.Fixtures.Duplicate");
        Assert.DoesNotContain(
            result.Switches,
            item => item.Switch.StartsWith("TestSwitch.", StringComparison.Ordinal)
                || item.Switch.StartsWith("Switch.", StringComparison.Ordinal)
                || item.Switch.StartsWith(
                    "System.Resources.UseSystemResourceKeys",
                    StringComparison.Ordinal));
        Assert.Equal(
            result.Switches
                .OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.Switch, StringComparer.Ordinal)
                .ThenBy(item => item.Api, StringComparer.Ordinal),
            result.Switches);
    }

    [Fact]
    public void SwitchesQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(DotnetInspector.Fixtures.AppContextSwitchFixture).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [SwitchesQuery.Definition],
            context);
        var switches = Assert.IsType<SwitchesResult.Available>(
            results.Get(SwitchesQuery.Definition));

        Assert.Contains(
            switches.Switches,
            item => item.Switch == "DotnetInspector.Fixtures.AppContextOnly");
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void SwitchesQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [SwitchesQuery.Definition],
            context);
        var failure = Assert.IsType<SwitchesResult.Failed>(
            results.Get(SwitchesQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void SwitchesQuery_DisposedBorrowedSessionRemainsTyped()
    {
        using var lender = PdbContext.Open(
            typeof(DotnetInspector.Fixtures.AppContextSwitchFixture).Assembly.Location);
        var session = AssemblyInspectionSession.Borrow(lender);
        session.Dispose();

        var failure = Assert.IsType<SwitchesResult.Failed>(
            SwitchesQuery.Execute(session));

        Assert.IsType<ObjectDisposedException>(failure.Error);
    }

    [Fact]
    public void SwitchesQuery_RetainedImageFailureDoesNotReopenPath()
    {
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        string reopenCanary =
            typeof(DotnetInspector.Fixtures.AppContextSwitchFixture).Assembly.Location;
        using (var canarySession = AssemblyInspectionSession.Open(reopenCanary))
        {
            var canary = Assert.IsType<SwitchesResult.Available>(
                SwitchesQuery.Execute(canarySession));
            Assert.Contains(
                canary.Switches,
                item => item.Switch == "DotnetInspector.Fixtures.AppContextOnly");
        }

        using var context = new InspectionQueryContext
        {
            AssemblyPath = reopenCanary,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };
        metadataContext.Dispose();

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [SwitchesQuery.Definition],
            context);
        var failure = Assert.IsType<SwitchesResult.Failed>(
            results.Get(SwitchesQuery.Definition));

        Assert.IsType<ObjectDisposedException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void SwitchesQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<SwitchesResult.Failed>(
            SwitchesQuery.Execute(session));
        LibraryMetadataService.ApplySwitchesResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<SwitchInfo>.Failed>(
            model.SwitchInspection!.Value);
        Assert.Null(model.Switches);
        Assert.Equal("Switches", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void ResourcesQuery_ReturnsManifestResourcesFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(LibraryInspection).Assembly.Location);

        var result = Assert.IsType<ResourcesResult.Available>(
            ResourcesQuery.Execute(session));

        Assert.Contains(
            result.Resources,
            resource => resource.Name.Contains("SKILL.md", StringComparison.Ordinal));
        Assert.Equal(session.Resources(), result.Resources);
    }

    [Fact]
    public void ResourcesQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ResourcesQuery.Definition],
            context);
        var resources = Assert.IsType<ResourcesResult.Available>(
            results.Get(ResourcesQuery.Definition));

        Assert.Contains(
            resources.Resources,
            resource => resource.Name.Contains("SKILL.md", StringComparison.Ordinal));
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void ResourcesQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ResourcesQuery.Definition],
            context);
        var failure = Assert.IsType<ResourcesResult.Failed>(
            results.Get(ResourcesQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void ResourcesQuery_DisposedBorrowedSessionRemainsTyped()
    {
        using var lender = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        var session = AssemblyInspectionSession.Borrow(lender);
        session.Dispose();

        var failure = Assert.IsType<ResourcesResult.Failed>(
            ResourcesQuery.Execute(session));

        Assert.IsType<ObjectDisposedException>(failure.Error);
    }

    [Fact]
    public void ResourcesQuery_RetainedImageFailureDoesNotReopenPath()
    {
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = typeof(AssemblyInspectionSession).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };
        metadataContext.Dispose();

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ResourcesQuery.Definition],
            context);
        var failure = Assert.IsType<ResourcesResult.Failed>(
            results.Get(ResourcesQuery.Definition));

        Assert.IsType<ObjectDisposedException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void ResourcesQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<ResourcesResult.Failed>(
            ResourcesQuery.Execute(session));
        LibraryMetadataService.ApplyResourcesResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<ManifestResourceInfo>.Failed>(
            model.ResourceInspection!.Value);
        Assert.Null(model.Resources);
        Assert.Equal("Resources", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void CustomAttributesQuery_ReturnsMetadataOrderedAttributesFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(AssemblyInspectionSession).Assembly.Location);

        var result = Assert.IsType<CustomAttributesResult.Available>(
            CustomAttributesQuery.Execute(session));

        Assert.Contains(
            result.Attributes,
            attribute => attribute.Name == "InternalsVisibleTo");
        Assert.Equal(session.CustomAttributes(), result.Attributes);
    }

    [Fact]
    public void CustomAttributesQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(AssemblyInspectionSession).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [CustomAttributesQuery.Definition],
            context);
        var customAttributes = Assert.IsType<CustomAttributesResult.Available>(
            results.Get(CustomAttributesQuery.Definition));

        Assert.Contains(
            customAttributes.Attributes,
            attribute => attribute.Name == "InternalsVisibleTo");
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void CustomAttributesQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [CustomAttributesQuery.Definition],
            context);
        var failure = Assert.IsType<CustomAttributesResult.Failed>(
            results.Get(CustomAttributesQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void CustomAttributesQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<CustomAttributesResult.Failed>(
            CustomAttributesQuery.Execute(session));
        LibraryMetadataService.ApplyCustomAttributesResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<AssemblyAttributeInfo>.Failed>(
            model.AssemblyAttributeInspection!.Value);
        Assert.Null(model.CustomAttributes);
        Assert.Equal("Custom Attributes", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void ExtensionMethodsQuery_ReturnsDeclaredMembersFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);

        var result = Assert.IsType<ExtensionMethodsResult.Available>(
            ExtensionMethodsQuery.Execute(session));

        Assert.Contains(
            result.Methods,
            method => method.MethodName == "ToUpperCase");
    }

    [Fact]
    public void ExtensionMethodsQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ExtensionMethodsQuery.Definition],
            context);
        var extensionMethods = Assert.IsType<ExtensionMethodsResult.Available>(
            results.Get(ExtensionMethodsQuery.Definition));

        Assert.Contains(
            extensionMethods.Methods,
            method => method.MethodName == "ToUpperCase");
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void ExtensionMethodsQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ExtensionMethodsQuery.Definition],
            context);
        var failure = Assert.IsType<ExtensionMethodsResult.Failed>(
            results.Get(ExtensionMethodsQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void ExtensionMethodsQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<ExtensionMethodsResult.Failed>(
            ExtensionMethodsQuery.Execute(session));
        LibraryMetadataService.ApplyExtensionMethodsResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<ExtensionMemberObservation>.Failed>(
            model.ExtensionMemberInspection!.Value);
        Assert.Null(model.ExtensionMethods);
        Assert.Equal("Extension Methods", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void TypedQueryRegistry_BindsByIdentityAndReturnsTypedCurrency()
    {
        var prerequisite = new InspectionQuery<int>("same display name", InspectionCost.Moderated);
        var query = new InspectionQuery<InertString>(
            "same display name",
            InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(prerequisite, _ => 42)
            .Add(
                query,
                (_, results) => InertString.Format(
                    TextPolicy.Field,
                    $"answer {results.Get(prerequisite)}"),
                prerequisite);

        InspectionQueryResults results = registry.Run([query], context: null);
        InertString answer = results.Get(query);

        Assert.Equal("answer 42", answer.ToString());
        Assert.Equal(InspectionCost.Moderated, registry.CostOf(query));
        Assert.NotSame(prerequisite, query);
    }

    [Fact]
    public void TypedQueryRegistry_CompileProducesImmutableCatalogSnapshot()
    {
        var first = new InspectionQuery<int>("first", InspectionCost.NetworkFree);
        var later = new InspectionQuery<int>("later", InspectionCost.Moderated);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(first, _ => 1);

        InspectionQueryCatalog<object?> catalog = registry.Compile();

        Assert.Same(catalog, registry.Compile());
        Assert.Equal([first], catalog.RegisteredQueries);

        registry.Add(later, _ => 2);
        InspectionQueryCatalog<object?> extended = registry.Compile();

        Assert.NotSame(catalog, extended);
        Assert.Equal([first], catalog.RegisteredQueries);
        Assert.Equal([first, later], extended.RegisteredQueries);
    }

    [Fact]
    public void TypedQueryCatalog_PrecomputesSingleQueryPlan()
    {
        var prerequisite = new InspectionQuery<int>(
            "prerequisite",
            InspectionCost.Moderated);
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);
        InspectionQueryCatalog<object?> catalog =
            new InspectionQueryRegistry<object?>()
                .Add(prerequisite, _ => 41)
                .Add(
                    query,
                    (_, results) => results.Get(prerequisite) + 1,
                    prerequisite)
                .Compile();

        InspectionQueryPlan<object?> plan = catalog.Plan(query);

        Assert.Same(plan, catalog.Plan(query));
        Assert.Equal([prerequisite, query], plan.Queries);
        Assert.Equal(InspectionCost.Moderated, plan.Cost);
        Assert.Equal(42, plan.Run(context: null).Get(query));
    }

    [Fact]
    public void TypedQueryPlan_ReusesPlanWithoutSharingRunState()
    {
        var query = new InspectionQuery<string>(
            "query",
            InspectionCost.NetworkFree);
        InspectionQueryPlan<string> plan =
            new InspectionQueryRegistry<string>()
                .Add(query, context => context)
                .Compile()
                .Plan(query);

        InspectionQueryResults first = plan.Run("first");
        InspectionQueryResults second = plan.Run("second");

        Assert.NotSame(first, second);
        Assert.Equal("first", first.Get(query));
        Assert.Equal("second", second.Get(query));
    }

    [Fact]
    public void CompiledDomain_MultipleLensesShareOneQueryCatalog()
    {
        var first = new InspectionQuery<int>(
            "first",
            InspectionCost.NetworkFree);
        var second = new InspectionQuery<int>(
            "second",
            InspectionCost.NetworkFree);
        InspectionQueryCatalog<object?> queryCatalog =
            new InspectionQueryRegistry<object?>()
                .Add(first, _ => 1)
                .Add(second, _ => 2)
                .Compile();
        var domain = new CompiledInspectionDomain<object?>(queryCatalog);

        CompiledInspectionLens<object?, TestModel> firstLens =
            domain.CompileLens<TestModel>(
                pipeline => pipeline.Add<QueryBackedSection>(first));
        CompiledInspectionLens<object?, TestModel> secondLens =
            domain.CompileLens<TestModel>(
                pipeline => pipeline.Add<QueryBackedSection>(second));

        Assert.Same(queryCatalog, firstLens.QueryCatalog);
        Assert.Same(queryCatalog, secondLens.QueryCatalog);
        Assert.Same(domain, firstLens.Domain);
        Assert.Same(domain, secondLens.Domain);
        Assert.Equal(
            [first],
            firstLens.Plan(Verbosity.Minimal).RequestedQueries);
        Assert.Equal(
            [second],
            secondLens.Plan(Verbosity.Minimal).RequestedQueries);
    }

    [Fact]
    public void CompiledLens_RejectsQueryOutsideProducerDomain()
    {
        var registered = new InspectionQuery<int>(
            "registered",
            InspectionCost.NetworkFree);
        var foreign = new InspectionQuery<int>(
            "foreign",
            InspectionCost.NetworkFree);
        var domain = new CompiledInspectionDomain<object?>(
            new InspectionQueryRegistry<object?>()
                .Add(registered, _ => 1)
                .Compile());

        InspectionQueryException exception =
            Assert.Throws<InspectionQueryException>(
                () => domain.CompileLens<TestModel>(
                    pipeline => pipeline.Add<QueryBackedSection>(foreign)));

        Assert.Contains("foreign", exception.Message);
        Assert.Contains("outside the compiled inspection domain", exception.Message);
    }

    [Fact]
    public void CompiledLens_InstallsPrerequisiteAwareCostsBeforeRegistration()
    {
        var prerequisite = new InspectionQuery<int>(
            "prerequisite",
            InspectionCost.Moderated);
        var query = new InspectionQuery<int>(
            "query",
            InspectionCost.NetworkFree);
        var domain = new CompiledInspectionDomain<object?>(
            new InspectionQueryRegistry<object?>()
                .Add(prerequisite, _ => 1)
                .Add(
                    query,
                    (_, results) => results.Get(prerequisite),
                    prerequisite)
                .Compile());

        CompiledInspectionLens<object?, TestModel> lens =
            domain.CompileLens<TestModel>(
                pipeline => pipeline.Add<QueryBackedSection>(query));

        Assert.Equal(
            SectionCost.Moderated,
            Assert.Single(lens.Sections.Pipeline.SectionCosts).Cost);
        Assert.Throws<InvalidOperationException>(
            () => domain.CompileLens<TestModel>(
                pipeline => pipeline.UseQueryCosts(
                    _ => InspectionCost.NetworkFree)));
    }

    [Fact]
    public void CompiledLens_LowersEmptySingleAndMultiQueryDemand()
    {
        var first = new InspectionQuery<int>(
            "first",
            InspectionCost.NetworkFree);
        var second = new InspectionQuery<int>(
            "second",
            InspectionCost.NetworkFree);
        var host = new InspectionQuery<int>(
            "host",
            InspectionCost.NetworkFree);
        var domain = new CompiledInspectionDomain<object?>(
            new InspectionQueryRegistry<object?>()
                .Add(first, _ => 1)
                .Add(second, _ => 2)
                .Add(host, _ => 3)
                .Compile());
        CompiledInspectionLens<object?, TestModel> lens =
            domain.CompileLens<TestModel>(
                pipeline => pipeline
                    .Add<QueryBackedSection>(first)
                    .Add<DetailedSection>(second));
        var firstOnly = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            QueryBackedSection.Name,
        };
        var both = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            QueryBackedSection.Name,
            DetailedSection.Name,
        };
        HostQueryDemand hostDemand = new("test host", host);

        CompiledInspectionPlan<object?> empty =
            lens.Plan(Verbosity.Quiet);
        CompiledInspectionPlan<object?> single =
            lens.Plan(Verbosity.Minimal, firstOnly);
        CompiledInspectionPlan<object?> multi =
            lens.Plan(Verbosity.Minimal, both);
        CompiledInspectionPlan<object?> attributedHost =
            lens.Plan(
                Verbosity.Quiet,
                hostDemand: [hostDemand]);
        CompiledInspectionPlan<object?> overlappingHost =
            lens.Plan(
                Verbosity.Minimal,
                firstOnly,
                hostDemand: [new HostQueryDemand("same query", first)]);

        Assert.Empty(empty.RequestedQueries);
        Assert.Empty(empty.QueryPlan.Queries);
        Assert.Same(
            domain.QueryCatalog.Plan(Array.Empty<InspectionQueryDefinition>()),
            empty.QueryPlan);
        Assert.Equal([first], single.RequestedQueries);
        Assert.Equal([first], single.QueryPlan.Queries);
        Assert.Same(domain.QueryCatalog.Plan(first), single.QueryPlan);
        Assert.Equal([first, second], multi.RequestedQueries);
        Assert.Equal([first, second], multi.QueryPlan.Queries);
        Assert.Equal([hostDemand], attributedHost.HostDemand);
        Assert.Equal([host], attributedHost.RequestedQueries);
        Assert.Equal([host], attributedHost.QueryPlan.Queries);
        Assert.Equal(
            [new HostQueryDemand("same query", first)],
            overlappingHost.HostDemand);
        Assert.Equal([first], overlappingHost.RequestedQueries);
        Assert.Equal([first], overlappingHost.QueryPlan.Queries);
    }

    [Fact]
    public void CompiledInspectionPlan_DefaultValueFailsExplicitly()
    {
        CompiledInspectionPlan<object?> plan = default;

        Assert.True(plan.IsDefault);
        Assert.Empty(plan.HostDemand);
        Assert.Empty(plan.RequestedQueries);
        Assert.Throws<InvalidOperationException>(() => plan.Run(context: null));
    }

    [Fact]
    public void CompiledExecution_DoesNotTransformTypedQueryResults()
    {
        var query = new InspectionQuery<object>(
            "context",
            InspectionCost.NetworkFree);
        var domain = new CompiledInspectionDomain<object>(
            new InspectionQueryRegistry<object>()
                .Add(query, context => context)
                .Compile());
        CompiledInspectionPlan<object> plan =
            domain.CompileLens<TestModel>(
                    pipeline => pipeline.Add<QueryBackedSection>(query))
                .Plan(Verbosity.Minimal);
        var expected = new object();

        InspectionQueryResults results = plan.Run(expected);

        Assert.Same(expected, results.Get(query));
    }

    [Fact]
    public async Task CompiledExecution_ForwardsAsyncCancellation()
    {
        var query = new InspectionQuery<int>(
            "async",
            InspectionCost.NetworkFree);
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        var domain = new CompiledInspectionDomain<object?>(
            new InspectionQueryRegistry<object?>()
                .AddAsync(
                    query,
                    async (_, cancellationToken) =>
                    {
                        observed = cancellationToken;
                        entered.SetResult(true);
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            cancellationToken);
                        return 1;
                    })
                .Compile());
        CompiledInspectionPlan<object?> plan =
            domain.CompileLens<TestModel>(
                    pipeline => pipeline.Add<QueryBackedSection>(query))
                .Plan(Verbosity.Minimal);
        using var cancellation = new CancellationTokenSource();

        Task<InspectionQueryResults> execution = plan.RunAsync(
            context: null,
            cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution);
        Assert.Equal(cancellation.Token, observed);
    }

    [Fact]
    public void CompiledExecution_DoesNotRetainOrDisposeSuppliedContext()
    {
        var query = new InspectionQuery<string>(
            "value",
            InspectionCost.NetworkFree);
        var domain = new CompiledInspectionDomain<DisposableQueryContext>(
            new InspectionQueryRegistry<DisposableQueryContext>()
                .Add(query, context => context.Value)
                .Compile());
        CompiledInspectionPlan<DisposableQueryContext> plan =
            domain.CompileLens<TestModel>(
                    pipeline => pipeline.Add<QueryBackedSection>(query))
                .Plan(Verbosity.Minimal);
        var first = new DisposableQueryContext("first");
        var second = new DisposableQueryContext("second");

        InspectionQueryResults firstResults = plan.Run(first);
        InspectionQueryResults secondResults = plan.Run(second);

        Assert.False(first.IsDisposed);
        Assert.False(second.IsDisposed);
        Assert.Equal("first", firstResults.Get(query));
        Assert.Equal("second", secondResults.Get(query));

        WeakReference releasedContext = RunAndReleaseContext(plan);
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Forced,
            blocking: true,
            compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Forced,
            blocking: true,
            compacting: true);

        Assert.False(releasedContext.IsAlive);
    }

    [Fact]
    public void LibraryQueryCatalog_RepeatedAcquisitionAndPlanningAllocateNothing()
    {
        InspectionQueryCatalog<InspectionQueryContext> queryCatalog =
            LibrarySections.QueryCatalog;
        InspectionQueryCatalog<AssemblyContextGroup> groupQueryCatalog =
            LibrarySections.GroupQueryCatalog;
        InspectionQueryPlan<InspectionQueryContext> plan =
            queryCatalog.Plan(BodyShapesQuery.Definition);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            if (!ReferenceEquals(queryCatalog, LibrarySections.QueryCatalog)
                || !ReferenceEquals(
                    groupQueryCatalog,
                    LibrarySections.GroupQueryCatalog)
                || !ReferenceEquals(
                    plan,
                    queryCatalog.Plan(BodyShapesQuery.Definition)))
            {
                throw new InvalidOperationException(
                    "The library query catalog or its precomputed plan changed identity.");
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CompiledSectionCatalog_FreezesBuilderAndSnapshotsEnumeration()
    {
        string[] categoryMembers = [AlwaysSection.Name];
        var pipeline = CreateTestPipeline()
            .AddCategory("@Core", categoryMembers);
        categoryMembers[0] = DetailedSection.Name;

        SectionCatalog<TestModel> catalog = pipeline.Compile();

        Assert.Same(catalog, pipeline.Compile());
        Assert.Equal(
            [AlwaysSection.Name, NormalSection.Name, DetailedSection.Name],
            catalog.AllSectionNames);
        Assert.Equal([AlwaysSection.Name], catalog.CategoryMap["@Core"]);
        Assert.Equal(["@All", "@Core"], catalog.CategoryNames);
        Assert.Throws<InvalidOperationException>(
            () => pipeline.Add<QueryBackedSection>());
        Assert.Throws<InvalidOperationException>(
            () => pipeline.AddCategory("@More", AlwaysSection.Name));
        Assert.Throws<InvalidOperationException>(
            () => pipeline.UseCuratedCatalog());
        Assert.Throws<InvalidOperationException>(
            () => pipeline.UseQueryCosts(
                _ => InspectionCost.NetworkFree));
        Assert.Throws<InvalidOperationException>(
            () => pipeline.WithoutComputedPoles());
    }

    [Fact]
    public void LibrarySectionCatalog_QueryPlansMatchMutablePipeline()
        => AssertSectionCatalogQueryPlansMatch(
            LibrarySections.CreateCatalog().Sections);

    [Fact]
    public void PackageSectionCatalog_QueryPlansMatchMutablePipeline()
        => AssertSectionCatalogQueryPlansMatch(
            PackageSectionDescriptors.CreateCatalog().Sections);

    [Fact]
    public void DiffSectionCatalog_QueryPlansMatchMutablePipeline()
        => AssertSectionCatalogQueryPlansMatch(
            DiffSections.CreateCatalog().Sections);

    [Fact]
    public void PackageProfileSectionCatalog_QueryPlansMatchMutablePipeline()
        => AssertSectionCatalogQueryPlansMatch(
            PackageProfileSections.CreateCatalog().Sections);

    [Fact]
    public void PackageCatalog_RepeatedAcquisitionAndCommonPlanningAllocateNothing()
    {
        PackageSectionCatalog packageCatalog =
            PackageSectionDescriptors.CreateCatalog();
        SectionCatalog<InspectionResult> sectionCatalog =
            packageCatalog.Sections;
        InspectionQueryCatalog<SourceLinkQueryContext> queryCatalog =
            packageCatalog.QueryCatalog;
        SectionQueryPlan automaticPlan =
            sectionCatalog.PlanQueries(Verbosity.Normal);
        HashSet<string> exactSelection = new(StringComparer.OrdinalIgnoreCase)
        {
            PackageSections.SourceLinkAvailability,
        };
        SectionQueryPlan exactPlan =
            sectionCatalog.PlanQueries(Verbosity.Normal, exactSelection);
        InspectionQueryPlan<SourceLinkQueryContext> exactQueryPlan =
            queryCatalog.Plan(exactPlan.Queries[0]);
        InspectionQueryPlan<SourceLinkQueryContext> emptyQueryPlan =
            queryCatalog.Plan(Array.Empty<InspectionQueryDefinition>());
        ImmutableArray<string> categoryMembers =
            sectionCatalog.CategoryMap[SectionCategoryNames.SourceLink];
        HashSet<string> categorySelection =
            new(categoryMembers, StringComparer.OrdinalIgnoreCase);
        SectionQueryPlan categoryPlan =
            sectionCatalog.PlanQueries(Verbosity.Normal, categorySelection);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            if (!ReferenceEquals(
                    packageCatalog,
                    PackageSectionDescriptors.CreateCatalog())
                || !ReferenceEquals(
                    sectionCatalog,
                    PackageSectionDescriptors.SectionCatalog)
                || !ReferenceEquals(
                    queryCatalog,
                    PackageSectionDescriptors.QueryCatalog)
                || !ReferenceEquals(
                    automaticPlan,
                    sectionCatalog.PlanQueries(Verbosity.Normal))
                || !ReferenceEquals(
                    exactPlan,
                    sectionCatalog.PlanQueries(
                        Verbosity.Normal,
                        exactSelection))
                || !ReferenceEquals(
                    exactQueryPlan,
                    queryCatalog.Plan(exactPlan.Queries[0]))
                || !ReferenceEquals(
                    emptyQueryPlan,
                    queryCatalog.Plan(
                        Array.Empty<InspectionQueryDefinition>()))
                || !ReferenceEquals(
                    categoryPlan,
                    sectionCatalog.PlanQueries(
                        Verbosity.Normal,
                        categorySelection)))
            {
                throw new InvalidOperationException(
                    "The package catalog or a precomputed plan changed identity.");
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void DiffCatalog_RepeatedAcquisitionAndCommonPlanningAllocateNothing()
    {
        DiffSectionCatalog diffCatalog = DiffSections.CreateCatalog();
        CompiledInspectionLens<DiffQueryContext, DiffDiscoveryModel> lens =
            diffCatalog.Lens;
        SectionCatalog<DiffDiscoveryModel> sectionCatalog =
            diffCatalog.Sections;
        InspectionQueryCatalog<DiffQueryContext> queryCatalog =
            diffCatalog.QueryCatalog;
        SectionQueryPlan automaticPlan =
            sectionCatalog.PlanQueries(Verbosity.Minimal);
        HashSet<string> changesSelection =
            new(StringComparer.OrdinalIgnoreCase)
            {
                DiffSections.Changes.Name,
            };
        HashSet<string> analysisSelection =
            new(StringComparer.OrdinalIgnoreCase)
            {
                DiffSections.AnalysisDiff.Name,
            };
        SectionQueryPlan changesSectionPlan =
            sectionCatalog.PlanQueries(
                Verbosity.Minimal,
                changesSelection);
        SectionQueryPlan analysisSectionPlan =
            sectionCatalog.PlanQueries(
                Verbosity.Minimal,
                analysisSelection);
        CompiledInspectionPlan<DiffQueryContext> changesCompiledPlan =
            lens.Plan(
                Verbosity.Minimal,
                changesSelection);
        CompiledInspectionPlan<DiffQueryContext> analysisCompiledPlan =
            lens.Plan(
                Verbosity.Minimal,
                analysisSelection);
        InspectionQueryPlan<DiffQueryContext> changesQueryPlan =
            queryCatalog.Plan(changesSectionPlan.Queries[0]);
        InspectionQueryPlan<DiffQueryContext> analysisQueryPlan =
            queryCatalog.Plan(analysisSectionPlan.Queries[0]);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            if (!ReferenceEquals(
                    diffCatalog,
                    DiffSections.CreateCatalog())
                || !ReferenceEquals(
                    lens,
                    DiffSections.Lens)
                || !ReferenceEquals(
                    sectionCatalog,
                    DiffSections.SectionCatalog)
                || !ReferenceEquals(
                    queryCatalog,
                    DiffSections.QueryCatalog)
                || !ReferenceEquals(
                    automaticPlan,
                    sectionCatalog.PlanQueries(Verbosity.Minimal))
                || !ReferenceEquals(
                    changesSectionPlan,
                    sectionCatalog.PlanQueries(
                        Verbosity.Minimal,
                        changesSelection))
                || !ReferenceEquals(
                    analysisSectionPlan,
                    sectionCatalog.PlanQueries(
                        Verbosity.Minimal,
                        analysisSelection))
                || !ReferenceEquals(
                    changesCompiledPlan.QueryPlan,
                    lens.Plan(
                        Verbosity.Minimal,
                        changesSelection).QueryPlan)
                || !ReferenceEquals(
                    analysisCompiledPlan.QueryPlan,
                    lens.Plan(
                        Verbosity.Minimal,
                        analysisSelection).QueryPlan)
                || !ReferenceEquals(
                    changesQueryPlan,
                    queryCatalog.Plan(changesSectionPlan.Queries[0]))
                || !ReferenceEquals(
                    analysisQueryPlan,
                    queryCatalog.Plan(analysisSectionPlan.Queries[0])))
            {
                throw new InvalidOperationException(
                    "The Diff catalog or a precomputed plan changed identity.");
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void PackageProfileCatalog_RepeatedAcquisitionAndCommonPlanningAllocateNothing()
    {
        PackageProfileSectionCatalog profileCatalog =
            PackageProfileSections.CreateCatalog();
        SectionCatalog<PackageProfileView> sectionCatalog =
            profileCatalog.Sections;
        InspectionQueryCatalog<PackageProfileQueryContext> queryCatalog =
            profileCatalog.QueryCatalog;
        SectionQueryPlan automaticPlan =
            sectionCatalog.PlanQueries(Verbosity.Normal);
        HashSet<string> packageSelection =
            new(StringComparer.OrdinalIgnoreCase)
            {
                PackageProfileSections.Packages,
            };
        SectionQueryPlan packageSectionPlan =
            sectionCatalog.PlanQueries(
                Verbosity.Normal,
                packageSelection);
        InspectionQueryPlan<PackageProfileQueryContext> packageQueryPlan =
            profileCatalog.Lens
                .Plan(Verbosity.Normal, packageSelection)
                .QueryPlan;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            if (!ReferenceEquals(
                    profileCatalog,
                    PackageProfileSections.CreateCatalog())
                || !ReferenceEquals(
                    sectionCatalog,
                    PackageProfileSections.SectionCatalog)
                || !ReferenceEquals(
                    queryCatalog,
                    PackageProfileSections.QueryCatalog)
                || !ReferenceEquals(
                    automaticPlan,
                    sectionCatalog.PlanQueries(Verbosity.Normal))
                || !ReferenceEquals(
                    packageSectionPlan,
                    sectionCatalog.PlanQueries(
                        Verbosity.Normal,
                        packageSelection))
                || !ReferenceEquals(
                    packageQueryPlan,
                    profileCatalog.Lens
                        .Plan(Verbosity.Normal, packageSelection)
                        .QueryPlan))
            {
                throw new InvalidOperationException(
                    "The package-profile catalog or a precomputed plan changed identity.");
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void LibrarySectionCatalog_RepeatedAcquisitionAndCommonPlanningAllocateNothing()
    {
        LibrarySectionCatalog libraryCatalog = LibrarySections.CreateCatalog();
        SectionCatalog<LibraryInspection> catalog = libraryCatalog.Sections;
        SectionQueryPlan automaticPlan = catalog.PlanQueries(Verbosity.Normal);
        HashSet<string> exactSelection = new(StringComparer.OrdinalIgnoreCase)
        {
            catalog.SelectableSectionNames[0],
        };
        SectionQueryPlan exactPlan =
            catalog.PlanQueries(Verbosity.Normal, exactSelection);
        ImmutableArray<string> categoryMembers = catalog.CategoryMap.Values.First();
        HashSet<string> categorySelection =
            new(categoryMembers, StringComparer.OrdinalIgnoreCase);
        SectionQueryPlan categoryPlan =
            catalog.PlanQueries(Verbosity.Normal, categorySelection);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            if (!ReferenceEquals(libraryCatalog, LibrarySections.CreateCatalog())
                || !ReferenceEquals(catalog, LibrarySections.SectionCatalog)
                || !ReferenceEquals(
                    automaticPlan,
                    catalog.PlanQueries(Verbosity.Normal))
                || !ReferenceEquals(
                    exactPlan,
                    catalog.PlanQueries(Verbosity.Normal, exactSelection))
                || !ReferenceEquals(
                    categoryPlan,
                    catalog.PlanQueries(Verbosity.Normal, categorySelection)))
            {
                throw new InvalidOperationException(
                    "The library section catalog or a precomputed plan changed identity.");
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CompiledSectionQueryPlan_PreservesTraceAttributionAndCommandDemand()
    {
        var sectionQuery = new InspectionQuery<int>(
            "section query",
            InspectionCost.NetworkFree);
        var commandQuery = new InspectionQuery<int>(
            "command query",
            InspectionCost.NetworkFree);
        var pipeline = new SectionPipeline<TestModel>()
            .Add<QueryBackedSection>(sectionQuery);
        SectionCatalog<TestModel> catalog = pipeline.Compile();
        var include = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            QueryBackedSection.Name,
        };
        List<HostQueryDemand> commandDemand =
        [
            new("test command", commandQuery),
        ];
        var expectedTrace = new InspectionTrace();
        var actualTrace = new InspectionTrace();

        HashSet<InspectionQueryDefinition> expected = pipeline.GetRequiredQueries(
            Verbosity.Normal,
            include,
            trace: expectedTrace,
            commandDemand: commandDemand);
        HashSet<InspectionQueryDefinition> actual = catalog
            .PlanQueries(Verbosity.Normal, include)
            .Activate(actualTrace, commandDemand);

        Assert.True(expected.SetEquals(actual));
        Assert.Equal(expectedTrace.QueryDemand, actualTrace.QueryDemand);
        Assert.Equal(expectedTrace.CommandQueryDemand, actualTrace.CommandQueryDemand);
        Assert.Equal(expectedTrace.RequestedQueries, actualTrace.RequestedQueries);
    }

    [Fact]
    public void QueryBackedSection_InheritsDependencyClosureCost()
    {
        var prerequisite = new InspectionQuery<int>(
            "moderated prerequisite",
            InspectionCost.Moderated);
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(prerequisite, _ => 1)
            .Add(query, (_, results) => results.Get(prerequisite), prerequisite);
        var pipeline = new SectionPipeline<TestModel>()
            .UseQueryCosts(registry.CostOf)
            .Add<QueryBackedSection>(query);

        var section = Assert.Single(pipeline.SectionCosts);
        Assert.Equal(SectionCost.Moderated, section.Cost);
    }

    [Fact]
    public void QueryBackedSection_MultipleQueries_InheritsMaximumCostAndDemandsEach()
    {
        var networkFree = new InspectionQuery<int>(
            "network-free",
            InspectionCost.NetworkFree);
        var moderated = new InspectionQuery<int>(
            "moderated",
            InspectionCost.Moderated);
        var pipeline = new SectionPipeline<TestModel>()
            .UseQueryCosts(query => query.Cost)
            .Add<QueryBackedSection>([networkFree, moderated]);

        var section = Assert.Single(pipeline.SectionCosts);
        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal);

        Assert.Equal(SectionCost.Moderated, section.Cost);
        Assert.Equal(
            [moderated, networkFree],
            required.OrderBy(query => query.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void QueryBackedSection_MultipleQueries_RejectsDuplicateIdentity()
    {
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);

        Assert.Throws<ArgumentException>(() =>
            new SectionPipeline<TestModel>().Add<QueryBackedSection>([query, query]));
    }

    [Fact]
    public void GetRequiredQueries_ExcludeUnbounded_PreservesExplicitBoundedSelection()
    {
        var bounded = new InspectionQuery<int>("bounded", InspectionCost.NetworkFree);
        var unbounded = new InspectionQuery<int>("unbounded", InspectionCost.Unbounded);
        var pipeline = new SectionPipeline<TestModel>()
            .UseCuratedCatalog()
            .UseQueryCosts(query => query.Cost)
            .Add(new SectionEntry<TestModel>
            {
                Name = "Bounded",
                IsExpensive = false,
                SizeClass = SectionSizeClass.Terse,
                Cost = SectionCost.NetworkFree,
                Queries = [bounded],
                IsApplicable = _ => true,
                CanRender = _ => true,
            })
            .Add(new SectionEntry<TestModel>
            {
                Name = "Unbounded",
                IsExpensive = false,
                SizeClass = SectionSizeClass.Terse,
                Cost = SectionCost.NetworkFree,
                Queries = [unbounded],
                IsApplicable = _ => true,
                CanRender = _ => true,
            });
        var include = new HashSet<string> { "Bounded", "Unbounded" };

        var renderQueries = pipeline.GetRequiredQueries(Verbosity.Detailed, include);
        var discoveryQueries = pipeline.GetRequiredQueries(
            Verbosity.Detailed,
            include,
            excludeUnbounded: true);

        Assert.Contains(bounded, renderQueries);
        Assert.Contains(unbounded, renderQueries);
        Assert.Equal([bounded], discoveryQueries);
    }

    [Fact]
    public void TypedQueryRegistry_ExecutesPrerequisitesOnceInDeclaredOrder()
    {
        List<string> order = [];
        var prerequisite = new InspectionQuery<int>("prerequisite", InspectionCost.NetworkFree);
        var first = new InspectionQuery<int>("first", InspectionCost.NetworkFree);
        var second = new InspectionQuery<int>("second", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(prerequisite, _ =>
            {
                order.Add("prerequisite");
                return 1;
            })
            .Add(first, (_, results) =>
            {
                order.Add("first");
                return results.Get(prerequisite) + 1;
            }, prerequisite)
            .Add(second, (_, results) =>
            {
                order.Add("second");
                return results.Get(prerequisite) + results.Get(first);
            }, first);

        InspectionQueryResults results = registry.Run([first, second], context: null);

        Assert.Equal(["prerequisite", "first", "second"], order);
        Assert.Equal(3, results.Get(second));
    }

    [Fact]
    public async Task TypedQueryRegistry_RunAsync_ExecutesMixedQueriesInDeclaredOrder()
    {
        List<string> order = [];
        var prerequisite = new InspectionQuery<int>("prerequisite", InspectionCost.NetworkFree);
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(prerequisite, _ =>
            {
                order.Add("prerequisite");
                return 1;
            })
            .AddAsync(
                query,
                (_, results, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    order.Add("query");
                    return ValueTask.FromResult(results.Get(prerequisite) + 1);
                },
                prerequisite);

        InspectionQueryResults results = await registry.RunAsync(
            [query],
            context: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["prerequisite", "query"], order);
        Assert.Equal(2, results.Get(query));
    }

    [Fact]
    public void TypedQueryRegistry_OptionalDependencyRunsOnlyWhenIndependentlyRequested()
    {
        var optional = new InspectionQuery<int>("optional", InspectionCost.Unbounded);
        var consumer = new InspectionQuery<int>("consumer", InspectionCost.NetworkFree);
        List<string> order = [];
        var registry = new InspectionQueryRegistry<object?>()
            .AddWithOptional(
                consumer,
                (_, results) =>
                {
                    order.Add("consumer");
                    return results.TryGet(optional, out int value) ? value : 0;
                },
                [optional])
            .Add(optional, _ =>
            {
                order.Add("optional");
                return 42;
            });

        InspectionQueryResults withoutOptional = registry.Run([consumer], null);

        Assert.Equal(0, withoutOptional.Get(consumer));
        Assert.Equal(["consumer"], order);
        Assert.Equal([consumer], registry.ExpandRequired([consumer]));
        Assert.Equal(InspectionCost.NetworkFree, registry.CostOf(consumer));

        order.Clear();
        InspectionQueryResults withOptional = registry.Run(
            [consumer, optional],
            null);

        Assert.Equal(42, withOptional.Get(consumer));
        Assert.Equal(["optional", "consumer"], order);
        Assert.Equal([optional], registry.OptionalDependenciesOf(consumer));
    }

    [Fact]
    public async Task TypedQueryRegistry_RunAsync_OrdersActiveOptionalDependency()
    {
        var optional = new InspectionQuery<int>("optional", InspectionCost.NetworkFree);
        var consumer = new InspectionQuery<int>("consumer", InspectionCost.NetworkFree);
        List<string> order = [];
        var registry = new InspectionQueryRegistry<object?>()
            .AddAsyncWithOptional(
                consumer,
                (_, results, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    order.Add("consumer");
                    return ValueTask.FromResult(results.Get(optional));
                },
                [optional])
            .Add(optional, _ =>
            {
                order.Add("optional");
                return 42;
            });

        InspectionQueryResults results = await registry.RunAsync(
            [consumer, optional],
            context: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(42, results.Get(consumer));
        Assert.Equal(["optional", "consumer"], order);
    }

    [Fact]
    public void TypedQueryRegistry_RejectsActiveOptionalDependencyCycle()
    {
        var first = new InspectionQuery<int>("first", InspectionCost.NetworkFree);
        var second = new InspectionQuery<int>("second", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .AddWithOptional(first, (_, _) => 1, [second])
            .AddWithOptional(second, (_, _) => 2, [first]);

        InspectionQueryException exception = Assert.Throws<InspectionQueryException>(
            () => registry.Run([first, second], context: null));

        Assert.Contains("active dependency cycle", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedQueryRegistry_RunRejectsAsynchronousQueries()
    {
        var query = new InspectionQuery<int>("async", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .AddAsync(query, (_, _) => ValueTask.FromResult(1));

        var exception = Assert.Throws<InspectionQueryException>(
            () => registry.Run([query], context: null));

        Assert.Contains("must be executed with RunAsync", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypedQueryRegistry_RunAsync_PropagatesCancellation()
    {
        bool ran = false;
        var query = new InspectionQuery<int>("async", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .AddAsync(query, (_, _) =>
            {
                ran = true;
                return ValueTask.FromResult(1);
            });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => registry.RunAsync([query], context: null, cancellationToken: cancellation.Token));
        Assert.False(ran);
    }

    [Fact]
    public async Task TypedQueryRegistry_RunAsync_EmptyDemandPropagatesCancellation()
    {
        var registered = new InspectionQuery<int>(
            "registered",
            InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(registered, _ => 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => registry.RunAsync(
                [],
                context: null,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task TypedQueryRegistry_RunAsync_RejectsUndeclaredResultDependencies()
    {
        var hidden = new InspectionQuery<int>("hidden", InspectionCost.Unbounded);
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .AddAsync(hidden, (_, _) => ValueTask.FromResult(42))
            .AddAsync(
                query,
                (_, results, _) => ValueTask.FromResult(results.Get(hidden)));

        var exception = await Assert.ThrowsAsync<InspectionQueryException>(
            () => registry.RunAsync(
                [hidden, query],
                context: null,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("not a declared prerequisite", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedQueryRegistry_RejectsUndeclaredResultDependencies()
    {
        var hidden = new InspectionQuery<int>("hidden", InspectionCost.Unbounded);
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(hidden, _ => 42)
            .Add(query, (_, results) => results.Get(hidden));

        var present = Assert.Throws<InspectionQueryException>(
            () => registry.Run([hidden, query], context: null));
        var absent = Assert.Throws<InspectionQueryException>(
            () => registry.Run([query], context: null));

        Assert.Contains("not a declared prerequisite", present.Message, StringComparison.Ordinal);
        Assert.Equal(present.Message, absent.Message);
        Assert.Equal(InspectionCost.NetworkFree, registry.CostOf(query));
    }

    [Fact]
    public void TypedQueryRegistry_PrerequisiteGraphIsImmutableAndFailVisible()
    {
        var prerequisite = new InspectionQuery<int>("prerequisite", InspectionCost.NetworkFree);
        var replacement = new InspectionQuery<int>("replacement", InspectionCost.Unbounded);
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);
        InspectionQueryDefinition[] declared = [prerequisite];
        var registry = new InspectionQueryRegistry<object?>()
            .Add(prerequisite, _ => 1)
            .Add(replacement, _ => 2)
            .Add(query, (_, results) => results.Get(prerequisite), declared);

        declared[0] = replacement;

        Assert.Equal([prerequisite], registry.RequirementsOf(query));
        Assert.Equal(InspectionCost.NetworkFree, registry.CostOf(query));

        var missing = new InspectionQuery<int>("missing", InspectionCost.NetworkFree);
        Assert.Throws<InspectionQueryException>(() => registry.ExpandRequired([missing]));
    }

    [Fact]
    public void TypedQueryRegistry_RejectsPrerequisiteCycles()
    {
        var first = new InspectionQuery<int>("first", InspectionCost.NetworkFree);
        var second = new InspectionQuery<int>("second", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(first, _ => 1, second)
            .Add(second, _ => 2, first);

        Assert.Throws<InspectionQueryException>(() => registry.ExpandRequired([first]));
    }

    [Fact]
    public void InspectionCost_OrdersFromCheapestToMostExpensive()
    {
        Assert.Equal(
            [InspectionCost.NetworkFree, InspectionCost.Moderated, InspectionCost.Unbounded],
            Enum.GetValues<InspectionCost>());
    }

    [Fact]
    public void TypedQuery_CannotTakeTheBodyIndexWithoutDeclaringItsTransitiveCost()
    {
        var cheap = new InspectionQuery<int>("cheap", InspectionCost.NetworkFree);
        var cheapRegistry = LibrarySections.CreateQueryRegistry()
            .Add(cheap, ctx =>
            {
                ctx.BodyIndex();
                return 0;
            });

        var refused = Assert.Throws<QueryCostDeclarationException>(
            () => cheapRegistry.Run([cheap], NullQueryContext()));
        Assert.Contains("Query 'cheap'", refused.Message, StringComparison.Ordinal);
        Assert.Contains("body index", refused.Message, StringComparison.Ordinal);
        Assert.Contains("NetworkFree", refused.Message, StringComparison.Ordinal);

        var unboundedPrerequisite = new InspectionQuery<int>(
            "unbounded prerequisite",
            InspectionCost.Unbounded);
        var transitivelyUnbounded = new InspectionQuery<int>(
            "transitively unbounded",
            InspectionCost.NetworkFree);
        var declaredRegistry = LibrarySections.CreateQueryRegistry()
            .Add(unboundedPrerequisite, _ => 1)
            .Add(
                transitivelyUnbounded,
                ctx =>
                {
                    ctx.BodyIndex();
                    return 0;
                },
                unboundedPrerequisite);

        var allowed = Assert.Throws<InvalidOperationException>(
            () => declaredRegistry.Run([transitivelyUnbounded], NullQueryContext()));
        Assert.Contains("metadata context", allowed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("transitively unbounded", allowed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedQuery_CannotTakeTheDrillMapWithoutDeclaringItsCost()
    {
        var cheap = new InspectionQuery<int>("cheap", InspectionCost.NetworkFree);
        var cheapRegistry = LibrarySections.CreateQueryRegistry()
            .Add(cheap, ctx =>
            {
                ctx.DrillMap();
                return 0;
            });

        var refused = Assert.Throws<QueryCostDeclarationException>(
            () => cheapRegistry.Run([cheap], NullQueryContext()));
        Assert.Contains("drill map", refused.Message, StringComparison.Ordinal);

        var declared = new InspectionQuery<int>("declared", InspectionCost.Unbounded);
        var declaredRegistry = LibrarySections.CreateQueryRegistry()
            .Add(declared, ctx =>
            {
                ctx.DrillMap();
                return 0;
            });

        var allowed = Assert.Throws<InvalidOperationException>(
            () => declaredRegistry.Run([declared], NullQueryContext()));
        Assert.Contains("metadata context", allowed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedQueryDeclaration_DoesNotOutliveTheExecutor()
    {
        var query = new InspectionQuery<int>("cheap", InspectionCost.NetworkFree);
        var registry = LibrarySections.CreateQueryRegistry()
            .Add(query, _ => 1);
        var context = NullQueryContext();

        registry.Run([query], context);

        var ex = Assert.Throws<InvalidOperationException>(() => context.BodyIndex());
        Assert.Contains("metadata context", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("NetworkFree", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MetadataImageQuery_CarriesInertStringInItsTypedResult()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);

        var available = Assert.IsType<MetadataImageResult.Available>(
            MetadataImageQuery.Execute(session));
        InertString metadataVersion = available.Overview.MetadataVersion;

        Assert.True(InertString.IsPermitted(TextPolicy.Field, metadataVersion.ToString()));
        Assert.False(metadataVersion.IsEmpty);
    }

    [Fact]
    public void MetadataImageQuery_FailureRemainsTypedAndAffectsEveryMetadataSection()
    {
        var model = new LibraryInspection();
        Assert.Null(model.InspectionFailures);

        var error = new InvalidDataException("metadata image failed");
        model.MetadataImageResult = new MetadataImageResult.Failed(error);

        Assert.Null(model.MetadataOverview);
        Assert.Same(error, Assert.IsType<MetadataImageResult.Failed>(
            model.MetadataImageResult).Error);
        LibraryInspectionFailureJson failure = Assert.Single(model.InspectionFailures!);
        Assert.Equal(MetadataSectionNames.Image, failure.Section);
        Assert.Equal(MetadataImageQuery.Definition.Name, failure.Finding);
        Assert.Equal(error.Message, failure.Reason);
        Assert.All(
            MetadataSectionNames.All,
            section => Assert.True(LibraryCommand.FailureAffectsSection(
                failure.Section,
                section)));
    }

    [Fact]
    public async Task ProductionQueryCatchBoundary_DoesNotSwallowDeclarationViolation()
    {
        var query = new InspectionQuery<TopLeverageResult>(
            "cheap",
            InspectionCost.NetworkFree);
        var registry = LibrarySections.CreateQueryRegistry()
            .Add(query, LibrarySections.ExecuteTopLeverageQuery);
        using var httpClient = new HttpClient();

        await Assert.ThrowsAsync<QueryCostDeclarationException>(() =>
            LibraryMetadataService.InspectAsync(
                typeof(SectionPipelineTests).Assembly.Location,
                new LibraryOptions(),
                new DotnetInspect.Cli.Output.VerboseLogger(false),
                packageName: null,
                packageVersion: null,
                httpClient,
                queries: [query],
                queryCatalog: registry.Compile()));
    }

    [Fact]
    public async Task ProductionQueryCatchBoundary_DoesNotSwallowExecutorFailure()
    {
        var query = new InspectionQuery<int>("failing", InspectionCost.NetworkFree);
        var registry = LibrarySections.CreateQueryRegistry()
            .Add<int>(query, _ => throw new IOException("executor failed"));
        using var httpClient = new HttpClient();

        var ex = await Assert.ThrowsAsync<InspectionQueryException>(() =>
            LibraryMetadataService.InspectAsync(
                typeof(SectionPipelineTests).Assembly.Location,
                new LibraryOptions(),
                new DotnetInspect.Cli.Output.VerboseLogger(false),
                packageName: null,
                packageVersion: null,
                httpClient,
                queries: [query],
                queryCatalog: registry.Compile()));

        Assert.Contains("query execution", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact]
    public async Task ProductionQueryCatchBoundary_PreservesCancellation()
    {
        var query = new InspectionQuery<int>("cancelled", InspectionCost.NetworkFree);
        var registry = LibrarySections.CreateQueryRegistry()
            .Add<int>(query, _ => throw new OperationCanceledException("cancelled"));
        using var httpClient = new HttpClient();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            LibraryMetadataService.InspectAsync(
                typeof(SectionPipelineTests).Assembly.Location,
                new LibraryOptions(),
                new DotnetInspect.Cli.Output.VerboseLogger(false),
                packageName: null,
                packageVersion: null,
                httpClient,
                queries: [query],
                queryCatalog: registry.Compile()));
    }

    [Fact]
    public async Task ProductionQueryCatchBoundary_DoesNotSwallowUnknownDemand()
    {
        var query = new InspectionQuery<int>("unregistered", InspectionCost.NetworkFree);
        using var httpClient = new HttpClient();

        var ex = await Assert.ThrowsAsync<InspectionQueryException>(() =>
            LibraryMetadataService.InspectAsync(
                typeof(SectionPipelineTests).Assembly.Location,
                new LibraryOptions(),
                new DotnetInspect.Cli.Output.VerboseLogger(false),
                packageName: null,
                packageVersion: null,
                httpClient,
                queries: [query],
                queryCatalog: LibrarySections.QueryCatalog));

        Assert.Contains("unregistered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryPipeline_ConsultsQueryCosts()
    {
        // Non-vacuity: Performance: Boxing declares no cost of its own and is expensive only
        // because the Optimization Opportunities query behind it is.
        var withCosts = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasMethodBodies = true,
        };

        Assert.DoesNotContain(
            SectionNames.PerformanceBoxing,
            withCosts.GetEffectiveSections(model, Verbosity.Detailed));
    }

    [Fact]
    public void LibrarySections_AboveNetworkFree_AreExplicitlyPinned()
    {
        // GPT review of #3626 caught Switches silently leaving -v:n: its scanner had been declared
        // Moderated, and Moderated means "auto-runs only at -v:d". Nothing failed. The regression
        // was visible only by building origin/main and diffing rendered output, which is far too
        // expensive a way to notice that a section changed verbosity ladder.
        //
        // The literal list is the point: it is a human-reviewed statement of which sections are
        // deliberately not cheap. Any cost change that moves a section across the NetworkFree
        // boundary now fails here and has to be justified in review.
        //
        // The primary assertion is on the pipeline's effective cost — the value the ladder
        // actually consults. A descriptor can raise its own cost above its query, while a query
        // raise always raises the entry.
        var pipeline = LibrarySections.CreatePipeline();

        string[] expectedQueryBodyIndexFamily =
        [
            SectionNames.ArrayPoolEscapes,
            SectionNames.BodyShapes,
            SectionNames.BodyShapeSummary,
            SectionNames.MemberMetrics,
            SectionNames.PerformanceHotspots,
            SectionNames.PerformanceArrays,
            SectionNames.PerformanceAsync,
            SectionNames.PerformanceBoxing,
            SectionNames.PerformanceClosures,
            SectionNames.PerformanceEnumerators,
            SectionNames.PerformanceStrings,
            SectionNames.PerformanceLoops,
            SectionNames.PerformanceOther,
        ];

        // The effective axis: everything the ladder will refuse to auto-render, whichever
        // declaration made it so. Metadata and SourceLink declare their own cost; Integrations
        // inherits the group query's Unbounded cost. This is the honest full set, so either kind
        // of cost declaration crossing the boundary requires an explicit review update.
        string[] expectedAboveCheap =
        [
            .. expectedQueryBodyIndexFamily,
            SectionNames.CloneCandidates,
            SectionNames.LibraryMetrics,
            SectionNames.TopLeverage,
            SectionNames.UnsafeMembers,
            IntegrationSectionNames.Integrations,
            IntegrationSectionNames.Opportunities,
            "Metadata: #Blob",
            "Metadata: #GUID",
            "Metadata: #Strings",
            "Metadata: #US",
            "Metadata: Assembly",
            "Metadata: AssemblyRef",
            "Metadata: Constant",
            "Metadata: CustomAttribute",
            "Metadata: ExportedType",
            "Metadata: Field",
            "Metadata: GenericParam",
            "Metadata: MemberRef",
            "Metadata: MethodDef",
            "Metadata: MethodImpl",
            "Metadata: MethodSpec",
            "Metadata: Module",
            "Metadata: Param",
            "Metadata: StandAloneSig",
            "Metadata: TypeDef",
            "Metadata: TypeRef",
            "Metadata: TypeSpec",
            SectionNames.IdentifierConfusion,
            SectionNames.ReferenceHierarchy,
            SectionNames.SourceLinkAvailability,
            SectionNames.SourceLinkFiles,
            SectionNames.SourceLinkIntegrity,
            SectionNames.SourceLinkMissingFiles,
        ];

        var effectivelyAboveCheap = pipeline.SectionCosts
            .Where(section => section.Cost > SectionCost.NetworkFree)
            .Select(section => section.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            expectedAboveCheap.OrderBy(name => name, StringComparer.Ordinal),
            effectivelyAboveCheap);
    }
}
