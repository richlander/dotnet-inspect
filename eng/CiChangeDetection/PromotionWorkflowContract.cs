using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static class PromotionWorkflowContract
{
    private const string AzureAction =
        "Azure/static-web-apps-deploy@4d27395796ac319302594769cfe812bd207490b1";
    private const string IndexCheck =
        "test -f artifacts/inspect-web-publish/wwwroot/index.html";

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
        string promotionWorkflow = File.ReadAllText(promotionPath);
        string stagingWorkflow = File.ReadAllText(stagingPath);
        ValidatePromotion(promotionWorkflow);
        ValidateStaging(stagingWorkflow);

        const string trustedCheckout =
            """
                steps:
                  - uses: actions/checkout@v6

                  - name: Setup .NET
            """;
        const string candidateCheckout =
            """
                steps:
                  - uses: actions/checkout@v6
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
                  - uses: actions/checkout@v6

                  - name: Download staged site artifact
            """;
        AssertMutationRejected(
            stagingWorkflow,
            stagingDownload,
            stagingCheckout,
            ValidateStaging,
            "Staging workflow contract accepted candidate code in the deployment job.");

        AssertMutationRejected(
            promotionWorkflow,
            "      - name: Setup .NET\n        uses: actions/setup-dotnet@v5",
            "      - name: Setup .NET\n        uses: actions/download-artifact@v8",
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
            "      - name: Revalidate staged site\n",
            "      - name: Download staged site artifact\n",
            ValidatePromotion,
            "Promotion workflow contract accepted download before revalidation.");
        AssertMutationRejected(
            stagingWorkflow,
            $"        run: {IndexCheck}\n",
            $"        run: {IndexCheck} || true\n",
            ValidateStaging,
            "Staging workflow contract accepted disabled artifact verification.");
        AssertMutationRejected(
            stagingWorkflow,
            "          skip_app_build: true\n",
            "",
            ValidateStaging,
            "Staging workflow contract accepted Azure app build.");

        const string productionJob =
            """
                environment:
                  name: inspect-web-production
                  url: https://dotnet-inspect.net
                runs-on: ubuntu-26.04
                steps:
            """;
        const string productionBashEnv =
            """
                environment:
                  name: inspect-web-production
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
                environment: inspect-web-production
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
            "    steps:\n      - uses: actions/checkout@v6\n",
            """
                steps:
                  - uses: actions/checkout@v6
                    with:
                      ref: ${{ github.event.pull_request.head.sha }}
            """,
            ValidateStaging,
            "Staging workflow contract accepted PR-head checkout.");
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
                ["name"] = "inspect-web-production",
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
            "actions/checkout@v6",
            "jobs.deploy checkout");

        YamlMappingNode setup = RequireStep(steps, 1, "Setup .NET");
        RequireExactKeys(setup, ["name", "uses", "with"], "production setup step");
        RequireScalarValue(
            setup,
            "uses",
            "actions/setup-dotnet@v5",
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
            "actions/download-artifact@v8",
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
                ["path"] = "artifacts/inspect-web-publish/wwwroot",
                ["digest-mismatch"] = "error",
            },
            "artifact download step.with");

        YamlMappingNode verify =
            RequireStep(steps, 4, "Verify staged site artifact");
        RequireExactKeys(
            verify,
            ["name", "shell", "run"],
            "artifact verification step");
        RequireScalarValue(
            verify,
            "shell",
            "bash",
            "artifact verification step");
        RequireScalarValue(
            verify,
            "run",
            IndexCheck,
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
                    "${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN_INSPECT_WEB }}",
                ["action"] = "upload",
                ["app_location"] = "artifacts/inspect-web-publish/wwwroot",
                ["output_location"] = "",
                ["skip_app_build"] = "true",
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
                ["DOTNET_SDK_VERSION"] = "11.0.100-preview.6.26359.118",
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
        if (buildSteps.Children.Count != 5)
        {
            throw new InvalidOperationException(
                "Staging build must contain checkout, setup, workload install, " +
                "publish, and artifact upload steps.");
        }
        YamlMappingNode checkout =
            RequireStep(buildSteps, 0, null, "jobs.build");
        RequireExactKeys(checkout, ["uses"], "staging build checkout");
        RequireScalarValue(
            checkout,
            "uses",
            "actions/checkout@v6",
            "staging build checkout");

        YamlMappingNode setup =
            RequireStep(buildSteps, 1, "Setup .NET", "jobs.build");
        RequireExactKeys(setup, ["name", "uses", "with"], "staging setup step");
        RequireScalarValue(
            setup,
            "uses",
            "actions/setup-dotnet@v5",
            "staging setup step");
        RequireExactScalarValues(
            GetRequiredMapping(setup, "with", "staging setup step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dotnet-version"] = "${{ env.DOTNET_SDK_VERSION }}",
            },
            "staging setup step.with");

        YamlMappingNode install =
            RequireStep(buildSteps, 2, "Install browser Wasm workload", "jobs.build");
        RequireExactScalarValues(
            install,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "Install browser Wasm workload",
                ["run"] = "dotnet workload install wasm-experimental",
            },
            "staging workload step");

        YamlMappingNode publish =
            RequireStep(buildSteps, 3, "Publish browser app", "jobs.build");
        RequireExactKeys(publish, ["name", "shell", "run"], "staging publish step");
        RequireScalarValue(publish, "shell", "bash", "staging publish step");
        const string ExpectedPublish =
            """
            version=$(dotnet msbuild src/dotnet-inspect/dotnet-inspect.csproj -getProperty:VersionPrefix -nologo)
            built_at=$(date -u +'%Y-%m-%dT%H:%M:%SZ')
            dotnet publish \
              prototypes/inspect-web/engine/InspectWeb.Engine.csproj \
              -c Release \
              --output artifacts/inspect-web-publish \
              -p:VersionPrefix="$version" \
              -p:SourceRevisionId="$GITHUB_SHA" \
              -p:BuildTimestampUtc="$built_at"
            """;
        if (GetRequiredScalar(publish, "run", "staging publish step").TrimEnd() !=
            ExpectedPublish)
        {
            throw new InvalidOperationException(
                "Staging publish command does not match the trusted contract.");
        }

        YamlMappingNode upload =
            RequireStep(buildSteps, 4, "Upload staged site artifact", "jobs.build");
        RequireExactKeys(
            upload,
            ["name", "uses", "with"],
            "staging artifact upload step");
        RequireScalarValue(
            upload,
            "uses",
            "actions/upload-artifact@v7",
            "staging artifact upload step");
        RequireExactScalarValues(
            GetRequiredMapping(upload, "with", "staging artifact upload step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "inspect-web-site",
                ["path"] = "artifacts/inspect-web-publish/wwwroot",
                ["if-no-files-found"] = "error",
                ["retention-days"] = "30",
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
            "actions/download-artifact@v8",
            "staging artifact download step");
        YamlMappingNode downloadWith =
            GetRequiredMapping(download, "with", "staging artifact download step");
        RequireExactScalarValues(
            downloadWith,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "inspect-web-site",
                ["path"] = "artifacts/inspect-web-publish/wwwroot",
                ["digest-mismatch"] = "error",
            },
            "staging artifact download step.with");

        YamlMappingNode verify =
            RequireStep(deploySteps, 1, "Verify staged site artifact");
        RequireExactKeys(
            verify,
            ["name", "shell", "run"],
            "staging artifact verification step");
        RequireScalarValue(
            verify,
            "shell",
            "bash",
            "staging artifact verification step");
        RequireScalarValue(
            verify,
            "run",
            IndexCheck,
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
                ["output_location"] = "",
                ["skip_app_build"] = "true",
            },
            "staging deploy step.with");
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
            ["staging_run_id", "confirm"],
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
            "actions/checkout@v6",
            "resolution checkout");

        YamlMappingNode setup =
            RequireStep(steps, 2, "Setup .NET", "jobs.resolve");
        RequireExactKeys(setup, ["name", "uses", "with"], "resolution setup step");
        RequireScalarValue(
            setup,
            "uses",
            "actions/setup-dotnet@v5",
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
            },
            "staging evidence step.env");
        const string ExpectedValidation =
            """
            bash eng/validate-inspect-web-promotion.sh \
              "$STAGING_RUN_ID" \
              720 \
              "$GITHUB_OUTPUT"
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
