using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static class InspectWebDeploymentWorkflowContract
{
    private const string AzureAction =
        "Azure/static-web-apps-deploy@1a947af9992250f3bc2e68ad0754c0b0c11566c9";
    private const string CheckoutAction =
        "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1";
    private const string DownloadArtifactAction =
        "actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c";
    private const string SetupDotnetAction =
        "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68";
    private const string SetupNodeAction =
        "actions/setup-node@820762786026740c76f36085b0efc47a31fe5020";
    private const string UploadArtifactAction =
        "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a";
    private const string CompilerAsyncDeploymentCheck =
        """
        eng/verify-inspect-web-async-deployment.sh \
          compiler \
          artifacts/bin/DotnetInspect.Web/release_browser-wasm/DotnetInspect.Web.dll \
          artifacts/inspect-web-publish/wwwroot \
          artifacts/inspect-web-publish/async-lowering.json \
          artifacts/inspect-web-compiler-async-receipts
        """;
    private const string RuntimeAsyncDeploymentCheck =
        """
        RestoreConfigFile="$RUNNER_TEMP/inspect-web-coreclr-NuGet.Config" \
          eng/verify-inspect-web-async-deployment.sh \
            runtime \
            artifacts/bin/DotnetInspect.Web/release_browser-wasm/DotnetInspect.Web.dll \
            artifacts/inspect-web-coreclr-publish/wwwroot \
            artifacts/inspect-web-coreclr-publish/async-lowering.json \
            artifacts/inspect-web-runtime-async-receipts
        """;
    private const string PairedAsyncDeploymentCheck =
        """
        eng/verify-inspect-web-async-deployment.sh \
          --compare \
          artifacts/inspect-web-compiler-publish/async-lowering.json \
          artifacts/inspect-web-coreclr-publish/async-lowering.json
        """;
    private const string CoreClrPackageOperationCheck =
        """
        INSPECT_WEB_PACKAGE_ADOPTION_SITE="$GITHUB_WORKSPACE/artifacts/inspect-web-coreclr-publish/wwwroot" \
          eng/test-inspect-web-package-adoption-gate.sh \
            --grep 'drives the production opening, join, occurrence, and rejection contracts'
        """;
    internal static void AssertMutations(string repository)
    {
        string stagingPath = Path.Combine(
            repository,
            ".github",
            "workflows",
            "deploy-inspect-web.yml");
        string runtimeSitesPath = Path.Combine(
            repository,
            ".github",
            "workflows",
            "deploy-inspect-web-runtime-sites.yml");
        string runtimeCohortPath = Path.Combine(
            repository,
            ".github",
            "workflows",
            "inspect-web-runtime-cohort-nightly.yml");
        string runtimePinProposalPath = Path.Combine(
            repository,
            ".github",
            "workflows",
            "inspect-web-runtime-pin-proposal.yml");
        string asyncVerifierPath = Path.Combine(
            repository,
            "eng",
            "verify-inspect-web-async-deployment.sh");
        string asyncLoweringReceiptTargetPath = Path.Combine(
            repository,
            "eng",
            "InspectWebAsyncLoweringReceipt.targets");
        string stagingWorkflow = File.ReadAllText(stagingPath);
        string runtimeSitesWorkflow = File.ReadAllText(runtimeSitesPath);
        string runtimeCohortWorkflow = File.ReadAllText(runtimeCohortPath);
        string runtimePinProposalWorkflow =
            File.ReadAllText(runtimePinProposalPath);
        string asyncVerifier = File.ReadAllText(asyncVerifierPath);
        string asyncLoweringReceiptTarget =
            File.ReadAllText(asyncLoweringReceiptTargetPath);
        ValidateStaging(stagingWorkflow);
        ValidateRuntimeSiteCandidateIdentity(
            runtimeSitesWorkflow,
            runtimeCohortWorkflow);
        ValidateRuntimeSdkGlobalJsonOverride(
            runtimeCohortWorkflow,
            "$DOTNET_SDK_VERSION",
            "runtime cohort");
        ValidateRuntimeSdkGlobalJsonOverride(
            runtimePinProposalWorkflow,
            "$candidate_sdk",
            "runtime pin proposal");
        ValidateAsyncDeploymentVerifier(asyncVerifier);
        ValidateAsyncLoweringReceiptTarget(asyncLoweringReceiptTarget);
        InspectWebAsyncDeployment_ReceiptsCoverExactFacadeSet(asyncVerifier);
        InspectWebAsyncDeployment_LoweringsPreserveFacadeContracts(asyncVerifier);

        AssertMutationRejected(
            runtimeCohortWorkflow,
            "              \"version\": \"$DOTNET_SDK_VERSION\",\n",
            "              \"version\": \"11.0.100-rc.1.26425.128\",\n",
            workflow => ValidateRuntimeSdkGlobalJsonOverride(
                workflow,
                "$DOTNET_SDK_VERSION",
                "runtime cohort"),
            "Runtime cohort contract accepted the repository SDK in the candidate SDK root.");
        AssertMutationRejected(
            runtimePinProposalWorkflow,
            "              \"version\": \"$candidate_sdk\",\n",
            "              \"version\": \"11.0.100-rc.1.26425.128\",\n",
            workflow => ValidateRuntimeSdkGlobalJsonOverride(
                workflow,
                "$candidate_sdk",
                "runtime pin proposal"),
            "Runtime pin proposal contract accepted the repository SDK in the candidate SDK root.");
        AssertMutationRejected(
            runtimeSitesWorkflow,
            "          eng/validate-release-candidate.sh \\\n",
            "          true \\\n",
            workflow => ValidateRuntimeSiteCandidateIdentity(
                workflow,
                runtimeCohortWorkflow),
            "Runtime sites accepted an unvalidated candidate identity.");
        AssertMutationRejected(
            runtimeSitesWorkflow,
            "          global-json-file: global.json\n",
            "          dotnet-version: 10.0.x\n",
            workflow => ValidateRuntimeSiteCandidateIdentity(
                workflow,
                runtimeCohortWorkflow),
            "Runtime sites accepted validation without the candidate-pinned SDK.");
        AssertMutationRejected(
            runtimeCohortWorkflow,
            "          ref: ${{ inputs.source_sha }}\n",
            "          ref: ${{ github.sha }}\n",
            workflow => ValidateRuntimeSiteCandidateIdentity(
                runtimeSitesWorkflow,
                workflow),
            "Runtime cohort accepted a checkout outside the selected candidate SHA.");

        const string stagingDownload =
            """
                steps:
                  - name: Download staged site artifact
            """;
        const string stagingCheckout =
            """
                steps:
                  - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1

                  - name: Download staged site artifact
            """;
        AssertMutationRejected(
            stagingWorkflow,
            stagingDownload,
            stagingCheckout,
            ValidateStaging,
            "Staging workflow contract accepted candidate code in the deployment job.");

        AssertMutationRejected(
            stagingWorkflow,
            "          test \"$import_map_line\" -lt \"$module_line\"\n",
            "          test \"$import_map_line\" -lt \"$module_line\" || true\n",
            ValidateStaging,
            "Staging workflow contract accepted disabled import-map verification.");
        AssertMutationRejected(
            stagingWorkflow,
            "            test -f \"$site/$asset\"\n",
            "            test -f \"$site/$asset\" || true\n",
            ValidateStaging,
            "Staging workflow contract accepted disabled Vite asset verification.");
        AssertMutationRejected(
            stagingWorkflow,
            "            [[ \"$asset\" =~ ^assets/([A-Za-z0-9_-][A-Za-z0-9._-]*/)*[A-Za-z0-9_-][A-Za-z0-9._-]*$ ]]\n",
            "",
            ValidateStaging,
            "Staging workflow contract accepted a deleted Vite asset path constraint.");
        AssertMutationRejected(
            stagingWorkflow,
            "          jq -e '. as $manifest | type == \"object\" and (.[\"index.html\"] | type == \"object\") and all(to_entries[]; (.value | type == \"object\") and all(((.value.imports // []) + (.value.dynamicImports // []))[]; . as $key | $manifest | has($key)))' \"$manifest\" >/dev/null\n",
            "",
            ValidateStaging,
            "Staging workflow contract accepted unresolved Vite manifest imports.");
        AssertMutationRejected(
            stagingWorkflow,
            "          skip_app_build: true\n",
            "",
            ValidateStaging,
            "Staging workflow contract accepted Azure app build.");
        AssertMutationRejected(
            stagingWorkflow,
            "          include-hidden-files: true\n",
            "",
            ValidateStaging,
            "Staging workflow contract accepted an artifact without hidden Function dependencies.");
        AssertMutationRejected(
            stagingWorkflow,
            "          overwrite: true\n",
            "",
            ValidateStaging,
            "Staging workflow contract accepted a non-rerun-safe artifact upload.");
        AssertMutationRejected(
            stagingWorkflow,
            "artifacts/bin/DotnetInspect.Web/release_browser-wasm/DotnetInspect.Web.dll",
            "artifacts/obj/DotnetInspect.Web/release_browser-wasm/linked/DotnetInspect.Web.dll",
            ValidateStaging,
            "Staging contract accepted async evidence from the wrong assembly.");
        AssertMutationRejected(
            asyncVerifier,
            "  \"$repo_root/inspect-web/scripts/verify-published-engine-facades.ts\" \\\n  \"$site\" \\\n  deployment \\\n  \"$domain\" \\\n  \"$smoke_result\"\n",
            "",
            ValidateAsyncDeploymentVerifier,
            "Async deployment verifier accepted a skipped browser invocation.");
        AssertMutationRejected(
            asyncVerifier,
            "    async_method_count: census.async_method_count,\n",
            "",
            ValidateAsyncDeploymentVerifier,
            "Async deployment verifier accepted a receipt without the async census.");
        AssertMutationRejected(
            asyncVerifier,
            "    repository_project_count: graph.repository_project_count,\n",
            "",
            ValidateAsyncDeploymentVerifier,
            "Async deployment verifier accepted a receipt without the project count.");
        AssertMutationRejected(
            asyncVerifier,
            "    generated_source_sha256: sha256(sourcePath),\n",
            "",
            ValidateAsyncDeploymentVerifier,
            "Async deployment verifier accepted a receipt without per-facade source identity.");
        AssertMutationRejected(
            asyncVerifier,
            "    published_js_sha256: sha256(javascriptPath),\n",
            "",
            ValidateAsyncDeploymentVerifier,
            "Async deployment verifier accepted a receipt without per-facade JavaScript identity.");
        AssertMutationRejected(
            asyncVerifier,
            "  \"DotnetInspect.Web.Interop.Source\",\n",
            "",
            InspectWebAsyncDeployment_ReceiptsCoverExactFacadeSet,
            "Async deployment receipt contract accepted an omitted source facade.");
        AssertMutationRejected(
            asyncVerifier,
            "  published_js_sha256: assembly.published_js_sha256,\n",
            "",
            InspectWebAsyncDeployment_LoweringsPreserveFacadeContracts,
            "Async deployment parity contract accepted unequal published JavaScript.");
        AssertMutationRejected(
            asyncLoweringReceiptTarget,
            "Condition=\"'$(InspectWebExpectedAsyncLowering)' == 'runtime' And $([System.String]::Copy(';$(Features);').Contains(';runtime-async=on;')) != 'True'\"",
            "Condition=\"false\"",
            ValidateAsyncLoweringReceiptTarget,
            "Async-lowering receipt target accepted runtime projects without the feature.");
        AssertMutationRejected(
            asyncLoweringReceiptTarget,
            "Condition=\"'$(InspectWebExpectedAsyncLowering)' == 'compiler' And $([System.String]::Copy(';$(Features);').Contains(';runtime-async=on;')) == 'True'\"",
            "Condition=\"false\"",
            ValidateAsyncLoweringReceiptTarget,
            "Async-lowering receipt target accepted compiler projects with the feature.");
        AssertMutationRejected(
            stagingWorkflow,
            "  workflow_dispatch:\n",
            "  workflow_dispatch:\n  pull_request_target:\n",
            ValidateStaging,
            "Staging workflow contract accepted pull_request_target.");
        AssertMutationRejected(
            stagingWorkflow,
            "permissions:\n  contents: read\n",
            "permissions:\n  contents: write\n",
            ValidateStaging,
            "Staging workflow contract accepted write permission.");
        AssertMutationRejected(
            stagingWorkflow,
            "  cancel-in-progress: false\n",
            "  cancel-in-progress: true\n",
            ValidateStaging,
            "Staging workflow contract accepted cancellation of an active deployment.");
        AssertMutationRejected(
            stagingWorkflow,
            "    steps:\n      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1\n",
            """
                steps:
                  - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
                    with:
                      ref: ${{ github.event.pull_request.head.sha }}
            """,
            ValidateStaging,
            "Staging workflow contract accepted PR-head checkout.");
    }

    private static void ValidateRuntimeSiteCandidateIdentity(
        string runtimeSitesWorkflow,
        string runtimeCohortWorkflow)
    {
        string[] runtimeSiteRequirements =
        [
            "  workflow_run:\n",
            "      - Nightly release candidate\n",
            "    name: Select completed candidate\n",
            "          ref: ${{ steps.trigger.outputs.sha }}\n"
                + "\n"
                + "      - name: Setup candidate .NET SDK\n"
                + "        uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0\n"
                + "        with:\n"
                + "          global-json-file: global.json\n"
                + "\n"
                + "      - name: Validate candidate identity\n",
            "          eng/validate-release-candidate.sh \\\n",
            "      source_sha: ${{ needs.source.outputs.sha }}\n",
            "      candidate_run_id: ${{ needs.source.outputs.run_id }}\n",
            "      candidate_attempt: ${{ needs.source.outputs.run_attempt }}\n",
            "            --candidate-identity \"$RUNNER_TEMP/runtime-site/evidence/candidate-identity.json\" \\\n",
            "            --candidate-run-id \"${{ needs.source.outputs.run_id }}\" \\\n",
            "            --candidate-attempt \"${{ needs.source.outputs.run_attempt }}\" \\\n",
            "              .schema == 2\n",
            "              and .candidate == {\n",
        ];
        string[] missingRuntimeSiteRequirements = runtimeSiteRequirements
            .Where(value =>
                !runtimeSitesWorkflow.Contains(value, StringComparison.Ordinal))
            .ToArray();
        if (missingRuntimeSiteRequirements.Length != 0
            || Count(
                runtimeSitesWorkflow,
                "          ref: ${{ needs.source.outputs.sha }}\n") != 2
            || Count(runtimeSitesWorkflow, "              .schema == 2\n") != 2
            || Count(
                runtimeSitesWorkflow,
                "              and .candidate == {\n") != 2)
        {
            throw new InvalidOperationException(
                "Runtime-site workflow does not bind deployment to one validated "
                + "candidate identity. Missing: ["
                + string.Join(", ", missingRuntimeSiteRequirements)
                + "].");
        }

        string[] runtimeCohortRequirements =
        [
            "      source_sha:\n",
            "        description: Exact source commit to build\n",
            "          ref: ${{ inputs.source_sha }}\n",
            "            -p:SourceRevisionId=\"${{ inputs.source_sha }}\" \\\n",
            "            --source-commit \"${{ inputs.source_sha }}\" \\\n",
            "              }' > \"$RUNNER_TEMP/runtime-cohort/evidence/candidate-identity.json\"\n",
        ];
        string[] missingRuntimeCohortRequirements = runtimeCohortRequirements
            .Where(value =>
                !runtimeCohortWorkflow.Contains(value, StringComparison.Ordinal))
            .ToArray();
        if (missingRuntimeCohortRequirements.Length != 0
            || Count(
                runtimeCohortWorkflow,
                "          ref: ${{ inputs.source_sha }}\n") != 4
            || Count(
                runtimeCohortWorkflow,
                "            -p:SourceRevisionId=\"${{ inputs.source_sha }}\" \\\n") != 2
            || Count(
                runtimeCohortWorkflow,
                "          dotnet publish \\\n"
                    + "            src/DotnetInspect.Web/DotnetInspect.Web.csproj \\\n") != 2
            || Count(
                runtimeCohortWorkflow,
                "            -p:InspectWebIncludeFrontend=true \\\n") != 2
            || Count(
                runtimeCohortWorkflow,
                "            --source-commit \"${{ inputs.source_sha }}\" \\\n") != 1)
        {
            throw new InvalidOperationException(
                "Runtime cohort does not preserve the selected source and "
                + "candidate identity. Missing: ["
                + string.Join(", ", missingRuntimeCohortRequirements)
                + "].");
        }
    }

    private static int Count(string value, string expected) =>
        value.Split(expected, StringSplitOptions.None).Length - 1;

    private static void ValidateRuntimeSdkGlobalJsonOverride(
        string workflow,
        string sdkVersionExpression,
        string context)
    {
        string expected =
            "          cat > global.json <<EOF\n" +
            "          {\n" +
            "            \"sdk\": {\n" +
            "              \"version\": \"__SDK_VERSION__\",\n" +
            "              \"rollForward\": \"disable\",\n" +
            "              \"allowPrerelease\": true\n" +
            "            }\n" +
            "          }\n" +
            "          EOF\n";
        expected = expected.Replace(
                    "__SDK_VERSION__",
                    sdkVersionExpression,
                    StringComparison.Ordinal);
        if (!workflow.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{context} must select its installed SDK through global.json.");
        }
    }


    private static void ValidateStaging(string workflow)
    {
        using TextReader reader = new StringReader(workflow);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one staging workflow document, found {yaml.Documents.Count}.");
        }

        YamlMappingNode root = RequireMapping(
            yaml.Documents[0].RootNode,
            "staging workflow root");
        RequireExactKeys(
            root,
            ["name", "on", "permissions", "concurrency", "env", "jobs"],
            "staging workflow");
        RequireScalarValue(
            root,
            "name",
            "Deploy inspect-web staging",
            "staging workflow");
        ValidateStagingTrigger(GetRequiredMapping(root, "on", "staging workflow"));
        RequireExactScalarValues(
            GetRequiredMapping(root, "permissions", "staging workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["contents"] = "read",
            },
            "staging workflow.permissions");
        RequireExactScalarValues(
            GetRequiredMapping(root, "concurrency", "staging workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["group"] = "deploy-inspect-web-staging",
                ["cancel-in-progress"] = "false",
            },
            "staging workflow.concurrency");
        RequireExactScalarValues(
            GetRequiredMapping(root, "env", "staging workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true",
                ["DOTNET_NOLOGO"] = "true",
                ["DOTNET_SDK_VERSION"] = "11.0.100-rc.1.26425.128",
            },
            "staging workflow.env");
        YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "staging workflow");
        RequireExactKeys(jobs, ["build", "deploy"], "staging jobs");
        YamlMappingNode build = GetRequiredMapping(jobs, "build", "staging jobs");
        RequireExactKeys(
            build,
            ["name", "if", "runs-on", "steps"],
            "jobs.build");
        RequireScalarValue(
            build,
            "name",
            "Build staging artifact",
            "jobs.build");
        RequireScalarValue(
            build,
            "if",
            "github.ref == 'refs/heads/main'",
            "jobs.build");
        RequireScalarValue(build, "runs-on", "ubuntu-26.04", "jobs.build");
        YamlSequenceNode buildSteps = GetRequiredSequence(build, "steps", "jobs.build");
        if (buildSteps.Children.Count != 10)
        {
            throw new InvalidOperationException(
                "Staging build must contain checkout, .NET and Node setup, " +
                "workload install, frontend build, site and API publish, async and " +
                "artifact verification, and artifact upload steps.");
        }
        YamlMappingNode checkout =
            RequireStep(buildSteps, 0, null, "jobs.build");
        RequireExactKeys(checkout, ["uses"], "staging build checkout");
        RequireScalarValue(
            checkout,
            "uses",
            CheckoutAction,
            "staging build checkout");

        YamlMappingNode setup =
            RequireStep(buildSteps, 1, "Setup .NET", "jobs.build");
        RequireExactKeys(setup, ["name", "uses", "with"], "staging setup step");
        RequireScalarValue(
            setup,
            "uses",
            SetupDotnetAction,
            "staging setup step");
        RequireExactScalarValues(
            GetRequiredMapping(setup, "with", "staging setup step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dotnet-version"] = "${{ env.DOTNET_SDK_VERSION }}",
            },
            "staging setup step.with");

        YamlMappingNode setupNode =
            RequireStep(buildSteps, 2, "Setup Node", "jobs.build");
        RequireExactKeys(
            setupNode,
            ["name", "uses", "with"],
            "staging Node setup step");
        RequireScalarValue(
            setupNode,
            "uses",
            SetupNodeAction,
            "staging Node setup step");
        RequireExactScalarValues(
            GetRequiredMapping(setupNode, "with", "staging Node setup step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["node-version"] = "24",
                ["cache"] = "npm",
                ["cache-dependency-path"] = "inspect-web/package-lock.json",
            },
            "staging Node setup step.with");

        YamlMappingNode install =
            RequireStep(buildSteps, 3, "Install browser Wasm workload", "jobs.build");
        RequireExactScalarValues(
            install,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "Install browser Wasm workload",
                ["run"] = "dotnet workload install wasm-experimental",
            },
            "staging workload step");

        YamlMappingNode frontend =
            RequireStep(buildSteps, 4, "Build browser frontend", "jobs.build");
        RequireExactScalarValues(
            frontend,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "Build browser frontend",
                ["working-directory"] = "inspect-web",
                ["run"] =
                    "npm ci\n" +
                    "npm run build\n" +
                    "grep -q '<script type=\"importmap\"></script>' dist/index.html\n" +
                    "grep -Eq '<link rel=\"preload\" id=\"webassembly\"[[:space:]]*/?>' dist/index.html\n",
            },
            "staging frontend build step");

        YamlMappingNode publish =
            RequireStep(buildSteps, 5, "Publish browser app", "jobs.build");
        RequireExactKeys(publish, ["name", "shell", "run"], "staging publish step");
        RequireScalarValue(publish, "shell", "bash", "staging publish step");
        const string ExpectedPublish =
            """
            rm -rf artifacts/inspect-web-compiler-async-receipts
            version=$(dotnet msbuild src/DotnetInspect.Cli/DotnetInspect.Cli.csproj -getProperty:VersionPrefix -nologo)
            built_at=$(date -u +'%Y-%m-%dT%H:%M:%SZ')
            dotnet publish \
              src/DotnetInspect.Web/DotnetInspect.Web.csproj \
              -c Release \
              --output artifacts/inspect-web-publish \
              -p:InspectWebIncludeFrontend=true \
              -p:VersionPrefix="$version" \
              -p:SourceRevisionId="$GITHUB_SHA" \
              -p:BuildTimestampUtc="$built_at" \
              -p:InspectWebExpectedAsyncLowering=compiler \
              -p:InspectWebAsyncLoweringReceiptDirectory="$GITHUB_WORKSPACE/artifacts/inspect-web-compiler-async-receipts" \
              -p:CustomAfterMicrosoftCommonTargets="$GITHUB_WORKSPACE/eng/InspectWebAsyncLoweringReceipt.targets"
            """;
        if (GetRequiredScalar(publish, "run", "staging publish step").TrimEnd() !=
            ExpectedPublish)
        {
            throw new InvalidOperationException(
                "Staging publish command does not match the trusted contract.");
        }

        ValidateAsyncDeploymentCheck(
            RequireStep(
                buildSteps,
                6,
                "Verify compiler-async deployment",
                "jobs.build"),
            CompilerAsyncDeploymentCheck,
            "compiler-async deployment verification step");

        ValidateManagedApiPublish(
            RequireStep(
                buildSteps,
                7,
                "Publish Inspect Web managed API",
                "jobs.build"),
            "artifacts/inspect-web-publish/api",
            "staging managed API publish step");

        YamlMappingNode buildVerify =
            RequireStep(buildSteps, 8, "Verify staged site artifact", "jobs.build");
        ValidateDeploymentArtifactVerification(
            buildVerify,
            "staging build artifact verification step");

        YamlMappingNode upload =
            RequireStep(buildSteps, 9, "Upload staged site artifact", "jobs.build");
        RequireExactKeys(
            upload,
            ["name", "uses", "with"],
            "staging artifact upload step");
        RequireScalarValue(
            upload,
            "uses",
            UploadArtifactAction,
            "staging artifact upload step");
        RequireExactScalarValues(
            GetRequiredMapping(upload, "with", "staging artifact upload step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "inspect-web-site",
                ["path"] = "artifacts/inspect-web-publish",
                ["if-no-files-found"] = "error",
                ["overwrite"] = "true",
                ["retention-days"] = "30",
                ["include-hidden-files"] = "true",
            },
            "staging artifact upload step.with");

        YamlMappingNode deploy = GetRequiredMapping(jobs, "deploy", "staging jobs");
        RequireExactKeys(
            deploy,
            ["name", "needs", "if", "environment", "runs-on", "steps"],
            "jobs.deploy");
        RequireScalarValue(deploy, "needs", "build", "jobs.deploy");
        RequireScalarValue(deploy, "name", "Publish staging", "jobs.deploy");
        RequireScalarValue(
            deploy,
            "if",
            "github.ref == 'refs/heads/main'",
            "jobs.deploy");
        RequireScalarValue(deploy, "runs-on", "ubuntu-26.04", "jobs.deploy");
        YamlMappingNode environment =
            GetRequiredMapping(deploy, "environment", "jobs.deploy");
        RequireExactScalarValues(
            environment,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "inspect-web-staging",
                ["url"] = "https://dotnet-inspect.ca",
            },
            "jobs.deploy.environment");
        YamlSequenceNode deploySteps =
            GetRequiredSequence(deploy, "steps", "jobs.deploy");
        if (deploySteps.Children.Count != 3)
        {
            throw new InvalidOperationException(
                "Staging deployment must contain only artifact download, " +
                "artifact verification, and deploy steps.");
        }

        YamlMappingNode download =
            RequireStep(deploySteps, 0, "Download staged site artifact");
        RequireExactKeys(
            download,
            ["name", "uses", "with"],
            "staging artifact download step");
        RequireScalarValue(
            download,
            "uses",
            DownloadArtifactAction,
            "staging artifact download step");
        YamlMappingNode downloadWith =
            GetRequiredMapping(download, "with", "staging artifact download step");
        RequireExactScalarValues(
            downloadWith,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "inspect-web-site",
                ["path"] = "artifacts/inspect-web-publish",
                ["digest-mismatch"] = "error",
            },
            "staging artifact download step.with");

        YamlMappingNode verify =
            RequireStep(deploySteps, 1, "Verify staged site artifact");
        ValidateDeploymentArtifactVerification(
            verify,
            "staging artifact verification step");

        YamlMappingNode deployStep =
            RequireStep(deploySteps, 2, "Deploy to staging");
        RequireExactKeys(
            deployStep,
            ["name", "uses", "with"],
            "staging deploy step");
        RequireScalarValue(
            deployStep,
            "uses",
            AzureAction,
            "staging deploy step");
        RequireExactScalarValues(
            GetRequiredMapping(deployStep, "with", "staging deploy step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["azure_static_web_apps_api_token"] =
                    "${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB_STAGING }}",
                ["action"] = "upload",
                ["app_location"] = "artifacts/inspect-web-publish/wwwroot",
                ["api_location"] = "artifacts/inspect-web-publish/api",
                ["output_location"] = "",
                ["skip_app_build"] = "true",
                ["skip_api_build"] = "true",
            },
            "staging deploy step.with");
    }

    private static void ValidateCoreClrStaging(string workflow)
    {
        using TextReader reader = new StringReader(workflow);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one CoreCLR staging workflow document, found {yaml.Documents.Count}.");
        }

        YamlMappingNode root = RequireMapping(
            yaml.Documents[0].RootNode,
            "CoreCLR staging workflow root");
        RequireExactKeys(
            root,
            ["name", "on", "permissions", "env", "jobs"],
            "CoreCLR staging workflow");
        RequireScalarValue(
            root,
            "name",
            "Deploy inspect-web CoreCLR staging",
            "CoreCLR staging workflow");
        ValidateCoreClrStagingTrigger(
            GetRequiredMapping(root, "on", "CoreCLR staging workflow"));
        RequireExactScalarValues(
            GetRequiredMapping(root, "permissions", "CoreCLR staging workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["actions"] = "read",
                ["contents"] = "read",
            },
            "CoreCLR staging workflow.permissions");
        RequireExactScalarValues(
            GetRequiredMapping(root, "env", "CoreCLR staging workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true",
                ["DOTNET_DOTNETUP_DATA_DIR"] =
                    "${{ github.workspace }}/artifacts/dotnetup-data",
                ["DOTNET_NOLOGO"] = "true",
                ["DOTNET_ROOT"] =
                    "${{ github.workspace }}/artifacts/dotnet-coreclr",
            },
            "CoreCLR staging workflow.env");

        YamlMappingNode jobs =
            GetRequiredMapping(root, "jobs", "CoreCLR staging workflow");
        RequireExactKeys(jobs, ["publish"], "CoreCLR staging jobs");

        YamlMappingNode publishJob =
            GetRequiredMapping(jobs, "publish", "CoreCLR staging jobs");
        RequireExactKeys(
            publishJob,
            ["name", "concurrency", "environment", "runs-on", "steps"],
            "CoreCLR jobs.publish");
        RequireScalarValue(
            publishJob,
            "name",
            "Build and publish promoted CoreCLR comparison",
            "CoreCLR jobs.publish");
        RequireExactScalarValues(
            GetRequiredMapping(
                publishJob,
                "concurrency",
                "CoreCLR jobs.publish"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["group"] = "deploy-inspect-web-coreclr-staging",
                ["cancel-in-progress"] = "false",
            },
            "CoreCLR jobs.publish.concurrency");
        RequireExactScalarValues(
            GetRequiredMapping(
                publishJob,
                "environment",
                "CoreCLR jobs.publish"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "inspect-web-coreclr-staging",
                ["url"] = "https://coreclr.dotnet-inspect.ca",
            },
            "CoreCLR jobs.publish.environment");
        RequireScalarValue(
            publishJob,
            "runs-on",
            "ubuntu-26.04",
            "CoreCLR jobs.publish");

        YamlSequenceNode publishSteps =
            GetRequiredSequence(publishJob, "steps", "CoreCLR jobs.publish");
        if (publishSteps.Children.Count != 15)
        {
            throw new InvalidOperationException(
                "CoreCLR publish must build and deploy the promoted product identity.");
        }

        YamlMappingNode checkout =
            RequireStep(publishSteps, 0, null, "CoreCLR jobs.publish");
        RequireExactKeys(
            checkout,
            ["uses", "with"],
            "CoreCLR staging build checkout");
        RequireScalarValue(
            checkout,
            "uses",
            CheckoutAction,
            "CoreCLR staging build checkout");
        RequireExactScalarValues(
            GetRequiredMapping(
                checkout,
                "with",
                "CoreCLR staging build checkout"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ref"] = "${{ inputs.source_sha }}",
            },
            "CoreCLR staging build checkout.with");

        YamlMappingNode compilerArtifact =
            RequireStep(
                publishSteps,
                1,
                "Download compiler-async staging artifact",
                "CoreCLR jobs.build");
        RequireExactKeys(
            compilerArtifact,
            ["name", "uses", "with"],
            "CoreCLR compiler artifact download step");
        RequireScalarValue(
            compilerArtifact,
            "uses",
            DownloadArtifactAction,
            "CoreCLR compiler artifact download step");
        RequireExactScalarValues(
            GetRequiredMapping(
                compilerArtifact,
                "with",
                "CoreCLR compiler artifact download step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["artifact-ids"] = "${{ inputs.artifact_id }}",
                ["path"] = "artifacts/inspect-web-compiler-publish",
                ["github-token"] = "${{ secrets.GITHUB_TOKEN }}",
                ["repository"] = "${{ github.repository }}",
                ["run-id"] = "${{ inputs.staging_run_id }}",
                ["digest-mismatch"] = "error",
            },
            "CoreCLR compiler artifact download step.with");

        YamlMappingNode setup =
            RequireStep(
                publishSteps,
                3,
                "Install pinned .NET 12 SDK",
                "CoreCLR jobs.publish");
        RequireExactKeys(
            setup,
            ["name", "shell", "run"],
            "CoreCLR staging setup step");
        RequireScalarValue(
            setup,
            "shell",
            "bash",
            "CoreCLR staging setup step");
        const string ExpectedSdkInstall =
            """
            set -euo pipefail
            pin_env="$RUNNER_TEMP/inspect-web-runtime-cohort.env"
            node inspect-web/scripts/runtime-cohort-pin.ts export \
              --pin inspect-web/runtime-cohort-pin.json \
              --output "$pin_env"
            set -a
            source "$pin_env"
            set +a
            cat "$pin_env" >> "$GITHUB_ENV"
            dotnetup_dir="$RUNNER_TEMP/dotnetup"
            getter_script="$RUNNER_TEMP/get-dotnetup.sh"
            mkdir -p "$dotnetup_dir" "$DOTNET_ROOT" "$DOTNET_DOTNETUP_DATA_DIR"
            curl -fsSL --retry 3 \
              https://aka.ms/dotnet/dotnetup/daily/get-dotnetup.sh \
              -o "$getter_script"
            bash "$getter_script" --install-dir "$dotnetup_dir"
            "$dotnetup_dir/dotnetup" sdk install "$DOTNET_SDK_VERSION" \
              --install-path "$DOTNET_ROOT" \
              --untracked \
              --set-default-install false \
              --interactive false
            cat > global.json <<EOF
            {
              "sdk": {
                "version": "$DOTNET_SDK_VERSION",
                "rollForward": "disable",
                "allowPrerelease": true
              }
            }
            EOF
            echo "$DOTNET_ROOT" >> "$GITHUB_PATH"
            test "$("$DOTNET_ROOT/dotnet" --version)" = "$DOTNET_SDK_VERSION"
            """;
        if (GetRequiredScalar(
                setup,
                "run",
                "CoreCLR staging setup step").TrimEnd() != ExpectedSdkInstall)
        {
            throw new InvalidOperationException(
                "CoreCLR staging SDK installation does not match the trusted contract.");
        }

        YamlMappingNode setupNode =
            RequireStep(
                publishSteps,
                2,
                "Setup Node",
                "CoreCLR jobs.build");
        RequireExactKeys(
            setupNode,
            ["name", "uses", "with"],
            "CoreCLR staging Node setup step");
        RequireScalarValue(
            setupNode,
            "uses",
            SetupNodeAction,
            "CoreCLR staging Node setup step");
        RequireExactScalarValues(
            GetRequiredMapping(
                setupNode,
                "with",
                "CoreCLR staging Node setup step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["node-version"] = "24",
                ["cache"] = "npm",
                ["cache-dependency-path"] = "inspect-web/package-lock.json",
            },
            "CoreCLR staging Node setup step.with");

        YamlMappingNode install =
            RequireStep(
                publishSteps,
                4,
                "Install browser Wasm workload",
                "CoreCLR jobs.build");
        RequireExactKeys(
            install,
            ["name", "shell", "run"],
            "CoreCLR staging workload step");
        RequireScalarValue(
            install,
            "shell",
            "bash",
            "CoreCLR staging workload step");
        const string ExpectedWorkloadInstall =
            """
            set -euo pipefail
            dotnet workload install "$INSPECT_WEB_RUNTIME_WORKLOAD" \
              --skip-manifest-update \
              --source "$DOTNET_DAILY_FEED" \
              --source "$DOTNET_NUGET_FEED"

            cohort_dir=artifacts/inspect-web-coreclr-runtime-cohort
            rm -rf "$cohort_dir"
            mkdir -p "$cohort_dir"
            dotnet --info > "$cohort_dir/dotnet-info.txt"
            dotnet workload list > "$cohort_dir/workload-list.txt"

            test "$(dotnet --version)" = "$DOTNET_SDK_VERSION"
            test "$(dotnet --list-runtimes | awk '$1 == "Microsoft.NETCore.App" { print $2 }')" = "$DOTNET_RUNTIME_VERSION"
            manifest="$DOTNET_ROOT/sdk-manifests/$DOTNET_SDK_FEATURE_BAND/$INSPECT_WEB_RUNTIME_WORKLOAD_MANIFEST/$DOTNET_SDK_VERSION/WorkloadManifest.json"
            jq -e \
              --arg sdk "$DOTNET_SDK_VERSION" \
              --arg runtime "$DOTNET_RUNTIME_VERSION" \
              --arg workload "$INSPECT_WEB_RUNTIME_WORKLOAD" \
              --slurpfile pin inspect-web/runtime-cohort-pin.json \
              '
                . as $manifest
                | $pin[0] as $pin
                | .workloads[$workload].packs as $packs
                | .version == $sdk
                  and $packs == $pin.workload.packIds
                  and all($packs[]; . as $id | $manifest.packs[$id].version == $runtime)
              ' "$manifest" >/dev/null

            for pack in \
              Microsoft.NET.Runtime.WebAssembly.Sdk \
              Microsoft.NETCore.App.Runtime.Mono.browser-wasm \
              Microsoft.NETCore.App.Runtime.browser-wasm \
              Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.browser-wasm; do
              test -d "$DOTNET_ROOT/packs/$pack/$DOTNET_RUNTIME_VERSION"
            done
            webassembly_pack_nupkg="$DOTNET_ROOT/library-packs/microsoft.net.sdk.webassembly.pack.$DOTNET_RUNTIME_VERSION.nupkg"
            test -f "$webassembly_pack_nupkg"
            vmr_repository=$(unzip -p "$webassembly_pack_nupkg" '*.nuspec' \
              | sed -n 's/.*<repository[^>]*url="\([^"]*\)"[^>]*>.*/\1/p')
            vmr_commit=$(unzip -p "$webassembly_pack_nupkg" '*.nuspec' \
              | sed -n 's/.*<repository[^>]*commit="\([^"]*\)"[^>]*>.*/\1/p')
            test "$vmr_repository" = "$DOTNET_VMR_REPOSITORY"
            test "$vmr_commit" = "$DOTNET_VMR_COMMIT"

            target_framework=$(dotnet msbuild \
              src/DotnetInspect.Web/DotnetInspect.Web.csproj \
              -getProperty:TargetFramework \
              -nologo)
            test "$target_framework" = "$INSPECT_WEB_RUNTIME_TARGET_FRAMEWORK"
            runtime_pack="$DOTNET_ROOT/packs/Microsoft.NETCore.App.Runtime.browser-wasm/$DOTNET_RUNTIME_VERSION/runtimes/browser-wasm/native"
            native_wasm_sha256=$(sha256sum "$runtime_pack/dotnet.native.wasm" | awk '{print $1}')
            native_javascript_sha256=$(sha256sum "$runtime_pack/dotnet.native.js" | awk '{print $1}')

            jq \
              --arg sdk "$DOTNET_SDK_VERSION" \
              --arg runtime "$DOTNET_RUNTIME_VERSION" \
              --arg feature_band "$DOTNET_SDK_FEATURE_BAND" \
              --arg daily_feed "$DOTNET_DAILY_FEED" \
              --arg nuget_feed "$DOTNET_NUGET_FEED" \
              --arg target_framework "$target_framework" \
              --arg vmr_commit "$vmr_commit" \
              --arg vmr_repository "$vmr_repository" \
              --arg native_wasm_sha256 "$native_wasm_sha256" \
              --arg native_javascript_sha256 "$native_javascript_sha256" \
              --arg workload "$INSPECT_WEB_RUNTIME_WORKLOAD" \
              '
                . as $manifest
                | .workloads[$workload].packs as $packs
                | {
                    schema: 2,
                    sdk: {
                      version: $sdk,
                      featureBand: $feature_band
                    },
                    runtime: {
                      name: "Microsoft.NETCore.App",
                      version: $runtime,
                      implementation: "CoreCLR",
                      source: {
                        repository: $vmr_repository,
                        commit: $vmr_commit
                      },
                      assets: {
                        nativeWasmSha256: $native_wasm_sha256,
                        nativeJavaScriptSha256: $native_javascript_sha256
                      }
                    },
                    targetFramework: $target_framework,
                    configuration: {
                      asyncLowering: "runtime",
                      publishReadyToRun: false,
                      wasmBuildNative: false
                    },
                    workload: {
                      id: $workload,
                      manifestVersion: .version,
                      sources: [$daily_feed, $nuget_feed],
                      packs: [
                        $packs[] as $id
                        | {
                            id: $id,
                            version: $manifest.packs[$id].version
                          }
                      ]
                    }
                  }
              ' "$manifest" > "$cohort_dir/runtime-cohort.json"
            """;
        if (GetRequiredScalar(
                install,
                "run",
                "CoreCLR staging workload step").TrimEnd() != ExpectedWorkloadInstall)
        {
            throw new InvalidOperationException(
                "CoreCLR staging workload installation does not match the trusted contract.");
        }

        YamlMappingNode frontend =
            RequireStep(
                publishSteps,
                5,
                "Build browser frontend",
                "CoreCLR jobs.build");
        RequireExactScalarValues(
            frontend,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "Build browser frontend",
                ["working-directory"] = "inspect-web",
                ["run"] =
                    "npm ci\n" +
                    "npm run build\n" +
                    "npx playwright install --with-deps firefox\n" +
                    "grep -q '<script type=\"importmap\"></script>' dist/index.html\n" +
                    "grep -Eq '<link rel=\"preload\" id=\"webassembly\"[[:space:]]*/?>' dist/index.html\n",
            },
            "CoreCLR staging frontend build step");

        YamlMappingNode publish =
            RequireStep(
                publishSteps,
                6,
                "Publish CoreCLR browser app",
                "CoreCLR jobs.build");
        RequireExactKeys(
            publish,
            ["name", "shell", "run"],
            "CoreCLR staging publish step");
        RequireScalarValue(
            publish,
            "shell",
            "bash",
            "CoreCLR staging publish step");
        const string ExpectedPublish =
            """
            rm -rf artifacts/inspect-web-coreclr-publish artifacts/inspect-web-runtime-async-receipts
            nuget_config="$RUNNER_TEMP/inspect-web-coreclr-NuGet.Config"
            cat > "$nuget_config" <<EOF
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="dotnet-workload" value="$DOTNET_ROOT/library-packs" />
                <add key="dotnet12" value="$DOTNET_DAILY_FEED" />
                <add key="nuget.org" value="$DOTNET_NUGET_FEED" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="dotnet-workload">
                  <package pattern="Microsoft.NET.Sdk.WebAssembly.Pack" />
                </packageSource>
                <packageSource key="dotnet12">
                  <package pattern="Microsoft.AspNetCore.App.*" />
                  <package pattern="Microsoft.DotNet.ILCompiler" />
                  <package pattern="Microsoft.NET.ILLink.Tasks" />
                  <package pattern="Microsoft.NETCore.App.*" />
                  <package pattern="runtime.*.Microsoft.DotNet.ILCompiler" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            EOF
            version=$(dotnet msbuild src/DotnetInspect.Cli/DotnetInspect.Cli.csproj -getProperty:VersionPrefix -nologo)
            built_at=$(date -u +'%Y-%m-%dT%H:%M:%SZ')
            dotnet publish \
              src/DotnetInspect.Web/DotnetInspect.Web.csproj \
              -c Release \
              --output artifacts/inspect-web-coreclr-publish \
              --configfile "$nuget_config" \
              -p:InspectWebIncludeFrontend=true \
              -p:VersionPrefix="$version" \
              -p:SourceRevisionId="${{ inputs.source_sha }}" \
              -p:BuildTimestampUtc="$built_at" \
              -p:Features=runtime-async=on \
              -p:UseMonoRuntime=false \
              -p:PublishReadyToRun=false \
              -p:WasmBuildNative=false \
              -p:WasmNestedPublishAppDependsOn= \
              -p:WasmEnableExceptionHandling=true \
              -p:InspectWebExpectedAsyncLowering=runtime \
              -p:InspectWebAsyncLoweringReceiptDirectory="$GITHUB_WORKSPACE/artifacts/inspect-web-runtime-async-receipts" \
              -p:CustomAfterMicrosoftCommonTargets="$GITHUB_WORKSPACE/eng/InspectWebAsyncLoweringReceipt.targets"
            mkdir -p artifacts/inspect-web-coreclr-publish/runtime-cohort
            cp artifacts/inspect-web-coreclr-runtime-cohort/dotnet-info.txt \
              artifacts/inspect-web-coreclr-runtime-cohort/workload-list.txt \
              artifacts/inspect-web-coreclr-runtime-cohort/runtime-cohort.json \
              artifacts/inspect-web-coreclr-publish/runtime-cohort/
            """;
        if (GetRequiredScalar(
                publish,
                "run",
                "CoreCLR staging publish step").TrimEnd() != ExpectedPublish)
        {
            throw new InvalidOperationException(
                "CoreCLR staging publish command does not match the trusted contract.");
        }

        ValidateAsyncDeploymentCheck(
            RequireStep(
                publishSteps,
                7,
                "Verify runtime-async deployment",
                "CoreCLR jobs.build"),
            RuntimeAsyncDeploymentCheck,
            "runtime-async deployment verification step");

        YamlMappingNode packageOperation =
            RequireStep(
                publishSteps,
                8,
                "Test CoreCLR package operation",
                "CoreCLR jobs.build");
        RequireExactKeys(
            packageOperation,
            ["name", "shell", "run"],
            "CoreCLR package operation step");
        RequireScalarValue(
            packageOperation,
            "shell",
            "bash",
            "CoreCLR package operation step");
        if (GetRequiredScalar(
                packageOperation,
                "run",
                "CoreCLR package operation step").TrimEnd()
            != CoreClrPackageOperationCheck)
        {
            throw new InvalidOperationException(
                "CoreCLR package operation does not match the trusted contract.");
        }

        ValidateManagedApiPublish(
            RequireStep(
                publishSteps,
                9,
                "Publish Inspect Web managed API",
                "CoreCLR jobs.build"),
            "artifacts/inspect-web-coreclr-publish/api",
            "CoreCLR managed API publish step");

        YamlMappingNode buildVerify =
            RequireStep(
                publishSteps,
                10,
                "Verify CoreCLR site artifact",
                "CoreCLR jobs.build");
        ValidateCoreClrArtifactVerification(
            buildVerify,
            "CoreCLR build artifact verification step");

        ValidateAsyncDeploymentCheck(
            RequireStep(
                publishSteps,
                11,
                "Compare compiler-async and runtime-async receipts",
                "CoreCLR jobs.build"),
            PairedAsyncDeploymentCheck,
            "paired async deployment comparison step");

        YamlMappingNode upload =
            RequireStep(
                publishSteps,
                12,
                "Upload CoreCLR staged site artifact",
                "CoreCLR jobs.build");
        RequireExactKeys(
            upload,
            ["name", "uses", "with"],
            "CoreCLR staging artifact upload step");
        RequireScalarValue(
            upload,
            "uses",
            UploadArtifactAction,
            "CoreCLR staging artifact upload step");
        RequireExactScalarValues(
            GetRequiredMapping(
                upload,
                "with",
                "CoreCLR staging artifact upload step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "inspect-web-coreclr-site",
                ["path"] = "artifacts/inspect-web-coreclr-publish",
                ["if-no-files-found"] = "error",
                ["retention-days"] = "30",
                ["include-hidden-files"] = "true",
            },
            "CoreCLR staging artifact upload step.with");

        YamlMappingNode deployVerify =
            RequireStep(
                publishSteps,
                13,
                "Verify CoreCLR staged site artifact");
        ValidateCoreClrArtifactVerification(
            deployVerify,
            "CoreCLR staging artifact verification step");

        YamlMappingNode deployStep =
            RequireStep(publishSteps, 14, "Deploy to CoreCLR staging");
        RequireExactKeys(
            deployStep,
            ["name", "uses", "with"],
            "CoreCLR staging deploy step");
        RequireScalarValue(
            deployStep,
            "uses",
            AzureAction,
            "CoreCLR staging deploy step");
        RequireExactScalarValues(
            GetRequiredMapping(
                deployStep,
                "with",
                "CoreCLR staging deploy step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["azure_static_web_apps_api_token"] =
                    "${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB_CORECLR }}",
                ["action"] = "upload",
                ["app_location"] =
                    "artifacts/inspect-web-coreclr-publish/wwwroot",
                ["api_location"] =
                    "artifacts/inspect-web-coreclr-publish/api",
                ["output_location"] = "",
                ["skip_app_build"] = "true",
                ["skip_api_build"] = "true",
            },
            "CoreCLR staging deploy step.with");
    }

    private static void ValidateManagedApiPublish(
        YamlMappingNode step,
        string output,
        string context)
    {
        RequireExactKeys(step, ["name", "run"], context);
        string command =
            GetRequiredScalar(step, "run", context).Trim();
        string expected =
            "dotnet publish src/MsdlProxy/MsdlProxy.csproj "
            + $"-c Release --output {output}";
        if (command != expected)
        {
            throw new InvalidOperationException(
                $"{context} does not match the trusted contract.");
        }
    }

    private static void ValidateCoreClrArtifactVerification(
        YamlMappingNode step,
        string context)
    {
        RequireExactKeys(step, ["name", "shell", "run"], context);
        RequireScalarValue(step, "shell", "bash", context);
        ValidateArtifactVerificationScript(
            GetRequiredScalar(step, "run", context),
            "runtime",
            coreClr: true,
            context);
    }

    private static void ValidateAsyncDeploymentCheck(
        YamlMappingNode step,
        string expected,
        string context)
    {
        RequireExactKeys(step, ["name", "shell", "run"], context);
        RequireScalarValue(step, "shell", "bash", context);
        if (GetRequiredScalar(step, "run", context).TrimEnd() != expected)
        {
            throw new InvalidOperationException(
                $"{context} does not match the trusted contract.");
        }
    }

    private static void ValidateAsyncDeploymentVerifier(string script)
    {
        string[] required =
        [
            "if [[ \"${1:-}\" == \"--compare\" ]]",
            "commonTopLevel(compiler)",
            "compiler.assemblies.map(commonAssembly)",
            "\"$repo_root/tools/InspectWeb.AsyncLoweringVerifier/verify-async-lowering.cs\"",
            "-getProperty:VersionPrefix",
            "\"$repo_root/eng/generate-inspect-web-engine-facade.sh\" \\\n  --contract",
            "\"$declarations\" \\\n  \"$version_prefix\"",
            "--context DotnetInspect.Web.InspectWebJsExportContext",
            "/^DotnetInspect\\.Web\\.Interop\\.([A-Z][A-Za-z0-9]*)$/",
            "compiled InspectWebJsExportContext does not declare the exact facade set",
            "-property:InspectWebIncludeFrontend=true",
            "\"$tsc\" -p \"$compiled_sources/tsconfig.json\"",
            "differs from the freshly compiled context source",
            "\"$repo_root/inspect-web/scripts/verify-published-engine-facades.ts\"",
            "\"$site\" \\\n  deployment \\\n  \"$domain\"",
            "\"$repo_root/inspect-web/scripts/verify-async-project-graph.ts\"",
            "-p:InspectWebIncludeFrontend=true",
            "async_method_count: census.async_method_count",
            "assembly_count: assemblies.length",
            "js_export_method_count: census.js_export_method_count",
            "repository_projects: graph.repository_projects",
            "repository_project_count: graph.repository_project_count",
            "repository_project_sha256: graph.repository_project_sha256",
            "generated_source_sha256: sha256(sourcePath)",
            "declaration_sha256: sha256(declarationPath)",
            "published_js_sha256: sha256(javascriptPath)",
            "published_webcil_sha256: sha256(",
            "schema: 5",
        ];
        string[] missing = required
            .Where(value =>
                script.Split(value, StringSplitOptions.None).Length != 2)
            .ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                "Inspect-web async deployment verifier does not contain each "
                + "trusted evidence step exactly once. Missing or duplicate: ["
                + string.Join(", ", missing)
                + "].");
        }
    }

    private static void InspectWebAsyncDeployment_ReceiptsCoverExactFacadeSet(
        string script)
    {
        string[] required =
        [
            "InspectWebJsExportContext",
            "census.assemblies.map",
            "assert.equal(census.assembly_count, 8)",
            "assert.ok(\n  census.js_export_method_count > 0,",
            "generated_source_file:",
            "generated_source_sha256:",
            "declaration_file:",
            "declaration_sha256:",
            "published_js_file:",
            "published_js_sha256:",
            "published_webcil_file:",
            "published_webcil_sha256:",
            "repository_projects:",
            "repository_project_sha256:",
            "smoke.initialized_facades",
            "schema: 5",
        ];
        string[] assemblies =
        [
            "DotnetInspect.Web",
            "DotnetInspect.Web.Interop.Analysis",
            "DotnetInspect.Web.Interop.CallGraph",
            "DotnetInspect.Web.Interop.Catalog",
            "DotnetInspect.Web.Interop.Library",
            "DotnetInspect.Web.Interop.Metadata",
            "DotnetInspect.Web.Interop.Package",
            "DotnetInspect.Web.Interop.Source",
        ];
        string[] missing = required
            .Concat(assemblies.Select(assembly => $"  \"{assembly}\","))
            .Where(value => !script.Contains(value, StringComparison.Ordinal))
            .ToArray();
        if (missing.Length != 0
            || script.Contains("\"DotnetInspect.Web.Core\"", StringComparison.Ordinal)
            || script.Contains("readdirSync(assemblyDirectory)", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "InspectWebAsyncDeployment_ReceiptsCoverExactFacadeSet failed. "
                + "Missing: ["
                + string.Join(", ", missing)
                + "].");
        }
    }

    private static void
        InspectWebAsyncDeployment_LoweringsPreserveFacadeContracts(string script)
    {
        string[] required =
        [
            "assert.deepEqual(\n  commonTopLevel(compiler),\n  commonTopLevel(runtime)",
            "assert.deepEqual(\n  compiler.assemblies.map(commonAssembly),\n  runtime.assemblies.map(commonAssembly)",
            "generated_source_sha256: assembly.generated_source_sha256",
            "declaration_sha256: assembly.declaration_sha256",
            "published_js_sha256: assembly.published_js_sha256",
            "repository_projects: receipt.repository_projects",
            "repository_project_sha256: receipt.repository_project_sha256",
            "receipt.compiler_async_method_count",
            "receipt.runtime_async_method_count",
            "assembly.compiler_async_method_count",
            "assembly.runtime_async_method_count",
        ];
        string[] missing = required
            .Where(value => !script.Contains(value, StringComparison.Ordinal))
            .ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                "InspectWebAsyncDeployment_LoweringsPreserveFacadeContracts "
                + "failed. Missing: ["
                + string.Join(", ", missing)
                + "].");
        }
    }

    private static void ValidateAsyncLoweringReceiptTarget(string target)
    {
        string[] required =
        [
            "BeforeTargets=\"CoreCompile\"",
            "Condition=\"'$(InspectWebExpectedAsyncLowering)' != 'compiler' And '$(InspectWebExpectedAsyncLowering)' != 'runtime'\"",
            "Condition=\"'$(InspectWebExpectedAsyncLowering)' == 'runtime' And $([System.String]::Copy(';$(Features);').Contains(';runtime-async=on;')) != 'True'\"",
            "Condition=\"'$(InspectWebExpectedAsyncLowering)' == 'compiler' And $([System.String]::Copy(';$(Features);').Contains(';runtime-async=on;')) == 'True'\"",
            "File=\"$(InspectWebAsyncLoweringReceiptDirectory)/$(MSBuildProjectName).txt\"",
            "Lines=\"$(MSBuildProjectFullPath)\"",
        ];
        string[] missing = required
            .Where(value =>
                target.Split(value, StringSplitOptions.None).Length != 2)
            .ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                "Inspect-web async-lowering receipt target does not contain each "
                + "trusted compile receipt step exactly once. Missing or duplicate: ["
                + string.Join(", ", missing)
                + "].");
        }
    }

    private static void ValidateDeploymentArtifactVerification(
        YamlMappingNode step,
        string context)
    {
        RequireExactKeys(step, ["name", "shell", "run"], context);
        RequireScalarValue(step, "shell", "bash", context);
        ValidateArtifactVerificationScript(
            GetRequiredScalar(step, "run", context),
            "compiler",
            coreClr: false,
            context);
    }

    private static void ValidateArtifactVerificationScript(
        string script,
        string lowering,
        bool coreClr,
        string context)
    {
        string expected = BuildArtifactVerificationScript(lowering, coreClr);
        if (script.TrimEnd() != expected)
        {
            throw new InvalidOperationException(
                $"{context} does not match the trusted artifact verification contract.");
        }
    }

    private static string BuildArtifactVerificationScript(
        string lowering,
        bool coreClr)
    {
        string publishRoot = coreClr
            ? "artifacts/inspect-web-coreclr-publish"
            : "artifacts/inspect-web-publish";
        string compilerCount = lowering == "compiler"
            ? ".compiler_async_method_count == .async_method_count"
            : ".compiler_async_method_count == 0";
        string runtimeCount = lowering == "runtime"
            ? ".runtime_async_method_count == .async_method_count"
            : ".runtime_async_method_count == 0";
        string coreClrChecks = coreClr
            ? $$"""
              test "$(find "$site/_framework" -maxdepth 1 -type f -name 'dotnet.native.*.js' | wc -l)" -eq 1
              test "$(find "$site/_framework" -maxdepth 1 -type f -name 'dotnet.native.*.wasm' | wc -l)" -eq 1
              grep -q GetDotNetRuntimeHeap "$site"/_framework/dotnet.native.*.js
              cohort={{publishRoot}}/runtime-cohort
              test -f "$cohort/dotnet-info.txt"
              test -f "$cohort/workload-list.txt"
              jq -e \
                --arg sdk "$DOTNET_SDK_VERSION" \
                --arg feature_band "$DOTNET_SDK_FEATURE_BAND" \
                --arg runtime "$DOTNET_RUNTIME_VERSION" \
                --arg repository "$DOTNET_VMR_REPOSITORY" \
                --arg commit "$DOTNET_VMR_COMMIT" \
                --arg target_framework "$INSPECT_WEB_RUNTIME_TARGET_FRAMEWORK" \
                --arg workload "$INSPECT_WEB_RUNTIME_WORKLOAD" \
                --arg daily_feed "$DOTNET_DAILY_FEED" \
                --arg nuget_feed "$DOTNET_NUGET_FEED" \
                --slurpfile pin inspect-web/runtime-cohort-pin.json \
                '
                .schema == 2
                and .sdk == {
                  version: $sdk,
                  featureBand: $feature_band
                }
                and .runtime.name == "Microsoft.NETCore.App"
                and .runtime.version == $runtime
                and .runtime.implementation == "CoreCLR"
                and .runtime.source == {
                  repository: $repository,
                  commit: $commit
                }
                and (.runtime.assets.nativeWasmSha256 | test("^[0-9a-f]{64}$"))
                and (.runtime.assets.nativeJavaScriptSha256 | test("^[0-9a-f]{64}$"))
                and .targetFramework == $target_framework
                and .configuration == {
                  asyncLowering: "runtime",
                  publishReadyToRun: false,
                  wasmBuildNative: false
                }
                and .workload.id == $workload
                and .workload.manifestVersion == $sdk
                and .workload.sources == [$daily_feed, $nuget_feed]
                and .workload.packs == [
                  $pin[0].workload.packIds[] | {
                    id: .,
                    version: $runtime
                  }
                ]
              ' "$cohort/runtime-cohort.json" >/dev/null
              test "$(sha256sum "$site"/_framework/dotnet.native.*.wasm | awk '{print $1}')" = \
                "$(jq -r '.runtime.assets.nativeWasmSha256' "$cohort/runtime-cohort.json")"
              test "$(sha256sum "$site"/_framework/dotnet.native.*.js | awk '{print $1}')" = \
                "$(jq -r '.runtime.assets.nativeJavaScriptSha256' "$cohort/runtime-cohort.json")"
              """
            : "";
        const string Template =
            """
            set -euo pipefail
            site=__PUBLISH_ROOT__/wwwroot
            api=__PUBLISH_ROOT__/api
            index="$site/index.html"
            receipt=__PUBLISH_ROOT__/async-lowering.json
            test -f "$index"
            jq -e '
              .schema == 5
              and .method == "InspectionEngine.AsyncLoweringCanary"
              and .lowering == "__LOWERING__"
              and .result == "inspect-web-async-lowering-ok"
              and .facade_count == 8
              and .assembly_count == 8
              and .js_export_method_count > 0
              and .async_method_count > 0
              and __COMPILER_COUNT__
              and __RUNTIME_COUNT__
              and .repository_project_count > 0
              and (.repository_projects | length) == .repository_project_count
              and .repository_projects == (.repository_projects | sort | unique)
              and (.repository_project_sha256 | test("^[0-9a-f]{64}$"))
              and ([.assemblies[].name] == [
                "DotnetInspect.Web",
                "DotnetInspect.Web.Interop.Analysis",
                "DotnetInspect.Web.Interop.CallGraph",
                "DotnetInspect.Web.Interop.Catalog",
                "DotnetInspect.Web.Interop.Library",
                "DotnetInspect.Web.Interop.Metadata",
                "DotnetInspect.Web.Interop.Package",
                "DotnetInspect.Web.Interop.Source"
              ])
              and ([.assemblies[].module] == [
                "inspect-web-host",
                "inspect-web-analysis",
                "inspect-web-call-graph",
                "inspect-web-catalog",
                "inspect-web-library",
                "inspect-web-metadata",
                "inspect-web-package",
                "inspect-web-source"
              ])
              and all(.assemblies[];
                .file == (.name + ".dll")
                and (.publish_assembly_sha256 | test("^[0-9a-f]{64}$"))
                and .generated_source_file == (.name + ".ts")
                and (.generated_source_sha256 | test("^[0-9a-f]{64}$"))
                and .declaration_file == (.module + ".d.ts")
                and (.declaration_sha256 | test("^[0-9a-f]{64}$"))
                and .published_js_file == (.module + ".js")
                and (.published_js_sha256 | test("^[0-9a-f]{64}$"))
                and .webcil_assembly == .name
                and (. as $assembly | $assembly.published_webcil_file | startswith($assembly.name + "."))
                and (.published_webcil_file | test("^DotnetInspect\\.Web(\\.[A-Za-z0-9]+)*\\.[A-Za-z0-9]+\\.wasm$"))
                and (.published_webcil_sha256 | test("^[0-9a-f]{64}$"))
                and .js_export_method_count > 0
                and .async_method_count > 0
                and __COMPILER_COUNT__
                and __RUNTIME_COUNT__)
              and ([.assemblies[].js_export_method_count] | add) == .js_export_method_count
              and ([.assemblies[].async_method_count] | add) == .async_method_count
              and ([.assemblies[].compiler_async_method_count] | add) == .compiler_async_method_count
              and ([.assemblies[].runtime_async_method_count] | add) == .runtime_async_method_count
              and .smoke.initialized_facades == [.assemblies[] | {
                assembly: .name,
                module: .module
              }]
              and .smoke.sdk_create_count == 1
              and .smoke.sdk_runtime_count == 1
              and .smoke.entry_point_count == 0
              and .smoke.async_lowering_canary == "inspect-web-async-lowering-ok"
            ' "$receipt" >/dev/null
            expected_modules=$(jq -r '.assemblies[].published_js_file' "$receipt" | sort)
            published_modules=$(find "$site" -maxdepth 1 -type f -name 'inspect-web-*.js' -printf '%f\n' | sort)
            test "$published_modules" = "$expected_modules"
            while IFS=$'\t' read -r js_file js_sha webcil_file webcil_sha; do
              test "$(sha256sum "$site/$js_file" | awk '{print $1}')" = "$js_sha"
              test "$(sha256sum "$site/_framework/$webcil_file" | awk '{print $1}')" = "$webcil_sha"
            done < <(jq -r '.assemblies[] | [.published_js_file, .published_js_sha256, .published_webcil_file, .published_webcil_sha256] | @tsv' "$receipt")
            test ! -f "$site/inspect-web-engine.js"
            test ! -f "$site/inspect-web-engine.ts"
            test -f "$site/staticwebapp.config.json"
            test -f "$api/host.json"
            test -f "$api/functions.metadata"
            test -f "$api/worker.config.json"
            test -f "$api/.azurefunctions/Microsoft.Azure.WebJobs.Extensions.FunctionMetadataLoader.dll"
            jq -e 'any(.[]; .name == "MsdlProxy" and .language == "dotnet-isolated" and any(.bindings[]; .type == "httpTrigger" and .authLevel == "Anonymous" and .methods == ["get"] and .route == "msdl/{pdbFileName}/{symbolKey}"))' "$api/functions.metadata" >/dev/null
            manifest="$site/manifest.json"
            test -f "$manifest"
            jq -e '. as $manifest | type == "object" and (.["index.html"] | type == "object") and all(to_entries[]; (.value | type == "object") and all(((.value.imports // []) + (.value.dynamicImports // []))[]; . as $key | $manifest | has($key)))' "$manifest" >/dev/null
            vite_assets=$(jq -er '[to_entries[].value | .file, (.css[]?), (.assets[]?)] | unique | if length > 0 then join("\n") else error("empty Vite manifest") end' "$manifest")
            while IFS= read -r asset; do
              [[ "$asset" =~ ^assets/([A-Za-z0-9_-][A-Za-z0-9._-]*/)*[A-Za-z0-9_-][A-Za-z0-9._-]*$ ]]
              test -f "$site/$asset"
            done <<< "$vite_assets"
            vite_entry=$(jq -er '.["index.html"].file' "$manifest")
            grep -Fq "src=\"/$vite_entry\"" "$index"
            vite_stylesheets=$(jq -er '.["index.html"].css | if length > 0 then join("\n") else error("missing Vite stylesheet") end' "$manifest")
            while IFS= read -r stylesheet; do
              grep -Fq "href=\"/$stylesheet\"" "$index"
            done <<< "$vite_stylesheets"
            dotnet_module=$(sed -n 's#.*"\./_framework/dotnet\.js": "\./_framework/\([^"]*\.js\)".*#\1#p' "$index" | head -n 1)
            test -n "$dotnet_module"
            test -f "$site/_framework/$dotnet_module"
            import_map_line=$(grep -n -m1 '<script type="importmap">' "$index" | cut -d: -f1)
            module_line=$(grep -n -m1 '<script type="module"' "$index" | cut -d: -f1)
            test "$import_map_line" -lt "$module_line"
            __CORECLR_CHECKS__
            """;
        return Template
            .Replace("__PUBLISH_ROOT__", publishRoot, StringComparison.Ordinal)
            .Replace("__LOWERING__", lowering, StringComparison.Ordinal)
            .Replace("__COMPILER_COUNT__", compilerCount, StringComparison.Ordinal)
            .Replace("__RUNTIME_COUNT__", runtimeCount, StringComparison.Ordinal)
            .Replace("__CORECLR_CHECKS__", coreClrChecks, StringComparison.Ordinal)
            .TrimEnd();
    }


    private static void ValidateStagingTrigger(YamlMappingNode on)
    {
        RequireExactKeys(
            on,
            ["push", "workflow_dispatch"],
            "staging workflow.on");
        YamlMappingNode push = GetRequiredMapping(on, "push", "staging workflow.on");
        RequireExactKeys(push, ["branches"], "staging workflow.on.push");
        YamlSequenceNode branches =
            GetRequiredSequence(push, "branches", "staging workflow.on.push");
        if (branches.Children.Count != 1 ||
            RequireScalar(branches.Children[0], "staging push branch") != "main")
        {
            throw new InvalidOperationException(
                "Staging push trigger must name only main.");
        }
        if (!TryGetNode(on, "workflow_dispatch", out YamlNode dispatch) ||
            dispatch is not YamlScalarNode { Value: null or "" })
        {
            throw new InvalidOperationException(
                "Staging workflow_dispatch must not declare inputs.");
        }
    }

    private static void ValidateCoreClrStagingTrigger(YamlMappingNode on)
    {
        RequireExactKeys(on, ["workflow_call"], "CoreCLR staging workflow.on");
        YamlMappingNode workflowCall =
            GetRequiredMapping(on, "workflow_call", "CoreCLR staging workflow.on");
        RequireExactKeys(
            workflowCall,
            ["inputs"],
            "CoreCLR staging workflow.on.workflow_call");
        YamlMappingNode inputs =
            GetRequiredMapping(
                workflowCall,
                "inputs",
                "CoreCLR staging workflow.on.workflow_call");
        RequireExactKeys(
            inputs,
            ["staging_run_id", "source_sha", "artifact_id"],
            "CoreCLR staging workflow_call.inputs");
        RequireExactScalarValues(
            GetRequiredMapping(
                inputs,
                "staging_run_id",
                "CoreCLR staging workflow_call.inputs"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["description"] =
                    "Staging run whose exact site artifact was promoted",
                ["required"] = "true",
                ["type"] = "string",
            },
            "CoreCLR staging_run_id input");
        RequireExactScalarValues(
            GetRequiredMapping(
                inputs,
                "source_sha",
                "CoreCLR staging workflow_call.inputs"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["description"] = "Exact product commit promoted to production",
                ["required"] = "true",
                ["type"] = "string",
            },
            "CoreCLR source_sha input");
        RequireExactScalarValues(
            GetRequiredMapping(
                inputs,
                "artifact_id",
                "CoreCLR staging workflow_call.inputs"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["description"] =
                    "Exact staged site artifact promoted to production",
                ["required"] = "true",
                ["type"] = "string",
            },
            "CoreCLR artifact_id input");
    }


    private static void ExpectFailure(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void AssertMutationRejected(
        string workflow,
        string oldValue,
        string newValue,
        Action<string> validate,
        string message)
    {
        string mutated = workflow.Replace(
            oldValue,
            newValue,
            StringComparison.Ordinal);
        if (mutated == workflow)
            throw new InvalidOperationException($"Mutation did not apply: {message}");
        ExpectFailure(() => validate(mutated), message);
    }

    private static void AssertRejected(
        string workflow,
        Action<string> validate,
        string message) =>
        ExpectFailure(() => validate(workflow), message);

    private static YamlMappingNode RequireStep(
        YamlSequenceNode steps,
        int index,
        string? name,
        string context = "jobs.deploy")
    {
        YamlMappingNode step = RequireMapping(
            steps.Children[index],
            $"{context} step {index}");
        if (name is not null)
            RequireScalarValue(step, "name", name, $"{context} step {index}");
        return step;
    }
}
