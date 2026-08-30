using static CiChangeDetection.GateAssertions;

namespace CiChangeDetection;

internal static class DetectionTestSuite
{
    internal static void Run(
        string repository,
        WorkflowContractResult contract)
    {
        string body = contract.DetectionBody;
        IReadOnlyCollection<string> outputs = contract.Outputs;

        AssertAll(
            RunDetection(
                repository,
                body,
                "pull_request",
                "",
                outputs),
            "true");
        AssertAll(
            RunDetection(repository, body, "push", "", outputs),
            "false");
        AssertAll(
            RunDetection(
                repository,
                body,
                "push",
                "README.md",
                outputs,
                resolutionSucceeds: false),
            "true");
        AssertAll(
            RunDetection(
                repository,
                body,
                "pull_request",
                "README.md",
                outputs,
                reportedChangedFileCount: "2"),
            "true");
        AssertAll(
            RunDetection(
                repository,
                body,
                "pull_request",
                "README.md",
                outputs,
                reportedChangedFileCount:
                    "999999999999999999999999999999999999"),
            "true");
        AssertAll(
            RunDetection(
                repository,
                body,
                "pull_request",
                "README.md",
                outputs,
                reportedChangedFileCount: "1",
                changedFileCountIsString: true),
            "true");
        AssertAll(
            RunDetection(
                repository,
                body,
                "pull_request",
                "README.md",
                outputs,
                resolutionSucceeds: false),
            "true");
        foreach ((string json, string count) in new[]
        {
            ("[null]", "1"),
            ("[{\"status\":\"modified\"}]", "1"),
            ("[{\"status\":\"modified\",\"filename\":[\"src/a.cs\"]}]", "1"),
            ("[" +
                "{\"status\":\"modified\",\"filename\":\"README.md\"}," +
                "{\"status\":\"modified\",\"filename\":\"\"}" +
                "]", "2"),
            ("[{\"filename\":\"src/missing-status.cs\"}]", "1"),
            ("[{\"status\":\"\",\"filename\":\"src/empty-status.cs\"}]", "1"),
            ("[{\"status\":\"renamed\",\"filename\":\"Directory.Build.props.moved\"}]",
                "1"),
            ("[{" +
                "\"status\":\"renamed\"," +
                "\"previous_filename\":\"\"," +
                "\"filename\":\"src/new.cs\"" +
                "}]", "1"),
            ("[{" +
                "\"status\":\"modified\"," +
                "\"previous_filename\":1," +
                "\"filename\":\"README.md\"" +
                "}]", "1"),
            ("[{" +
                "\"status\":\"modified\"," +
                "\"previous_filename\":\"\"," +
                "\"filename\":\"README.md\"" +
                "}]", "1"),
            ("[" +
                "{\"status\":\"modified\",\"filename\":\"README.md\"}," +
                "{\"status\":\"modified\",\"filename\":\"README.md\"}" +
                "]", "2"),
            ("[{" +
                "\"status\":\"renamed\"," +
                "\"previous_filename\":\"README.md\"," +
                "\"filename\":\"README.md\"" +
                "}]", "1"),
            ("[" +
                "{\"status\":\"renamed\",\"previous_filename\":\"src/old.cs\"," +
                "\"filename\":\"src/new-a.cs\"}," +
                "{\"status\":\"renamed\",\"previous_filename\":\"src/old.cs\"," +
                "\"filename\":\"src/new-b.cs\"}" +
                "]", "2"),
            ("[{\"status\":\"modified\",\"filename\":\"/src/Program.cs\"}]",
                "1"),
            ("[{\"status\":\"modified\",\"filename\":\"src/\"}]", "1"),
            ("[{\"status\":\"modified\",\"filename\":\"src//Program.cs\"}]",
                "1"),
            ("[{\"status\":\"modified\",\"filename\":\"./src/Program.cs\"}]",
                "1"),
            ("[{\"status\":\"modified\",\"filename\":\"../src/Program.cs\"}]",
                "1"),
            ("[{\"status\":\"modified\",\"filename\":\"src/./Program.cs\"}]",
                "1"),
            ("[{\"status\":\"modified\",\"filename\":\"src/../Program.cs\"}]",
                "1"),
            ("[{\"status\":\"modified\",\"filename\":\"src/.\"}]", "1"),
            ("[{\"status\":\"modified\",\"filename\":\"src/..\"}]", "1"),
        })
        {
            Dictionary<string, string> malformed = RunDetection(
                repository,
                body,
                "pull_request",
                "README.md",
                outputs,
                reportedChangedFileCount: count,
                malformedFileRecordJson: json);
            AssertAll(malformed, "true");
        }
        AssertAll(
            RunDetection(
                repository,
                body,
                "pull_request",
                "README.md",
                outputs,
                objectShapedFilePage: true),
            "true");
        AssertAll(
            RunDetection(
                repository,
                body,
                "pull_request",
                "README.md",
                outputs,
                truncateRecordStream: true),
            "true");
        AssertAll(
            RunDetection(
                repository,
                body,
                "pull_request",
                "README.md",
                outputs,
                nulFileRecord: true),
            "true");
        AssertAll(
            RunDetection(
                repository,
                body,
                "pull_request",
                "README.md",
                outputs,
                nulPreviousFileRecord: true),
            "true");
        AssertAll(
            RunDetection(
                repository,
                body,
                "push",
                "src/dotnet-inspect/Program.cs",
                outputs,
                truncatePushStream: true),
            "true");
        AssertAll(
            RunDetection(
                repository,
                body,
                "push",
                "",
                outputs,
                emptyPushRecord: true),
            "true");
        foreach (int decode in new[] { 1, 2, 3 })
        {
            AssertAll(
                RunDetection(
                    repository,
                    body,
                    "pull_request",
                    "README.md",
                    outputs,
                    failDecodeAt: decode),
                "true");
        }
        AssertAll(
            RunDetection(
                repository,
                body,
                "push",
                "src/dotnet-inspect/Program.cs",
                outputs,
                failDecodeAt: 1),
            "true");

        AssertAll(
            RunDetection(
                repository,
                body,
                "pull_request",
                "eng/ci-detect-changes.sh",
                outputs),
            "true");

        const string WebProjectsFile =
            "WEB_PROJECTS_FILE=eng/inspect-web-gate-projects.txt";
        const string MissingWebProjectsFile =
            "WEB_PROJECTS_FILE=eng/missing-inspect-web-gate-projects.txt";
        string missingWebManifestBody = body.Replace(
            WebProjectsFile,
            MissingWebProjectsFile,
            StringComparison.Ordinal);
        if (missingWebManifestBody == body)
        {
            throw new InvalidOperationException(
                "Could not redirect the Inspect Web project manifest canary.");
        }
        Dictionary<string, string> missingWebManifest = RunDetection(
            repository,
            missingWebManifestBody,
            "pull_request",
            "src/dotnet-inspect/Program.cs",
            outputs);
        if (missingWebManifest["code"] != "true"
            || missingWebManifest["web"] != "true")
        {
            throw new InvalidOperationException(
                "Missing Inspect Web project manifest did not fail closed: "
                + FormatValues(missingWebManifest));
        }

        Dictionary<string, string> readme =
            RunDetection(
                repository,
                body,
                "pull_request",
                "README.md",
                outputs);
        if (readme["code"] != "false" || readme["docs"] != "true")
        {
            throw new InvalidOperationException(
                $"README.md canary did not discriminate: " +
                $"{FormatValues(readme)}");
        }

        foreach (string status in new[]
        {
            "added",
            "removed",
            "modified",
            "copied",
            "changed",
            "unchanged",
        })
        {
            Dictionary<string, string> statusResult = RunDetection(
                repository,
                body,
                "pull_request",
                "README.md",
                outputs,
                fileStatus: status);
            if (statusResult["code"] != "false" ||
                statusResult["docs"] != "true")
            {
                throw new InvalidOperationException(
                    $"{status} file-record canary did not discriminate: " +
                    FormatValues(statusResult));
            }
        }

        AssertAll(
            RunDetection(
                repository,
                body,
                "pull_request",
                "README.md",
                outputs,
                fileStatus: "future"),
            "true");

        Dictionary<string, string> source = RunDetection(
            repository,
            body,
            "pull_request",
            "src/dotnet-inspect/Program.cs",
            outputs);
        if (source["code"] != "true" || source["web"] != "false")
        {
            throw new InvalidOperationException(
                $"CLI source canary did not select only code: " +
                $"{FormatValues(source)}");
        }

        Dictionary<string, string> windowsInstaller = RunDetection(
            repository,
            body,
            "pull_request",
            "install.ps1",
            outputs);
        if (windowsInstaller["code"] != "true")
        {
            throw new InvalidOperationException(
                "Windows installer canary did not select code: " +
                FormatValues(windowsInstaller));
        }

        Dictionary<string, string> webDependency = RunDetection(
            repository,
            body,
            "pull_request",
            "src/DotnetInspector.Queries/AssemblyContextApiSurfaceQuery.cs",
            outputs);
        if (webDependency["code"] != "true" ||
            webDependency["web"] != "true")
        {
            throw new InvalidOperationException(
                "Web dependency canary did not select code and web: " +
                FormatValues(webDependency));
        }

        Dictionary<string, string> bindingGeneratorDependency = RunDetection(
            repository,
            body,
            "pull_request",
            "src/ILInspector.JsExportSurface/JsExportSurfaceBuilder.cs",
            outputs);
        if (bindingGeneratorDependency["code"] != "true"
            || bindingGeneratorDependency["web"] != "true")
        {
            throw new InvalidOperationException(
                "Web binding-generator dependency canary did not select "
                + "code and web: "
                + FormatValues(bindingGeneratorDependency));
        }

        foreach ((string path, string label) in new[]
        {
            (
                "src/DotnetInspector.Services.Tests/DependencyResolutionServiceTests.cs",
                "DotnetInspector test project"),
            (
                "src/ILInspector.Decompiler.Tests/CSharpBodyDiffTests.cs",
                "ILInspector test project"),
            (
                "src/ILInspector.Analysis.CallerGraphTarget/TargetApi.cs",
                "ILInspector fixture project"),
        })
        {
            Dictionary<string, string> testOnlyProject = RunDetection(
                repository,
                body,
                "pull_request",
                path,
                outputs);
            if (testOnlyProject["code"] != "true"
                || testOnlyProject["web"] != "false")
            {
                throw new InvalidOperationException(
                    $"{label} canary did not select only code: "
                    + FormatValues(testOnlyProject));
            }
        }

        Dictionary<string, string> manifestQueryDependency = RunDetection(
            repository,
            body,
            "pull_request",
            "src/DotnetInspector.Queries/PackageManifestFactsQuery.cs",
            outputs);
        if (manifestQueryDependency["code"] != "true"
            || manifestQueryDependency["web"] != "true")
        {
            throw new InvalidOperationException(
                "Package-manifest query canary did not select code and web: "
                + FormatValues(manifestQueryDependency));
        }

        Dictionary<string, string> sharedWebCompileInput = RunDetection(
            repository,
            body,
            "pull_request",
            "src/UnionPolyfill.cs",
            outputs);
        if (sharedWebCompileInput["code"] != "true"
            || sharedWebCompileInput["web"] != "true")
        {
            throw new InvalidOperationException(
                "Shared web compile-input canary did not select code and web: "
                + FormatValues(sharedWebCompileInput));
        }

        Dictionary<string, string> sharedNetworkPolicy = RunDetection(
            repository,
            body,
            "pull_request",
            "src/NetworkDestinationPolicy.cs",
            outputs);
        if (sharedNetworkPolicy["code"] != "true"
            || sharedNetworkPolicy["web"] != "true")
        {
            throw new InvalidOperationException(
                "Shared network-policy compile-input canary did not select "
                + "code and web: "
                + FormatValues(sharedNetworkPolicy));
        }

        Dictionary<string, string> globalAnalyzerInput = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/BannedSymbols.txt",
            outputs);
        if (globalAnalyzerInput["code"] != "true"
            || globalAnalyzerInput["web"] != "true")
        {
            throw new InvalidOperationException(
                "Global analyzer input canary did not select code and web: "
                + FormatValues(globalAnalyzerInput));
        }

        Dictionary<string, string> webProjectManifest = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/inspect-web-gate-projects.txt",
            outputs);
        if (webProjectManifest["code"] != "true"
            || webProjectManifest["web"] != "true")
        {
            throw new InvalidOperationException(
                "Inspect Web project manifest canary did not select code and web: "
                + FormatValues(webProjectManifest));
        }

        Dictionary<string, string> sourceOraclePreparation = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/prepare-authored-source-oracles.sh",
            outputs);
        if (sourceOraclePreparation["code"] != "true")
        {
            throw new InvalidOperationException(
                "Source-oracle preparation canary did not select code: " +
                FormatValues(sourceOraclePreparation));
        }

        Dictionary<string, string> web = RunDetection(
            repository,
            body,
            "pull_request",
            "prototypes/inspect-web/engine/InspectionEngine.cs",
            outputs);
        if (web["code"] != "false" || web["web"] != "true")
        {
            throw new InvalidOperationException(
                $"Web canary did not select only web: {FormatValues(web)}");
        }
        foreach (string webDocumentation in new[]
        {
            "prototypes/inspect-web/README.md",
            "prototypes/inspect-web/docs/hosting.md",
        })
        {
            Dictionary<string, string> webDocs = RunDetection(
                repository,
                body,
                "pull_request",
                webDocumentation,
                outputs);
            if (webDocs["code"] != "false"
                || webDocs["docs"] != "true"
                || webDocs["web"] != "false")
            {
                throw new InvalidOperationException(
                    $"Web documentation {webDocumentation} selected the wrong " +
                    $"lanes: {FormatValues(webDocs)}");
            }
        }
        Dictionary<string, string> webDocsAndSource = RunDetection(
            repository,
            body,
            "pull_request",
            """
            prototypes/inspect-web/README.md
            prototypes/inspect-web/src/dotnet-inspect.ts
            """,
            outputs);
        if (webDocsAndSource["docs"] != "true"
            || webDocsAndSource["web"] != "true")
        {
            throw new InvalidOperationException(
                "Mixed web documentation and source did not select both lanes: "
                + FormatValues(webDocsAndSource));
        }
        Dictionary<string, string> webSourceRenamedToDocs = RunDetection(
            repository,
            body,
            "pull_request",
            "prototypes/inspect-web/archived-design.md",
            outputs,
            previousFiles: "prototypes/inspect-web/src/archived-design.ts");
        if (webSourceRenamedToDocs["docs"] != "true"
            || webSourceRenamedToDocs["web"] != "true")
        {
            throw new InvalidOperationException(
                "Web source renamed to documentation escaped the web lane: "
                + FormatValues(webSourceRenamedToDocs));
        }
        Dictionary<string, string> webTextFixture = RunDetection(
            repository,
            body,
            "pull_request",
            "prototypes/inspect-web/test/fixture.txt",
            outputs);
        if (webTextFixture["docs"] != "true"
            || webTextFixture["web"] != "true")
        {
            throw new InvalidOperationException(
                "Non-Markdown web fixture escaped the web lane: "
                + FormatValues(webTextFixture));
        }
        Dictionary<string, string> webGenerator = RunDetection(
            repository,
            body,
            "pull_request",
            "src/tsbindgen/JsEmitter.cs",
            outputs);
        if (webGenerator["code"] != "true" || webGenerator["web"] != "true")
        {
            throw new InvalidOperationException(
                "Web generator canary did not select code and web: "
                + FormatValues(webGenerator));
        }
        Dictionary<string, string> webGenerationScript = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/generate-inspect-web-engine-dts.sh",
            outputs);
        if (webGenerationScript["code"] != "false"
            || webGenerationScript["web"] != "true")
        {
            throw new InvalidOperationException(
                "Web generation-script canary did not select only web: "
                + FormatValues(webGenerationScript));
        }
        Dictionary<string, string> methodSemanticsProbeRunner = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/run-method-semantics-platform-probe.sh",
            outputs);
        if (methodSemanticsProbeRunner["code"] != "true"
            || methodSemanticsProbeRunner["web"] != "true")
        {
            throw new InvalidOperationException(
                "MethodSemantics platform-probe runner did not select code and web: "
                + FormatValues(methodSemanticsProbeRunner));
        }
        Dictionary<string, string> methodSemanticsProbeSource = RunDetection(
            repository,
            body,
            "pull_request",
            "tests/ILInspector.MetadataPrimitives.PlatformProbe/wwwroot/main.js",
            outputs);
        if (methodSemanticsProbeSource["code"] != "true"
            || methodSemanticsProbeSource["web"] != "true")
        {
            throw new InvalidOperationException(
                "MethodSemantics platform-probe source did not select code and web: "
                + FormatValues(methodSemanticsProbeSource));
        }
        foreach (string promotionInput in new[]
        {
            ".github/workflows/deploy-inspect-web.yml",
            ".github/workflows/deploy-inspect-web-coreclr.yml",
            ".github/workflows/promote-inspect-web.yml",
            "eng/validate-inspect-web-promotion.cs",
            "eng/validate-inspect-web-promotion.sh",
        })
        {
            Dictionary<string, string> promotion = RunDetection(
                repository,
                body,
                "pull_request",
                promotionInput,
                outputs);
            if (promotion["code"] != "false" || promotion["web"] != "true")
            {
                throw new InvalidOperationException(
                    $"Promotion input {promotionInput} did not select only web: " +
                    FormatValues(promotion));
            }
        }
        Dictionary<string, string> promotionContract = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/CiChangeDetection/PromotionWorkflowContract.cs",
            outputs);
        if (promotionContract["code"] != "true" ||
            promotionContract["web"] != "true")
        {
            throw new InvalidOperationException(
                "Promotion workflow contract did not select code and web: " +
                FormatValues(promotionContract));
        }
        AssertRouting(
            source,
            selected: "shipped",
            notSelected: "csharpdiff");
        AssertRouting(
            source,
            selected: "shipped",
            notSelected: "decompiler");

        Dictionary<string, string> cliTests = RunDetection(
            repository,
            body,
            "pull_request",
            "src/dotnet-inspect.Tests/CommandExecutionTests.cs",
            outputs);
        AssertRouting(
            cliTests,
            selected: "code",
            notSelected: "decompiler");

        Dictionary<string, string> decompilerSubstrate = RunDetection(
            repository,
            body,
            "pull_request",
            "src/ILInspector.MetadataPrimitives/TypeName.cs",
            outputs);
        AssertRouting(
            decompilerSubstrate,
            selected: "decompiler",
            notSelected: "packaging");

        Dictionary<string, string> decompilerFixture = RunDetection(
            repository,
            body,
            "pull_request",
            "src/ILInspector.Decompiler.Fixtures.ClassicAsync/Fixture.cs",
            outputs);
        AssertRouting(
            decompilerFixture,
            selected: "decompiler",
            notSelected: "packaging");

        Dictionary<string, string> missingDecompilerSkipList = RunDetection(
            repository,
            body.Replace(
                "eng/decompiler-gate-skip-projects.txt",
                "eng/missing-decompiler-gate-skip-projects.txt",
                StringComparison.Ordinal),
            "pull_request",
            "src/dotnet-inspect/Program.cs",
            outputs);
        if (missingDecompilerSkipList["decompiler"] != "true")
        {
            throw new InvalidOperationException(
                "Missing decompiler project skip list did not fail safe: " +
                FormatValues(missingDecompilerSkipList));
        }

        Dictionary<string, string> multipleFiles = RunDetection(
            repository,
            body,
            "pull_request",
            "src/dotnet-inspect/Program.cs\nREADME.md",
            outputs);
        if (multipleFiles["code"] != "true" ||
            multipleFiles["docs"] != "true" ||
            multipleFiles["packaging"] != "false")
        {
            throw new InvalidOperationException(
                "Distinct multi-file canary did not discriminate: " +
                FormatValues(multipleFiles));
        }

        Dictionary<string, string> platformTypeRouting = RunDetection(
            repository,
            body,
            "pull_request",
            """
            docs/workflows/getting-started/type-and-member-addressability.md
            src/dotnet-inspect.Tests/CommandExecutionTests.cs
            src/dotnet-inspect/CommandLine/Commands/RouterCommandDefinition.cs
            """,
            outputs);
        if (platformTypeRouting["code"] != "true"
            || platformTypeRouting["docs"] != "true"
            || platformTypeRouting["decompiler"] != "false")
        {
            throw new InvalidOperationException(
                "Platform type routing canary selected the wrong lanes: " +
                FormatValues(platformTypeRouting));
        }

        Dictionary<string, string> csharpDiff = RunDetection(
            repository,
            body,
            "pull_request",
            "tools/CSharpDiffHarness/Program.cs",
            outputs);
        AssertRouting(
            csharpDiff,
            selected: "csharpdiff",
            notSelected: "code");

        Dictionary<string, string> decompiler = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/check-decompiler-gate.cs",
            outputs);
        AssertRouting(
            decompiler,
            selected: "decompiler",
            notSelected: "code");

        Dictionary<string, string> ilDiff = RunDetection(
            repository,
            body,
            "pull_request",
            "tools/IlDiffHarness/Program.cs",
            outputs);
        AssertRouting(
            ilDiff,
            selected: "ildiff",
            notSelected: "code");

        Dictionary<string, string> ilDiffOwner = RunDetection(
            repository,
            body,
            "pull_request",
            "src/ILInspector.ILDiff/IlBodyDiff.cs",
            outputs);
        if (ilDiffOwner["code"] != "true"
            || ilDiffOwner["csharpdiff"] != "true"
            || ilDiffOwner["ildiff"] != "true")
        {
            throw new InvalidOperationException(
                "IL diff owner routing canary skipped a required lane: " +
                FormatValues(ilDiffOwner));
        }

        Dictionary<string, string> ilRoundtrip = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/restore-ilassembler.sh",
            outputs);
        AssertRouting(
            ilRoundtrip,
            selected: "ilroundtrip",
            notSelected: "docs");
        if (ilRoundtrip["code"] != "true")
        {
            throw new InvalidOperationException(
                "IL round-trip canary did not start its containing test job: " +
                FormatValues(ilRoundtrip));
        }

        Dictionary<string, string> packaging = RunDetection(
            repository,
            body,
            "pull_request",
            "src/dotnet-inspect/dotnet-inspect.csproj",
            outputs);
        AssertRouting(
            packaging,
            selected: "packaging",
            notSelected: "docs");

        Dictionary<string, string> packageFixture = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/package-fixtures/tool-v2/1.0.0/pointer.nuspec",
            outputs);
        AssertRouting(
            packageFixture,
            selected: "code",
            notSelected: "packaging");

        Dictionary<string, string> metadataPackageFixture = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/package-fixtures/metadata-confusion/1.0.0/metadata-confusion.nuspec",
            outputs);
        AssertRouting(
            metadataPackageFixture,
            selected: "code",
            notSelected: "packaging");

        Dictionary<string, string> packageManifestCorpus = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/package-manifest-corpus.json",
            outputs);
        AssertRouting(
            packageManifestCorpus,
            selected: "code",
            notSelected: "docs");

        Dictionary<string, string> packageManifestCorpusVerifier = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/verify-package-manifest-corpus.cs",
            outputs);
        AssertRouting(
            packageManifestCorpusVerifier,
            selected: "code",
            notSelected: "docs");

        Dictionary<string, string> workflow = RunDetection(
            repository,
            body,
            "pull_request",
            ".github/workflows/ci.yml",
            outputs);
        if (workflow["code"] != "true" || workflow["skills"] != "true")
        {
            throw new InvalidOperationException(
                "Workflow canary did not select code and skills: " +
                FormatValues(workflow));
        }

        Dictionary<string, string> detectionGate = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/test-ci-change-detection.cs",
            outputs);
        AssertRouting(
            detectionGate,
            selected: "code",
            notSelected: "docs");

        Dictionary<string, string> detectionOwner = RunDetection(
            repository,
            body,
            "pull_request",
            "eng/CiChangeDetection/DetectionTestSuite.cs",
            outputs);
        AssertRouting(
            detectionOwner,
            selected: "code",
            notSelected: "docs");

        Dictionary<string, string> skill = RunDetection(
            repository,
            body,
            "pull_request",
            "skills/new-skill/SKILL.md",
            outputs);
        if (skill["code"] != "false"
            || skill["docs"] != "true"
            || skill["skills"] != "true")
        {
            throw new InvalidOperationException(
                "Skill canary did not select only docs and skills: " +
                FormatValues(skill));
        }

        Dictionary<string, string> skillSupportDoc = RunDetection(
            repository,
            body,
            "pull_request",
            "skills/workflow-scenarios/validating-workflows.md",
            outputs);
        if (skillSupportDoc["code"] != "false"
            || skillSupportDoc["docs"] != "true"
            || skillSupportDoc["skills"] != "false")
        {
            throw new InvalidOperationException(
                "Skill support document canary selected the wrong lanes: " +
                FormatValues(skillSupportDoc));
        }

        Dictionary<string, string> nestedSkillSupportDoc = RunDetection(
            repository,
            body,
            "pull_request",
            "skills/workflow-scenarios/examples/SKILL.md",
            outputs);
        if (nestedSkillSupportDoc["code"] != "false"
            || nestedSkillSupportDoc["docs"] != "true"
            || nestedSkillSupportDoc["skills"] != "false")
        {
            throw new InvalidOperationException(
                "Nested skill support document canary selected the wrong lanes: " +
                FormatValues(nestedSkillSupportDoc));
        }

        Dictionary<string, string> pushedSource = RunDetection(
            repository,
            body,
            "push",
            "src/dotnet-inspect/Program.cs",
            outputs);
        if (pushedSource["code"] != "true")
        {
            throw new InvalidOperationException(
                $"Pushed source canary did not select code: " +
                $"{FormatValues(pushedSource)}");
        }

        Dictionary<string, string> pushedWebDependency = RunDetection(
            repository,
            body,
            "push",
            "src/DotnetInspector.Queries/AssemblyContextApiSurfaceQuery.cs",
            outputs);
        if (pushedWebDependency["code"] != "true" ||
            pushedWebDependency["web"] != "true")
        {
            throw new InvalidOperationException(
                "Pushed web dependency did not select code and web: " +
                FormatValues(pushedWebDependency));
        }

        Dictionary<string, string> mergeGroupWebDependency = RunDetection(
            repository,
            body,
            "merge_group",
            "src/DotnetInspector.Queries/AssemblyContextApiSurfaceQuery.cs",
            outputs);
        if (mergeGroupWebDependency.Count != webDependency.Count ||
            mergeGroupWebDependency.Any(item =>
                !webDependency.TryGetValue(
                    item.Key,
                    out string? expected) ||
                item.Value != expected))
        {
            throw new InvalidOperationException(
                "Merge-group web dependency did not match PR routing: " +
                FormatValues(mergeGroupWebDependency));
        }

        Dictionary<string, string> unicodeSource = RunDetection(
            repository,
            body,
            "pull_request",
            "src/dotnet-inspect/\u00E9.cs",
            outputs);
        if (unicodeSource["code"] != "true")
        {
            throw new InvalidOperationException(
                $"Unicode source canary did not select code: " +
                $"{FormatValues(unicodeSource)}");
        }

        Dictionary<string, string> renamedBuildInput = RunDetection(
            repository,
            body,
            "pull_request",
            "notes/renamed.txt",
            outputs,
            previousFiles: "Directory.Build.props");
        if (renamedBuildInput["code"] != "true")
        {
            throw new InvalidOperationException(
                "Renamed build-input canary did not select code: " +
                FormatValues(renamedBuildInput));
        }

        int recordEncoding = body.IndexOf(
            "| @json",
            StringComparison.Ordinal);
        int recordBase64 = recordEncoding < 0
            ? -1
            : body.IndexOf(
                "| @base64",
                recordEncoding,
                StringComparison.Ordinal);
        if (recordBase64 < 0)
        {
            throw new InvalidOperationException(
                "Could not construct the gh invocation mutation.");
        }
        string brokenGhInvocation = body.Remove(
            recordBase64,
            "| @base64".Length);

        Dictionary<string, string> brokenGh = RunDetection(
            repository,
            brokenGhInvocation,
            "pull_request",
            "README.md",
            outputs);
        if (brokenGh["code"] == "false" && brokenGh["docs"] == "true")
        {
            throw new InvalidOperationException(
                "The fake gh accepted a broken invocation.");
        }

        GateAssertions.AssertDetectionFails(
            new DetectionHarness(
                repository,
                $"false{Environment.NewLine}{body}",
                outputs),
            new DetectionScenario("pull_request", "README.md"));
    }

    private static Dictionary<string, string> RunDetection(
        string repository,
        string body,
        string eventName,
        string files,
        IReadOnlyCollection<string> expectedOutputs,
        string previousFiles = "",
        string? reportedChangedFileCount = null,
        bool changedFileCountIsString = false,
        bool resolutionSucceeds = true,
        string malformedFileRecordJson = "",
        bool objectShapedFilePage = false,
        bool nulFileRecord = false,
        bool nulPreviousFileRecord = false,
        string fileStatus = "modified",
        int failDecodeAt = 0,
        bool truncateRecordStream = false,
        bool truncatePushStream = false,
        bool emptyPushRecord = false) =>
        new DetectionHarness(repository, body, expectedOutputs).Run(
            new DetectionScenario(
                eventName,
                files,
                previousFiles,
                reportedChangedFileCount,
                changedFileCountIsString,
                resolutionSucceeds,
                malformedFileRecordJson,
                objectShapedFilePage,
                nulFileRecord,
                nulPreviousFileRecord,
                fileStatus,
                failDecodeAt,
                truncateRecordStream,
                truncatePushStream,
                emptyPushRecord));
}
