using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static class PromotionWorkflowContract
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
    private const string CoreClrDotnetDailyFeed =
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet12/nuget/v3/index.json";
    private const string CoreClrDotnetNugetFeed =
        "https://api.nuget.org/v3/index.json";
    private const string CoreClrDotnetRuntimeVersion =
        "12.0.0-alpha.1.26454.116";
    private const string CoreClrDotnetSdkFeatureBand =
        "12.0.100-alpha.1";
    private const string CoreClrDotnetSdkVersion =
        "12.0.100-alpha.1.26454.116";
    private const string CompilerAsyncDeploymentCheck =
        """
        eng/verify-inspect-web-async-deployment.sh \
          compiler \
          prototypes/inspect-web/engine/bin/Release/net11.0/InspectWeb.Engine.dll \
          artifacts/inspect-web-publish/wwwroot \
          artifacts/inspect-web-publish/async-lowering.json \
          artifacts/inspect-web-compiler-async-receipts
        """;
    private const string RuntimeAsyncDeploymentCheck =
        """
        eng/verify-inspect-web-async-deployment.sh \
          runtime \
          prototypes/inspect-web/engine/bin/Release/net11.0/InspectWeb.Engine.dll \
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
    private const string NewestEligibleStagingRunCheck =
        """
        set -euo pipefail
        latest_run_id=$(
          gh api --method GET \
            "repos/${GITHUB_REPOSITORY}/actions/workflows/deploy-inspect-web.yml/runs" \
            -f branch=main \
            -f event=push \
            -f status=success \
            -f per_page=100 \
            --jq '.workflow_runs | max_by(.run_number) | .id // empty'
        )
        test -n "$latest_run_id"
        test "$latest_run_id" = "$SELECTED_RUN_ID"
        """;
    private const string ResolveLatestSuccessfulStagingRun =
        """
        set -euo pipefail
        latest=$(
          gh api --method GET \
            "repos/${GITHUB_REPOSITORY}/actions/workflows/deploy-inspect-web.yml/runs" \
            -f branch=main \
            -f event=push \
            -f status=success \
            -f per_page=100 \
            --jq '.workflow_runs | max_by(.run_number) | [.id, .head_sha] | @tsv'
        )
        IFS=$'\t' read -r run_id head_sha <<< "$latest"
        test -n "$run_id"
        test -n "$head_sha"
        echo "run_id=$run_id" >> "$GITHUB_OUTPUT"
        echo "head_sha=$head_sha" >> "$GITHUB_OUTPUT"
        """;
    internal static void AssertMutations(string repository)
    {
        string promotionPath = Path.Combine(
            repository,
            ".github",
            "workflows",
            "promote-inspect-web.yml");
        string stagingPath = Path.Combine(
            repository,
            ".github",
            "workflows",
            "deploy-inspect-web.yml");
        string coreClrStagingPath = Path.Combine(
            repository,
            ".github",
            "workflows",
            "deploy-inspect-web-coreclr.yml");
        string asyncVerifierPath = Path.Combine(
            repository,
            "eng",
            "verify-inspect-web-async-deployment.sh");
        string asyncLoweringReceiptTargetPath = Path.Combine(
            repository,
            "eng",
            "InspectWebAsyncLoweringReceipt.targets");
        string promotionWorkflow = File.ReadAllText(promotionPath);
        string stagingWorkflow = File.ReadAllText(stagingPath);
        string coreClrStagingWorkflow = File.ReadAllText(coreClrStagingPath);
        string asyncVerifier = File.ReadAllText(asyncVerifierPath);
        string asyncLoweringReceiptTarget =
            File.ReadAllText(asyncLoweringReceiptTargetPath);
        ValidatePromotion(promotionWorkflow);
        ValidateStaging(stagingWorkflow);
        ValidateCoreClrStaging(coreClrStagingWorkflow);
        ValidateAsyncDeploymentVerifier(asyncVerifier);
        ValidateAsyncLoweringReceiptTarget(asyncLoweringReceiptTarget);
        InspectWebAsyncDeployment_ReceiptsCoverExactFacadeSet(asyncVerifier);
        InspectWebAsyncDeployment_LoweringsPreserveFacadeContracts(asyncVerifier);

        const string trustedCheckout =
            """
                steps:
                  - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1

                  - name: Setup .NET
            """;
        const string candidateCheckout =
            """
                steps:
                  - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
                    with:
                      ref: ${{ needs.resolve.outputs.sha }}

                  - name: Setup .NET
            """;
        AssertMutationRejected(
            promotionWorkflow,
            trustedCheckout,
            candidateCheckout,
            ValidatePromotion,
            "Promotion workflow contract accepted candidate-controlled production checkout.");

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
            coreClrStagingWorkflow,
            "          --jq '.workflow_runs | max_by(.run_number) | .id // empty'\n",
            "          --jq '.workflow_runs[0].id // empty'\n",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted arrival-ordered freshness.");

        AssertMutationRejected(
            promotionWorkflow,
            "      - name: Setup .NET\n        uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0",
            "      - name: Setup .NET\n        uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8",
            ValidatePromotion,
            "Promotion workflow contract accepted an alternate setup action.");
        AssertMutationRejected(
            promotionWorkflow,
            "            \"$EXPECTED_DIGEST\"\n",
            "            \"$EXPECTED_DIGEST\" || true\n",
            ValidatePromotion,
            "Promotion workflow contract accepted disabled revalidation.");
        AssertMutationRejected(
            promotionWorkflow,
            "          ALLOW_MANUAL_STAGING: ${{ inputs.allow_manual_staging }}\n",
            "",
            ValidatePromotion,
            "Promotion workflow contract accepted an unpinned manual-staging override.");
        AssertMutationRejected(
            promotionWorkflow,
            "      - name: Revalidate staged site\n",
            "      - name: Download staged site artifact\n",
            ValidatePromotion,
            "Promotion workflow contract accepted download before revalidation.");
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
            promotionWorkflow,
            "            and .method == \"InspectionEngine.AsyncLoweringCanary\"\n",
            "",
            ValidatePromotion,
            "Promotion workflow contract accepted a receipt without the exact canary method.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "            and .result == \"inspect-web-async-lowering-ok\"\n",
            "            and .result == \"changed\"\n",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted a changed canary result.");
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
            coreClrStagingWorkflow,
            "            -p:Features=runtime-async=on \\\n",
            "",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted classic async lowering.");
        AssertMutationRejected(
            stagingWorkflow,
            "prototypes/inspect-web/engine/bin/Release/net11.0/InspectWeb.Engine.dll",
            "prototypes/inspect-web/engine/obj/Release/net11.0/linked/InspectWeb.Engine.dll",
            ValidateStaging,
            "Staging contract accepted async evidence from the wrong assembly.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "prototypes/inspect-web/engine/bin/Release/net11.0/InspectWeb.Engine.dll",
            "prototypes/inspect-web/engine/obj/Release/net11.0/linked/InspectWeb.Engine.dll",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted async evidence from the wrong assembly.");
        AssertMutationRejected(
            asyncVerifier,
            "  \"$repo_root/prototypes/inspect-web/scripts/verify-published-engine-facades.ts\" \\\n  \"$site\" \\\n  deployment \\\n  \"$domain\" \\\n  \"$smoke_result\"\n",
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
            "  \"InspectWeb.Engine.SourceExports\",\n",
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
            coreClrStagingWorkflow,
            "            -p:UseMonoRuntime=false \\\n",
            "",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted the Mono runtime.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "  DOTNET_SDK_VERSION: '12.0.100-alpha.1.26454.116'\n",
            "  DOTNET_SDK_VERSION: '12.0.100-alpha.1.99999.999'\n",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted a different .NET 12 daily SDK.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "            --source \"$DOTNET_DAILY_FEED\" \\\n",
            "",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted workload installation without the daily feed.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "            -p:PublishReadyToRun=false \\\n",
            "",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted implicit ReadyToRun behavior.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "            -p:WasmBuildNative=false \\\n",
            "",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted native relinking.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "          grep -q GetDotNetRuntimeHeap \"$site\"/_framework/dotnet.native.*.js\n",
            "          grep -q GetDotNetRuntimeHeap \"$site\"/_framework/dotnet.native.*.js || true\n",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted disabled runtime verification.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "secrets.AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB_CORECLR",
            "secrets.AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB_STAGING",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted the Mono staging credential.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "          include-hidden-files: true\n",
            "",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted an artifact without hidden Function dependencies.");
        AssertMutationRejected(
            promotionWorkflow,
            "          test -f \"$api/.azurefunctions/Microsoft.Azure.WebJobs.Extensions.FunctionMetadataLoader.dll\"\n",
            "",
            ValidatePromotion,
            "Promotion workflow contract accepted an artifact without the Function extension loader.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "          skip_app_build: true\n",
            "",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted Azure app build.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            """
                  - name: Compare compiler-async and runtime-async receipts
                    shell: bash
                    run: |
                      eng/verify-inspect-web-async-deployment.sh \
                        --compare \
                        artifacts/inspect-web-compiler-publish/async-lowering.json \
                        artifacts/inspect-web-coreclr-publish/async-lowering.json

            """,
            "",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted removal of the paired receipt comparison.");

        const string productionJob =
            """
                environment:
                  name: inspect-web-production-promotion
                  url: https://dotnet-inspect.net
                runs-on: ubuntu-26.04
                steps:
            """;
        const string productionBashEnv =
            """
                environment:
                  name: inspect-web-production-promotion
                  url: https://dotnet-inspect.net
                runs-on: ubuntu-26.04
                env:
                  BASH_ENV: artifacts/inspect-web-publish/wwwroot/payload.sh
                steps:
            """;
        AssertMutationRejected(
            promotionWorkflow,
            productionJob,
            productionBashEnv,
            ValidatePromotion,
            "Promotion workflow contract accepted inherited BASH_ENV.");

        AssertRejected(
            promotionWorkflow +
            """

              bypass:
                name: Bypass production
                environment: inspect-web-production-promotion
                runs-on: ubuntu-26.04
                steps:
                  - run: echo bypass
            """,
            ValidatePromotion,
            "Promotion workflow contract accepted an extra environment-scoped job.");

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
            "    steps:\n      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1\n",
            """
                steps:
                  - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
                    with:
                      ref: ${{ github.event.pull_request.head.sha }}
            """,
            ValidateStaging,
            "Staging workflow contract accepted PR-head checkout.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "  workflow_run:\n",
            "  workflow_run:\n  pull_request_target:\n",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted pull_request_target.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "          ref: ${{ steps.latest.outputs.head_sha }}\n",
            "          ref: ${{ github.event.workflow_run.head_sha }}\n",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted checkout of the wakeup revision.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "          run-id: ${{ steps.latest.outputs.run_id }}\n",
            "          run-id: ${{ github.event.workflow_run.id }}\n",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted the wakeup artifact.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "      && github.event.workflow_run.head_branch == 'main'\n"
                + "      && github.event.workflow_run.event == 'push'\n",
            "",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted ineligible wakeups.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "      group: deploy-inspect-web-coreclr-staging\n",
            "      group: >-\n"
                + "        deploy-inspect-web-coreclr-staging-"
                + "${{ github.event.workflow_run.id }}\n",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted per-wakeup concurrency.");
        AssertMutationRejected(
            coreClrStagingWorkflow,
            "          echo \"run_id=$run_id\" >> \"$GITHUB_OUTPUT\"\n",
            "          echo \"run_id=${{ github.event.workflow_run.id }}\" "
                + ">> \"$GITHUB_OUTPUT\"\n",
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted wakeup identity as deployment authority.");

        AssertRejected(
            coreClrStagingWorkflow +
            """

              bypass:
                name: Bypass CoreCLR staging
                environment: inspect-web-coreclr-staging
                runs-on: ubuntu-26.04
                steps:
                  - run: echo bypass
            """,
            ValidateCoreClrStaging,
            "CoreCLR staging contract accepted an extra environment-scoped job.");
    }

    private static void ValidatePromotion(string workflow)
    {
        using TextReader reader = new StringReader(workflow);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one promotion workflow document, found {yaml.Documents.Count}.");
        }

        YamlMappingNode root = RequireMapping(
            yaml.Documents[0].RootNode,
            "promotion workflow root");
        RequireExactKeys(
            root,
            ["name", "on", "permissions", "concurrency", "jobs"],
            "promotion workflow");
        RequireScalarValue(root, "name", "Promote inspect-web", "promotion workflow");
        ValidatePromotionTrigger(
            GetRequiredMapping(root, "on", "promotion workflow"));
        RequireExactScalarValues(
            GetRequiredMapping(root, "permissions", "promotion workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["actions"] = "read",
                ["contents"] = "read",
            },
            "promotion workflow.permissions");
        RequireExactScalarValues(
            GetRequiredMapping(root, "concurrency", "promotion workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["group"] = "promote-inspect-web",
                ["cancel-in-progress"] = "false",
            },
            "promotion workflow.concurrency");
        YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "promotion workflow");
        RequireExactKeys(jobs, ["resolve", "deploy"], "promotion jobs");
        YamlMappingNode resolve = GetRequiredMapping(jobs, "resolve", "promotion jobs");
        RequireExactKeys(
            resolve,
            ["name", "runs-on", "outputs", "steps"],
            "jobs.resolve");
        RequireScalarValue(
            resolve,
            "name",
            "Validate staging evidence",
            "jobs.resolve");
        RequireScalarValue(resolve, "runs-on", "ubuntu-26.04", "jobs.resolve");
        RequireExactScalarValues(
            GetRequiredMapping(resolve, "outputs", "jobs.resolve"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sha"] = "${{ steps.evidence.outputs.sha }}",
                ["run_attempt"] = "${{ steps.evidence.outputs.run_attempt }}",
                ["artifact_id"] = "${{ steps.evidence.outputs.artifact_id }}",
                ["artifact_digest"] =
                    "${{ steps.evidence.outputs.artifact_digest }}",
            },
            "jobs.resolve.outputs");
        ValidateResolveSteps(
            GetRequiredSequence(resolve, "steps", "jobs.resolve"));
        YamlMappingNode deploy = GetRequiredMapping(jobs, "deploy", "promotion jobs");
        RequireExactKeys(
            deploy,
            ["name", "needs", "environment", "runs-on", "steps"],
            "jobs.deploy");
        RequireScalarValue(
            deploy,
            "name",
            "Promote to production",
            "jobs.deploy");
        RequireScalarValue(deploy, "needs", "resolve", "jobs.deploy");
        RequireScalarValue(deploy, "runs-on", "ubuntu-26.04", "jobs.deploy");

        YamlMappingNode environment =
            GetRequiredMapping(deploy, "environment", "jobs.deploy");
        RequireExactScalarValues(
            environment,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "inspect-web-production-promotion",
                ["url"] = "https://dotnet-inspect.net",
            },
            "jobs.deploy.environment");

        YamlSequenceNode steps = GetRequiredSequence(deploy, "steps", "jobs.deploy");
        if (steps.Children.Count != 6)
        {
            throw new InvalidOperationException(
                "Production deployment must contain checkout, setup, revalidation, " +
                "artifact download, artifact verification, and deploy steps.");
        }

        YamlMappingNode checkout = RequireStep(steps, 0, null);
        RequireExactKeys(checkout, ["uses"], "jobs.deploy checkout");
        RequireScalarValue(
            checkout,
            "uses",
            CheckoutAction,
            "jobs.deploy checkout");

        YamlMappingNode setup = RequireStep(steps, 1, "Setup .NET");
        RequireExactKeys(setup, ["name", "uses", "with"], "production setup step");
        RequireScalarValue(
            setup,
            "uses",
            SetupDotnetAction,
            "production setup step");
        RequireExactScalarValues(
            GetRequiredMapping(setup, "with", "production setup step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dotnet-version"] = "11.0.x",
                ["dotnet-quality"] = "preview",
            },
            "production setup step.with");

        YamlMappingNode revalidate =
            RequireStep(steps, 2, "Revalidate staged site");
        RequireExactKeys(
            revalidate,
            ["name", "shell", "env", "run"],
            "revalidation step");
        RequireScalarValue(revalidate, "shell", "bash", "revalidation step");
        RequireExactScalarValues(
            GetRequiredMapping(revalidate, "env", "revalidation step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GH_TOKEN"] = "${{ secrets.GITHUB_TOKEN }}",
                ["STAGING_RUN_ID"] = "${{ inputs.staging_run_id }}",
                ["ALLOW_MANUAL_STAGING"] = "${{ inputs.allow_manual_staging }}",
                ["EXPECTED_SHA"] = "${{ needs.resolve.outputs.sha }}",
                ["EXPECTED_ATTEMPT"] = "${{ needs.resolve.outputs.run_attempt }}",
                ["EXPECTED_ARTIFACT_ID"] =
                    "${{ needs.resolve.outputs.artifact_id }}",
                ["EXPECTED_DIGEST"] =
                    "${{ needs.resolve.outputs.artifact_digest }}",
            },
            "revalidation step.env");
        string revalidationCommand = GetRequiredScalar(
            revalidate,
            "run",
            "revalidation step");
        const string ExpectedRevalidation =
            """
            bash eng/validate-inspect-web-promotion.sh \
              "$STAGING_RUN_ID" \
              720 \
              "$RUNNER_TEMP/revalidated-inspect-web" \
              "$ALLOW_MANUAL_STAGING" \
              "$EXPECTED_SHA" \
              "$EXPECTED_ATTEMPT" \
              "$EXPECTED_ARTIFACT_ID" \
              "$EXPECTED_DIGEST"
            """;
        if (revalidationCommand.TrimEnd() != ExpectedRevalidation)
        {
            throw new InvalidOperationException(
                "Production revalidation command does not match the trusted contract.");
        }

        YamlMappingNode download =
            RequireStep(steps, 3, "Download staged site artifact");
        RequireExactKeys(
            download,
            ["name", "uses", "with"],
            "artifact download step");
        RequireScalarValue(
            download,
            "uses",
            DownloadArtifactAction,
            "artifact download step");
        YamlMappingNode downloadWith =
            GetRequiredMapping(download, "with", "artifact download step");
        RequireExactScalarValues(
            downloadWith,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["artifact-ids"] = "${{ needs.resolve.outputs.artifact_id }}",
                ["github-token"] = "${{ secrets.GITHUB_TOKEN }}",
                ["repository"] = "${{ github.repository }}",
                ["run-id"] = "${{ inputs.staging_run_id }}",
                ["path"] = "artifacts/inspect-web-publish",
                ["digest-mismatch"] = "error",
            },
            "artifact download step.with");

        YamlMappingNode verify =
            RequireStep(steps, 4, "Verify staged site artifact");
        ValidateDeploymentArtifactVerification(
            verify,
            "artifact verification step");

        YamlMappingNode deployStep =
            RequireStep(steps, 5, "Deploy to production");
        RequireExactKeys(
            deployStep,
            ["name", "uses", "with"],
            "production deploy step");
        RequireScalarValue(
            deployStep,
            "uses",
            AzureAction,
            "production deploy step");
        RequireExactScalarValues(
            GetRequiredMapping(deployStep, "with", "production deploy step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["azure_static_web_apps_api_token"] =
                    "${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB_PRODUCTION }}",
                ["action"] = "upload",
                ["app_location"] = "artifacts/inspect-web-publish/wwwroot",
                ["api_location"] = "artifacts/inspect-web-publish/api",
                ["output_location"] = "",
                ["skip_app_build"] = "true",
                ["skip_api_build"] = "true",
            },
            "production deploy step.with");
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
                ["cancel-in-progress"] = "true",
            },
            "staging workflow.concurrency");
        RequireExactScalarValues(
            GetRequiredMapping(root, "env", "staging workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true",
                ["DOTNET_NOLOGO"] = "true",
                ["DOTNET_SDK_VERSION"] = "11.0.100-preview.7.26381.103",
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
                ["cache-dependency-path"] = "prototypes/inspect-web/package-lock.json",
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
                ["working-directory"] = "prototypes/inspect-web",
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
            version=$(dotnet msbuild src/dotnet-inspect/dotnet-inspect.csproj -getProperty:VersionPrefix -nologo)
            built_at=$(date -u +'%Y-%m-%dT%H:%M:%SZ')
            dotnet publish \
              prototypes/inspect-web/engine/InspectWeb.Engine.csproj \
              -c Release \
              --output artifacts/inspect-web-publish \
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
                "Publish MSDL managed API",
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
                ["DOTNET_DAILY_FEED"] = CoreClrDotnetDailyFeed,
                ["DOTNET_DOTNETUP_DATA_DIR"] =
                    "${{ github.workspace }}/artifacts/dotnetup-data",
                ["DOTNET_NOLOGO"] = "true",
                ["DOTNET_NUGET_FEED"] = CoreClrDotnetNugetFeed,
                ["DOTNET_ROOT"] =
                    "${{ github.workspace }}/artifacts/dotnet-coreclr",
                ["DOTNET_RUNTIME_VERSION"] = CoreClrDotnetRuntimeVersion,
                ["DOTNET_SDK_FEATURE_BAND"] = CoreClrDotnetSdkFeatureBand,
                ["DOTNET_SDK_VERSION"] = CoreClrDotnetSdkVersion,
            },
            "CoreCLR staging workflow.env");

        YamlMappingNode jobs =
            GetRequiredMapping(root, "jobs", "CoreCLR staging workflow");
        RequireExactKeys(jobs, ["publish"], "CoreCLR staging jobs");

        YamlMappingNode publishJob =
            GetRequiredMapping(jobs, "publish", "CoreCLR staging jobs");
        RequireExactKeys(
            publishJob,
            ["name", "if", "concurrency", "environment", "runs-on", "steps"],
            "CoreCLR jobs.publish");
        RequireScalarValue(
            publishJob,
            "name",
            "Build and publish latest CoreCLR staging",
            "CoreCLR jobs.publish");
        RequireScalarValue(
            publishJob,
            "if",
            "github.event.workflow_run.conclusion == 'success' "
                + "&& github.event.workflow_run.head_branch == 'main' "
                + "&& github.event.workflow_run.event == 'push'",
            "CoreCLR jobs.publish");
        RequireExactScalarValues(
            GetRequiredMapping(
                publishJob,
                "concurrency",
                "CoreCLR jobs.publish"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["group"] = "deploy-inspect-web-coreclr-staging",
                ["cancel-in-progress"] = "true",
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
        if (publishSteps.Children.Count != 16)
        {
            throw new InvalidOperationException(
                "CoreCLR publish must atomically resolve latest staging, build, "
                + "verify freshness, and deploy.");
        }

        ValidateResolveLatestSuccessfulStagingRun(
            RequireStep(
                publishSteps,
                0,
                "Resolve latest successful staging run",
                "CoreCLR jobs.publish"),
            "CoreCLR latest-run resolver");

        YamlMappingNode checkout =
            RequireStep(publishSteps, 1, null, "CoreCLR jobs.publish");
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
                ["ref"] = "${{ steps.latest.outputs.head_sha }}",
            },
            "CoreCLR staging build checkout.with");

        YamlMappingNode compilerArtifact =
            RequireStep(
                publishSteps,
                2,
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
                ["name"] = "inspect-web-site",
                ["path"] = "artifacts/inspect-web-compiler-publish",
                ["github-token"] = "${{ secrets.GITHUB_TOKEN }}",
                ["repository"] = "${{ github.repository }}",
                ["run-id"] = "${{ steps.latest.outputs.run_id }}",
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
                4,
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
                ["cache-dependency-path"] = "prototypes/inspect-web/package-lock.json",
            },
            "CoreCLR staging Node setup step.with");

        YamlMappingNode install =
            RequireStep(
                publishSteps,
                5,
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
            dotnet workload install wasm-tools \
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
            manifest="$DOTNET_ROOT/sdk-manifests/$DOTNET_SDK_FEATURE_BAND/microsoft.net.workload.mono.toolchain.current/$DOTNET_SDK_VERSION/WorkloadManifest.json"
            jq -e \
              --arg sdk "$DOTNET_SDK_VERSION" \
              --arg runtime "$DOTNET_RUNTIME_VERSION" \
              '
                . as $manifest
                | .workloads["wasm-tools"].packs as $packs
                | .version == $sdk
                  and $packs == [
                    "Microsoft.NET.Runtime.WebAssembly.Sdk.net11",
                    "Microsoft.NET.Sdk.WebAssembly.Pack.net11",
                    "Microsoft.NETCore.App.Runtime.Mono.net11.browser-wasm",
                    "Microsoft.NETCore.App.Runtime.net11.browser-wasm",
                    "Microsoft.NETCore.App.Runtime.AOT.Cross.net11.browser-wasm"
                  ]
                  and all($packs[]; . as $id | $manifest.packs[$id].version == $runtime)
              ' "$manifest" >/dev/null

            for pack in \
              Microsoft.NET.Runtime.WebAssembly.Sdk \
              Microsoft.NETCore.App.Runtime.Mono.browser-wasm \
              Microsoft.NETCore.App.Runtime.browser-wasm \
              Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.browser-wasm; do
              test -d "$DOTNET_ROOT/packs/$pack/$DOTNET_RUNTIME_VERSION"
            done
            test -f "$DOTNET_ROOT/library-packs/microsoft.net.sdk.webassembly.pack.$DOTNET_RUNTIME_VERSION.nupkg"

            target_framework=$(dotnet msbuild \
              prototypes/inspect-web/engine/InspectWeb.Engine.csproj \
              -getProperty:TargetFramework \
              -nologo)
            test "$target_framework" = "net11.0"
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
              --arg native_wasm_sha256 "$native_wasm_sha256" \
              --arg native_javascript_sha256 "$native_javascript_sha256" \
              '
                . as $manifest
                | .workloads["wasm-tools"].packs as $packs
                | {
                    schema: 1,
                    sdk: {
                      version: $sdk,
                      featureBand: $feature_band
                    },
                    runtime: {
                      name: "Microsoft.NETCore.App",
                      version: $runtime,
                      implementation: "CoreCLR",
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
                      id: "wasm-tools",
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
                6,
                "Build browser frontend",
                "CoreCLR jobs.build");
        RequireExactScalarValues(
            frontend,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "Build browser frontend",
                ["working-directory"] = "prototypes/inspect-web",
                ["run"] =
                    "npm ci\n" +
                    "npm run build\n" +
                    "grep -q '<script type=\"importmap\"></script>' dist/index.html\n" +
                    "grep -Eq '<link rel=\"preload\" id=\"webassembly\"[[:space:]]*/?>' dist/index.html\n",
            },
            "CoreCLR staging frontend build step");

        YamlMappingNode publish =
            RequireStep(
                publishSteps,
                7,
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
            version=$(dotnet msbuild src/dotnet-inspect/dotnet-inspect.csproj -getProperty:VersionPrefix -nologo)
            built_at=$(date -u +'%Y-%m-%dT%H:%M:%SZ')
            dotnet publish \
              prototypes/inspect-web/engine/InspectWeb.Engine.csproj \
              -c Release \
              --output artifacts/inspect-web-coreclr-publish \
              -p:VersionPrefix="$version" \
              -p:SourceRevisionId="${{ steps.latest.outputs.head_sha }}" \
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
                8,
                "Verify runtime-async deployment",
                "CoreCLR jobs.build"),
            RuntimeAsyncDeploymentCheck,
            "runtime-async deployment verification step");

        ValidateManagedApiPublish(
            RequireStep(
                publishSteps,
                9,
                "Publish MSDL managed API",
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

        ValidateNewestEligibleStagingRun(
            RequireStep(
                publishSteps,
                14,
                "Verify newest eligible staging run",
                "CoreCLR jobs.publish"),
            "CoreCLR deploy newest-run step");

        YamlMappingNode deployStep =
            RequireStep(publishSteps, 15, "Deploy to CoreCLR staging");
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

    private static void ValidateResolveLatestSuccessfulStagingRun(
        YamlMappingNode step,
        string context)
    {
        RequireExactKeys(step, ["name", "id", "env", "run"], context);
        RequireScalarValue(step, "id", "latest", context);
        RequireExactScalarValues(
            GetRequiredMapping(step, "env", context),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GH_TOKEN"] = "${{ secrets.GITHUB_TOKEN }}",
            },
            $"{context}.env");
        if (GetRequiredScalar(step, "run", context).TrimEnd()
            != ResolveLatestSuccessfulStagingRun)
        {
            throw new InvalidOperationException(
                $"{context} does not resolve the latest successful main-push staging run.");
        }
    }

    private static void ValidateNewestEligibleStagingRun(
        YamlMappingNode step,
        string context)
    {
        RequireExactKeys(step, ["name", "env", "run"], context);
        ValidateNewestEligibleStagingRunEnvironment(step, context);
        if (GetRequiredScalar(step, "run", context).TrimEnd()
            != NewestEligibleStagingRunCheck)
        {
            throw new InvalidOperationException(
                $"{context} does not select the newest successful main-push staging run.");
        }
    }

    private static void ValidateNewestEligibleStagingRunEnvironment(
        YamlMappingNode step,
        string context)
    {
        RequireExactScalarValues(
            GetRequiredMapping(step, "env", context),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GH_TOKEN"] = "${{ secrets.GITHUB_TOKEN }}",
                ["SELECTED_RUN_ID"] = "${{ steps.latest.outputs.run_id }}",
            },
            $"{context}.env");
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
            "dotnet publish prototypes/inspect-web/msdl-proxy/MsdlProxy.csproj "
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
            "\"$repo_root/prototypes/inspect-web/scripts/verify-async-lowering.cs\"",
            "-getProperty:VersionPrefix",
            "\"$repo_root/eng/generate-inspect-web-engine-facade.sh\" \\\n  --contract",
            "\"$declarations\" \\\n  \"$version_prefix\"",
            "--context InspectWeb.Engine.InspectWebJsExportContext",
            "compiled InspectWebJsExportContext does not declare the exact facade set",
            "\"$tsc\" -p \"$compiled_sources/tsconfig.json\"",
            "differs from the freshly compiled context source",
            "\"$repo_root/prototypes/inspect-web/scripts/verify-published-engine-facades.ts\"",
            "\"$site\" \\\n  deployment \\\n  \"$domain\"",
            "\"$repo_root/prototypes/inspect-web/scripts/verify-async-project-graph.ts\"",
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
            "assert.equal(census.assembly_count, 7)",
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
            "InspectWeb.Engine",
            "InspectWeb.Engine.AnalysisExports",
            "InspectWeb.Engine.CallGraphExports",
            "InspectWeb.Engine.CatalogExports",
            "InspectWeb.Engine.MetadataExports",
            "InspectWeb.Engine.PackageExports",
            "InspectWeb.Engine.SourceExports",
        ];
        string[] missing = required
            .Concat(assemblies.Select(assembly => $"  \"{assembly}\","))
            .Where(value => !script.Contains(value, StringComparison.Ordinal))
            .ToArray();
        if (missing.Length != 0
            || script.Contains("\"InspectWeb.Engine.Core\"", StringComparison.Ordinal)
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
              jq -e '
                .schema == 1
                and .sdk == {
                  version: "{{CoreClrDotnetSdkVersion}}",
                  featureBand: "{{CoreClrDotnetSdkFeatureBand}}"
                }
                and .runtime.name == "Microsoft.NETCore.App"
                and .runtime.version == "{{CoreClrDotnetRuntimeVersion}}"
                and .runtime.implementation == "CoreCLR"
                and (.runtime.assets.nativeWasmSha256 | test("^[0-9a-f]{64}$"))
                and (.runtime.assets.nativeJavaScriptSha256 | test("^[0-9a-f]{64}$"))
                and .targetFramework == "net11.0"
                and .configuration == {
                  asyncLowering: "runtime",
                  publishReadyToRun: false,
                  wasmBuildNative: false
                }
                and .workload == {
                  id: "wasm-tools",
                  manifestVersion: "{{CoreClrDotnetSdkVersion}}",
                  sources: [
                    "{{CoreClrDotnetDailyFeed}}",
                    "{{CoreClrDotnetNugetFeed}}"
                  ],
                  packs: [
                    {
                      id: "Microsoft.NET.Runtime.WebAssembly.Sdk.net11",
                      version: "{{CoreClrDotnetRuntimeVersion}}"
                    },
                    {
                      id: "Microsoft.NET.Sdk.WebAssembly.Pack.net11",
                      version: "{{CoreClrDotnetRuntimeVersion}}"
                    },
                    {
                      id: "Microsoft.NETCore.App.Runtime.Mono.net11.browser-wasm",
                      version: "{{CoreClrDotnetRuntimeVersion}}"
                    },
                    {
                      id: "Microsoft.NETCore.App.Runtime.net11.browser-wasm",
                      version: "{{CoreClrDotnetRuntimeVersion}}"
                    },
                    {
                      id: "Microsoft.NETCore.App.Runtime.AOT.Cross.net11.browser-wasm",
                      version: "{{CoreClrDotnetRuntimeVersion}}"
                    }
                  ]
                }
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
              and .facade_count == 7
              and .assembly_count == 7
              and .js_export_method_count > 0
              and .async_method_count > 0
              and __COMPILER_COUNT__
              and __RUNTIME_COUNT__
              and .repository_project_count > 0
              and (.repository_projects | length) == .repository_project_count
              and .repository_projects == (.repository_projects | sort | unique)
              and (.repository_project_sha256 | test("^[0-9a-f]{64}$"))
              and ([.assemblies[].name] == [
                "InspectWeb.Engine",
                "InspectWeb.Engine.AnalysisExports",
                "InspectWeb.Engine.CallGraphExports",
                "InspectWeb.Engine.CatalogExports",
                "InspectWeb.Engine.MetadataExports",
                "InspectWeb.Engine.PackageExports",
                "InspectWeb.Engine.SourceExports"
              ])
              and ([.assemblies[].module] == [
                "inspect-web-host",
                "inspect-web-analysis",
                "inspect-web-call-graph",
                "inspect-web-catalog",
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
                and (.published_webcil_file | test("^InspectWeb\\.Engine(\\.[A-Za-z0-9]+)*\\.[A-Za-z0-9]+\\.wasm$"))
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

    private static void ValidatePromotionTrigger(YamlMappingNode on)
    {
        RequireExactKeys(on, ["workflow_dispatch"], "promotion workflow.on");
        YamlMappingNode dispatch =
            GetRequiredMapping(on, "workflow_dispatch", "promotion workflow.on");
        RequireExactKeys(dispatch, ["inputs"], "promotion workflow_dispatch");
        YamlMappingNode inputs =
            GetRequiredMapping(dispatch, "inputs", "promotion workflow_dispatch");
        RequireExactKeys(
            inputs,
            ["staging_run_id", "allow_manual_staging", "confirm"],
            "promotion workflow_dispatch.inputs");
        RequireExactScalarValues(
            GetRequiredMapping(
                inputs,
                "staging_run_id",
                "promotion workflow_dispatch.inputs"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["description"] =
                    "Successful main staging run whose site artifact will be promoted",
                ["required"] = "true",
            },
            "promotion staging_run_id input");
        RequireExactScalarValues(
            GetRequiredMapping(
                inputs,
                "allow_manual_staging",
                "promotion workflow_dispatch.inputs"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["description"] =
                    "Allow an operator-dispatched staging run instead of a main-push run",
                ["required"] = "true",
                ["default"] = "false",
                ["type"] = "boolean",
            },
            "promotion allow_manual_staging input");
        RequireExactScalarValues(
            GetRequiredMapping(
                inputs,
                "confirm",
                "promotion workflow_dispatch.inputs"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["description"] = "Type \"promote\" to confirm production deployment",
                ["required"] = "true",
            },
            "promotion confirm input");
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
        RequireExactKeys(on, ["workflow_run"], "CoreCLR staging workflow.on");
        YamlMappingNode workflowRun =
            GetRequiredMapping(on, "workflow_run", "CoreCLR staging workflow.on");
        RequireExactKeys(
            workflowRun,
            ["workflows", "types"],
            "CoreCLR staging workflow.on.workflow_run");
        YamlSequenceNode workflows =
            GetRequiredSequence(
                workflowRun,
                "workflows",
                "CoreCLR staging workflow.on.workflow_run");
        if (workflows.Children.Count != 1
            || RequireScalar(
                workflows.Children[0],
                "CoreCLR triggering workflow") != "Deploy inspect-web staging")
        {
            throw new InvalidOperationException(
                "CoreCLR staging must be triggered only by the Mono staging workflow.");
        }
        YamlSequenceNode types =
            GetRequiredSequence(
                workflowRun,
                "types",
                "CoreCLR staging workflow.on.workflow_run");
        if (types.Children.Count != 1
            || RequireScalar(
                types.Children[0],
                "CoreCLR workflow_run type") != "completed")
        {
            throw new InvalidOperationException(
                "CoreCLR staging must trigger only when Mono staging completes.");
        }
    }

    private static void ValidateResolveSteps(YamlSequenceNode steps)
    {
        if (steps.Children.Count != 4)
        {
            throw new InvalidOperationException(
                "Promotion resolution must contain intent, checkout, setup, " +
                "and staging validation steps.");
        }

        YamlMappingNode intent =
            RequireStep(steps, 0, "Validate dispatch intent", "jobs.resolve");
        RequireExactKeys(
            intent,
            ["name", "shell", "env", "run"],
            "dispatch intent step");
        RequireScalarValue(intent, "shell", "bash", "dispatch intent step");
        RequireExactScalarValues(
            GetRequiredMapping(intent, "env", "dispatch intent step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CONFIRM"] = "${{ inputs.confirm }}",
            },
            "dispatch intent step.env");
        const string ExpectedIntent =
            """
            set -euo pipefail
            if [ "$GITHUB_REF" != refs/heads/main ]; then
              echo "::error::Production promotion must be dispatched from main." >&2
              exit 1
            fi
            if [ "$CONFIRM" != promote ]; then
              echo "::error::Type promote to confirm production deployment." >&2
              exit 1
            fi
            """;
        if (GetRequiredScalar(intent, "run", "dispatch intent step").TrimEnd() !=
            ExpectedIntent)
        {
            throw new InvalidOperationException(
                "Dispatch intent command does not match the trusted contract.");
        }

        YamlMappingNode checkout =
            RequireStep(steps, 1, null, "jobs.resolve");
        RequireExactKeys(checkout, ["uses"], "resolution checkout");
        RequireScalarValue(
            checkout,
            "uses",
            CheckoutAction,
            "resolution checkout");

        YamlMappingNode setup =
            RequireStep(steps, 2, "Setup .NET", "jobs.resolve");
        RequireExactKeys(setup, ["name", "uses", "with"], "resolution setup step");
        RequireScalarValue(
            setup,
            "uses",
            SetupDotnetAction,
            "resolution setup step");
        RequireExactScalarValues(
            GetRequiredMapping(setup, "with", "resolution setup step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dotnet-version"] = "11.0.x",
                ["dotnet-quality"] = "preview",
            },
            "resolution setup step.with");

        YamlMappingNode validate =
            RequireStep(steps, 3, "Validate staged site", "jobs.resolve");
        RequireExactKeys(
            validate,
            ["name", "id", "shell", "env", "run"],
            "staging evidence step");
        RequireScalarValue(validate, "id", "evidence", "staging evidence step");
        RequireScalarValue(validate, "shell", "bash", "staging evidence step");
        RequireExactScalarValues(
            GetRequiredMapping(validate, "env", "staging evidence step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GH_TOKEN"] = "${{ secrets.GITHUB_TOKEN }}",
                ["STAGING_RUN_ID"] = "${{ inputs.staging_run_id }}",
                ["ALLOW_MANUAL_STAGING"] = "${{ inputs.allow_manual_staging }}",
            },
            "staging evidence step.env");
        const string ExpectedValidation =
            """
            bash eng/validate-inspect-web-promotion.sh \
              "$STAGING_RUN_ID" \
              720 \
              "$GITHUB_OUTPUT" \
              "$ALLOW_MANUAL_STAGING"
            """;
        if (GetRequiredScalar(validate, "run", "staging evidence step").TrimEnd() !=
            ExpectedValidation)
        {
            throw new InvalidOperationException(
                "Staging evidence command does not match the trusted contract.");
        }
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
