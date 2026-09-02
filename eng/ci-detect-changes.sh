#!/usr/bin/env bash
set -e -o pipefail

# This step must never fail. Every downstream job is gated on its
# outputs, so failing here does not just lose the filter -- it SKIPS
# the entire build and test matrix, landing an unvalidated commit on
# main. So when the change set cannot be determined, force every
# output true: CI over-runs instead of silently vanishing. Enforced
# by the fallback branch below, which runs no fallible command, and
# by the CiChangeDetection gate invoked by eng/test-ci-change-detection.cs.
FILES=""
RESOLVED=false
if [ "$GITHUB_EVENT_NAME" = "pull_request" ]; then
  # An empty PR diff is ambiguous: the base may now contain equivalent
  # changes, or the API may have returned no usable change set. In
  # either case, over-run rather than let every gated job disappear.
  # An empty push or merge-group diff remains resolved below because it means
  # the before and after trees do not differ.
  # The files API reports both sides of a rename. Encode the complete
  # NUL-delimited path stream because shell variables cannot contain
  # NULs and newline-delimited names cannot represent every Git path.
  if FILES=$(
    {
      EXPECTED_FILES=$(
        gh api \
          "repos/$GITHUB_REPOSITORY/pulls/$CI_PR_NUMBER" \
          --jq 'if ((.changed_files | type) == "number") and (.changed_files >= 0) and (.changed_files == (.changed_files | floor)) then (.changed_files | tostring) else error("invalid changed_files") end'
      ) || exit 1
      NON_DIGITS=${EXPECTED_FILES//[0-9]/}
      if [ -z "$EXPECTED_FILES" ] || [ -n "$NON_DIGITS" ]; then
        exit 1
      fi
      RECORDS=$(
        gh api --paginate \
          "repos/$GITHUB_REPOSITORY/pulls/$CI_PR_NUMBER/files" \
          --jq '
            def valid_file_record:
              type == "object"
              and (.filename | type) == "string"
              and (.filename | length) > 0
              and (.filename | index("\u0000")) == null
              and (
                (
                  .status == "renamed"
                  and (.previous_filename | type) == "string"
                  and (.previous_filename | length) > 0
                  and (.previous_filename | index("\u0000")) == null
                  and .previous_filename != .filename
                )
                or (
                  (
                    .status == "added"
                    or .status == "removed"
                    or .status == "modified"
                    or .status == "copied"
                    or .status == "changed"
                    or .status == "unchanged"
                  )
                  and (.previous_filename? == null)
                )
              );
            if type != "array" then
              error("pull request files response is not an array")
            elif any(.[]; valid_file_record | not) then
              error("pull request files response contains an invalid record")
            else
              .[]
              | @json
              | @base64
            end
          '
      ) || exit 1
      ACTUAL_FILES=0
      SEEN_FILES=""
      SEEN_PREVIOUS_FILES=""
      PATH_RECORDS=""
      while IFS= read -r encoded_record; do
        if [ -z "$encoded_record" ]; then
          exit 1
        fi
        record=$(
          printf '%s' "$encoded_record" | base64 --decode
        ) || exit 1
        fields=$(
          printf '%s' "$record" |
            jq -r '
              [.status, (.previous_filename // ""), .filename]
              | map(@base64)
              | join(":")
            '
        ) || exit 1
        status=${fields%%:*}
        remainder=${fields#*:}
        previous=${remainder%%:*}
        current=${remainder#*:}
        if [ -z "$status" ] || [ -z "$current" ] \
           || [ "$remainder" = "$fields" ] \
           || [ "$current" = "$remainder" ] \
           || [[ "$current" == *:* ]]; then
          exit 1
        fi
        if printf '%s' "$SEEN_FILES" | grep -Fqx "$current"; then
          exit 1
        else
          grep_status=$?
          if [ "$grep_status" -ne 1 ]; then
            exit 1
          fi
        fi
        SEEN_FILES="${SEEN_FILES}${current}"$'\n'
        if [ -n "$previous" ]; then
          if printf '%s' "$SEEN_PREVIOUS_FILES" |
               grep -Fqx "$previous"; then
            exit 1
          else
            grep_status=$?
            if [ "$grep_status" -ne 1 ]; then
              exit 1
            fi
          fi
          SEEN_PREVIOUS_FILES="${SEEN_PREVIOUS_FILES}${previous}"$'\n'
          PATH_RECORDS="${PATH_RECORDS}${previous}"$'\n'
        fi
        PATH_RECORDS="${PATH_RECORDS}${current}"$'\n'
        ACTUAL_FILES=$((ACTUAL_FILES + 1))
      done <<< "$RECORDS"
      # Compare the API's canonical decimal text without Bash integer
      # coercion: an out-of-range value must fail closed, not turn the
      # `[` diagnostic into a false condition.
      if [ "$ACTUAL_FILES" != "$EXPECTED_FILES" ]; then
        exit 1
      fi
      while IFS= read -r encoded; do
        if [ -z "$encoded" ]; then
          continue
        fi
        if ! printf '%s' "$encoded" | base64 --decode; then
          exit 1
        fi
        printf '\0'
      done <<< "$PATH_RECORDS"
    } |
      base64 -w0
  ) && [ -n "$FILES" ]; then
    RESOLVED=true
  fi
else
  BEFORE="$CI_BEFORE_SHA"
  # The zero SHA means a new branch or a force push with no prior tip. For a
  # merge group, CI_BEFORE_SHA is the queue-provided base_sha and GITHUB_SHA is
  # the synthetic candidate head.
  if [ "$BEFORE" != "0000000000000000000000000000000000000000" ] \
     && git cat-file -e "${BEFORE}^{commit}" 2>/dev/null; then
    # Two dots, not three. A symmetric difference has to compute a
    # merge base, which is a second way to fail and is not what
    # change detection is asking. "What differs between the old tip
    # and the new tip" is exactly the two-dot diff, and for the
    # fast-forward pushes main receives the two agree anyway.
    # Disable rename collapsing so both the deletion and addition are
    # classified, then preserve arbitrary path bytes through base64.
    if FILES=$(
      git diff --no-renames --name-only -z "$BEFORE" "$GITHUB_SHA" |
        base64 -w0
    ); then
      RESOLVED=true
    fi
  fi
fi
DECODED_FILES=""
if [ "$RESOLVED" = "true" ]; then
  DECODED_FILES=$(mktemp) || DECODED_FILES=""
  if [ -z "$DECODED_FILES" ] \
     || ! printf '%s' "$FILES" | base64 --decode > "$DECODED_FILES"; then
    RESOLVED=false
  elif [ -s "$DECODED_FILES" ]; then
    LAST_BYTE=$(
      tail -c 1 "$DECODED_FILES" | od -An -t u1 | tr -d '[:space:]'
    ) || RESOLVED=false
    if [ "$LAST_BYTE" != "0" ]; then
      RESOLVED=false
    fi
  fi
  if [ "$RESOLVED" = "true" ] && [ -s "$DECODED_FILES" ]; then
    while IFS= read -r -d '' file; do
      case "$file" in
        ""|/*|*/|*//*)
          RESOLVED=false
          break
          ;;
      esac
      case "/$file/" in
        */./*|*/../*)
          RESOLVED=false
          break
          ;;
      esac
    done < "$DECODED_FILES"
  fi
fi
if [ "$RESOLVED" != "true" ]; then
  if [ -n "$DECODED_FILES" ]; then
    rm -f "$DECODED_FILES" || true
  fi
  # Force every gate on and stop here. Deliberately NOT "list every
  # tracked file and classify it": that would put another fallible
  # git call -- and a dependency on every case pattern below matching
  # something -- on the one path whose entire job is to be
  # infallible. `git ls-files` exits 128 on an unreadable index.
  # Keep this list in sync with the `outputs:` block above.
  echo "::warning title=Change detection fell back::Could not determine the changed files for $GITHUB_SHA; running every job so that no validation is skipped."
  for name in code csharpdiff decompiler docs ildiff ilroundtrip packaging shipped web skills tla; do
    echo "$name=true" >> "$GITHUB_OUTPUT"
  done
  echo "Change detection fell back; every job filter forced true."
  exit 0
fi
CODE=false
CSHARPDIFF=false
DECOMPILER=false
DOCS=false
ILDIFF=false
ILROUNDTRIP=false
PACKAGING=false
SHIPPED=false
WEB=false
SKILLS=false
TLA=false
DECOMPILER_SKIP_PROJECTS_READY=true
DECOMPILER_SKIP_PROJECTS=()
DECOMPILER_SKIP_PROJECTS_FILE=eng/decompiler-gate-skip-projects.txt
WEB_PROJECTS_READY=true
WEB_PROJECTS=()
WEB_PROJECTS_FILE=eng/inspect-web-gate-projects.txt
if [ ! -f "$DECOMPILER_SKIP_PROJECTS_FILE" ]; then
  DECOMPILER_SKIP_PROJECTS_READY=false
else
  while IFS= read -r project || [ -n "$project" ]; do
    case "$project" in
      src/*|tests/*|tools/*) ;;
      *) DECOMPILER_SKIP_PROJECTS_READY=false; break ;;
    esac
    case "/$project/" in
      *"//"*|*"/./"*|*"/../"*) DECOMPILER_SKIP_PROJECTS_READY=false; break ;;
    esac
    if [ ! -d "$project" ]; then
      DECOMPILER_SKIP_PROJECTS_READY=false
      break
    fi
    DECOMPILER_SKIP_PROJECTS+=("$project/")
  done < "$DECOMPILER_SKIP_PROJECTS_FILE"
fi
if [ "$DECOMPILER_SKIP_PROJECTS_READY" != "true" ]; then
  echo "::warning title=Decompiler change filter fell back::Could not load $DECOMPILER_SKIP_PROJECTS_FILE; no source, test, or tool project will be exempted from decompiler gates."
fi
if [ ! -f "$WEB_PROJECTS_FILE" ]; then
  WEB_PROJECTS_READY=false
else
  while IFS= read -r project || [ -n "$project" ]; do
    case "$project" in
      src/*) ;;
      *) WEB_PROJECTS_READY=false; break ;;
    esac
    case "/$project/" in
      *"//"*|*"/./"*|*"/../"*) WEB_PROJECTS_READY=false; break ;;
    esac
    if [ ! -d "$project" ]; then
      WEB_PROJECTS_READY=false
      break
    fi
    WEB_PROJECTS+=("$project/")
  done < "$WEB_PROJECTS_FILE"
  if [ "${#WEB_PROJECTS[@]}" -eq 0 ]; then
    WEB_PROJECTS_READY=false
  fi
fi
if [ "$WEB_PROJECTS_READY" != "true" ]; then
  echo "::warning title=Inspect Web change filter fell back::Could not load $WEB_PROJECTS_FILE; every src change will run the Browser/Wasm lane."
fi
skips_decompiler_project() {
  if [ "$DECOMPILER_SKIP_PROJECTS_READY" != "true" ]; then
    return 1
  fi
  local project
  for project in "${DECOMPILER_SKIP_PROJECTS[@]}"; do
    case "$1" in
      "$project"*) return 0 ;;
    esac
  done
  return 1
}
is_web_project_path() {
  if [ "$WEB_PROJECTS_READY" != "true" ]; then
    case "$1" in
      src/*) return 0 ;;
      *) return 1 ;;
    esac
  fi
  local project
  for project in "${WEB_PROJECTS[@]}"; do
    case "$1" in
      "$project"*) return 0 ;;
    esac
  done
  return 1
}
selects_tla_job() {
  local path="$1"
  local path_lower
  case "$path" in
    .github/workflows/ci.yml|eng/ci-detect-changes.sh|eng/run-tla-checks.sh|eng/test-tla-checks.sh|eng/tla-module-overrides.txt|eng/tla-expected-exit-codes.txt)
      return 0
      ;;
  esac
  path_lower=$(printf '%s' "$path" | tr '[:upper:]' '[:lower:]')
  case "$path_lower" in
    docs/design/models/*/*.tla|docs/design/models/*/*.cfg)
      return 0
      ;;
    docs/models/*/*.tla|docs/models/*/*.cfg)
      return 0
      ;;
    docs/design/models/*.tla|docs/design/models/*.cfg)
      return 0
      ;;
    docs/models/*.tla|docs/models/*.cfg)
      return 0
      ;;
  esac
  return 1
}
while IFS= read -r -d '' file; do
  if [ "$file" = "eng/ci-detect-changes.sh" ]; then
    CODE=true
    CSHARPDIFF=true
    DECOMPILER=true
    DOCS=true
    ILDIFF=true
    ILROUNDTRIP=true
    PACKAGING=true
    SHIPPED=true
    WEB=true
    SKILLS=true
    TLA=true
    continue
  fi
  # Portable lowercase fold (avoids bash 4+ ${var,,}, since local dev on
  # macOS defaults to bash 3.2): used only where extension case must not
  # affect classification, e.g. the TLA+ patterns below, since
  # eng/run-tla-checks.sh discovers .tla/.cfg files case-insensitively
  # (find -iname) and a mismatch here would silently exempt an
  # uppercase/mixed-case module or config from the tla-plus job.
  file_lower=$(printf '%s' "$file" | tr '[:upper:]' '[:lower:]')
  if is_web_project_path "$file"; then
    CODE=true
    WEB=true
  fi
  case "$file" in
    # The browser engine is a real product consumer, so changes in its product dependency
    # graph must compile it as well as the solution. Test hosts, CLI-only projects, and
    # unrelated tools stay on the regular code lane.
    src/NetworkDestinationPolicy.cs|src/UnionPolyfill.cs) CODE=true; WEB=true ;;
    src/*) CODE=true ;;
    tests/ILInspector.MetadataPrimitives.PlatformProbe/*) CODE=true; WEB=true ;;
    tests/DotnetInspector.Artifacts.Local.PlatformProbe/*) CODE=true; WEB=true ;;
    tests/ILInspector.JsExportSurface.TypeScriptFixtures/*) CODE=true; WEB=true ;;
    tests/ILInspector.JsExportSurface.Tests/Fixtures/ts-jsexport-runtime/*) CODE=true; WEB=true ;;
    tests/*) CODE=true ;;
    tools/DecompilerHarness/*.md|tools/DecompilerHarness/*.txt) ;;
    tools/DecompilerHarness/*) CODE=true ;;
    # The file-based entrypoint and its conventional library own this
    # classifier gate and its pinned prerequisites. Editing either must run
    # the product test lane as well as executing the gate here in `changes`.
    eng/test-ci-change-detection.cs) CODE=true ;;
    eng/inspect-web-gate-projects.txt) CODE=true; WEB=true ;;
    eng/CiChangeDetection/PromotionWorkflowContract.cs) CODE=true; WEB=true ;;
    eng/CiChangeDetection/*) CODE=true ;;
    # Package fixture inputs are executable test evidence. The fast CLI test
    # lane packs and inspects them; the product pack job does not consume them.
    eng/package-fixtures/*) CODE=true ;;
    # The manifest catalog and its live verifier are executable compatibility
    # evidence consumed by PackageManifestCorpusTests.
    eng/package-manifest-corpus.json|eng/verify-package-manifest-corpus.cs) CODE=true ;;
    eng/prepare-decompiler-assertion-corpus.sh) CODE=true ;;
    eng/prepare-decompiler-corpus.sh) CODE=true ;;
    eng/prepare-decompiler-opt-in-corpus.sh) CODE=true ;;
    eng/prepare-decompiler-pr-corpus.sh) CODE=true ;;
    eng/prepare-authored-source-oracles.sh) CODE=true ;;
    eng/report-decompiler-opt-in-corpus-drift.sh) CODE=true ;;
    # The package sweep builds the EVIL pool the decompiler corpus is
    # run over, and EvilPoolSweepGateTests runs it as a subprocess.
    # Left out of this list, a change to it ran neither this lane nor
    # the gates, and reported green over jobs that had skipped.
    eng/prepare-decompiler-package-sweep.cs) CODE=true ;;
    # The script that runs the sweep to build the EVIL corpus. Missing from
    # every arm here, so editing it ran no lane at all.
    eng/prepare-evil-corpus.sh) CODE=true ;;
    # The pin the sweep reads, and what EvilPoolPinTests holds to the
    # sweep's own rules. Under docs/, which no arm here would catch.
    docs/data/nuget-top-packages.lock.json) CODE=true ;;
    docs/data/nuget-top-packages.json) CODE=true ;;
    # The PR test lane is what actually runs these scripts before merge (the
    # `Install ilasm/ildasm/mdv` step and IlToolsActivationTests), so
    # it is the only thing that can catch a break in them. Without
    # this, a PR touching only a script skips that lane and
    # `ci-required` passes on a `skipped`.
    eng/restore-iltools.sh) CODE=true ;;
    eng/activate-iltools.sh) CODE=true ;;
    eng/run-method-semantics-platform-probe.sh) CODE=true; WEB=true ;;
    eng/run-local-path-admission-platform-probe.sh) CODE=true; WEB=true ;;
    eng/test-ts-jsexport-context-aot.sh) CODE=true ;;
    eng/test-ts-jsexport-typescript.sh) WEB=true ;;
    eng/generate-inspect-web-multi-facade-canary.sh) WEB=true ;;
    eng/test-inspect-web-multi-facade-canary.sh) WEB=true ;;
    eng/validate-inspect-web-promotion.cs) WEB=true ;;
    eng/validate-inspect-web-promotion.sh) WEB=true ;;
    eng/generate-inspect-web-engine-facade.sh) WEB=true ;;
    eng/InspectWebAsyncLoweringReceipt.targets) WEB=true ;;
    eng/verify-inspect-web-async-deployment.sh) WEB=true ;;
    # Global analyzer input consumed by every product and Browser build.
    eng/BannedSymbols.txt) CODE=true; WEB=true ;;
    # Controls checkout line endings on Windows, including the raw
    # string fixtures this lane exists to validate.
    .gitattributes) CODE=true ;;
    # InstallScriptTests runs the Windows bootstrap under both PowerShell
    # engines; installer-only changes must reach that test lane.
    install.ps1) CODE=true ;;
    # GateExpectedClassesTests lives in the fast lane and is the only
    # thing keeping this file honest against the pre-merge preset, so
    # editing the file must run the lane that validates it.
    eng/decompiler-gate-expected-classes.txt) CODE=true ;;
    # Markdown under the browser prototype is documentation, not a browser
    # build input. Keep non-Markdown fixtures and configuration gated.
    prototypes/inspect-web/*.md) ;;
    prototypes/inspect-web/*) WEB=true ;;
    prototypes/annotated-source-viewer/*) WEB=true ;;
    *.props|*.targets|*.sln|*.slnx) CODE=true; WEB=true ;;
    .github/workflows/ci.yml) CODE=true; WEB=true ;;
    .github/workflows/deploy-inspect-web.yml) WEB=true ;;
    .github/workflows/deploy-inspect-web-coreclr.yml) WEB=true ;;
    .github/workflows/promote-inspect-web.yml) WEB=true ;;
    .github/workflows/*) CODE=true ;;
  esac
  # C# Diff smoke coverage is separate from the full test lane for the
  # same reason as IL Diff: CSharpDiffHarness-only changes get a
  # focused card/snapshot smoke instead of the entire test suite.
  case "$file" in
    tools/CSharpDiffHarness/*) CSHARPDIFF=true ;;
    src/ILInspector.Decompiler/*) CSHARPDIFF=true ;;
    src/ILInspector.ILDiff/*) CSHARPDIFF=true ;;
    src/ILInspector.Instructions/*) CSHARPDIFF=true ;;
    src/ILInspector.ControlFlow/*) CSHARPDIFF=true ;;
    src/DiffFixtures.V1/*) CSHARPDIFF=true ;;
    src/DiffFixtures.V2/*) CSHARPDIFF=true ;;
    tools/DiffHarnessCommon/*) CSHARPDIFF=true ;;
    Directory.Packages.props) CSHARPDIFF=true ;;
    *.props|*.targets|*.slnx) CSHARPDIFF=true ;;
    .github/workflows/ci.yml) CSHARPDIFF=true ;;
  esac
  # IL Diff smoke coverage is intentionally separate from the full
  # test lane: most PRs skip it, and IlDiffHarness-only changes get a
  # cheap targeted build/smoke instead of the whole test suite.
  case "$file" in
    tools/IlDiffHarness/*) ILDIFF=true ;;
    src/ILInspector.ILDiff/*) ILDIFF=true ;;
    src/ILInspector.Instructions/*) ILDIFF=true ;;
    src/ILInspector.ControlFlow/*) ILDIFF=true ;;
    src/DiffFixtures.V1/*) ILDIFF=true ;;
    src/DiffFixtures.V2/*) ILDIFF=true ;;
    tools/DiffHarnessCommon/*) ILDIFF=true ;;
    Directory.Packages.props) ILDIFF=true ;;
    *.props|*.targets|*.slnx) ILDIFF=true ;;
    .github/workflows/ci.yml) ILDIFF=true ;;
  esac
  case "$file" in
    # The lint *configuration* is not a `.md` file, so it needs its
    # own case: editing `.markdownlint.yaml` changes how every
    # document is linted, and without this the lint job skips and the
    # new configuration is never exercised. Only a root-level config
    # exists today, but a `case` pattern is anchored at the start of
    # the path, so match nested ones too rather than leaving the same
    # hole open one directory down.
    #
    # These patterns over-match slightly: `*` spans `/`, so a path
    # like `x/.markdownlint.d/y.cs` also trips them. That is the
    # deliberate direction to err in -- over-matching costs one short
    # lint run, while under-matching silently skips the job and, since
    # `ci-required` counts `skipped` as passing, hides it. `case` has
    # no way to anchor the basename, so tightening this would cost
    # more legibility than the false positive is worth.
    .markdownlint.*|.markdownlint-cli2.*) DOCS=true ;;
    */.markdownlint.*|*/.markdownlint-cli2.*) DOCS=true ;;
    *.md|*.txt|docs/*|skills/*) DOCS=true ;;
  esac
  # Shipped SKILL.md files are embedded resources. Their focused lane
  # checks registry/resource parity without paying for the full
  # product matrix. Other documents under skills/ need only linting.
  case "$file" in
    .github/workflows/ci.yml) SKILLS=true ;;
    skills/*/*/SKILL.md) ;;
    skills/*/SKILL.md) SKILLS=true ;;
  esac
  # TLA+ models get their own SANY/TLC verification lane rather than riding
  # on the docs lint job, since checking them costs real compute. Root-level
  # and deeply nested files deliberately over-match here so the runner can
  # report their unsupported layout instead of the job silently skipping.
  if selects_tla_job "$file"; then
    TLA=true
  fi
  # The decompiler docket/byte-neutrality gates cost ~8 minutes, so
  # they run as their own job rather than in the hot `test` lane.
  #
  # Source, test, and tool projects run this gate by default. The
  # small skip list contains only measured false positives, and the
  # self-test proves none is in the evaluated project graph rooted at
  # ILInspector.Decompiler.Tests. New projects therefore run the gate
  # without requiring any list update. If the skip list cannot be
  # loaded, no project is exempted.
  #
  # The documentation-extension arm covers only `*.md`,
  # deliberately: a `.txt` or `.jsonl` under a selected project can
  # be a corpus or baseline fixture, and excluding it by extension
  # would silently un-gate it. `global.json` selects the SDK, which
  # selects the compiler and the IL compared.
  case "$file" in
    eng/check-decompiler-gate.cs) DECOMPILER=true ;;
    eng/decompiler-gate-known-red.txt) DECOMPILER=true ;;
    eng/decompiler-gate-expected-classes.txt) DECOMPILER=true ;;
    eng/decompiler-gate-skip-projects.txt) DECOMPILER=true ;;
    # Deliberately NOT mapped to this lane, though an earlier revision of this
    # PR mapped all four here on the premise that "the sweep builds the EVIL
    # pool this gate is run over". Measured, and false: `--gate pre-merge`
    # selects ByteNeutralityGateTests, ByteDivergentGateTests,
    # ClusterCaptureTests, FidelityGateTests, GateExpectedClassesTests,
    # LoweredFidelityGateTests and
    # PrinterPrecedenceTests, none of which read the EVIL pool, and this
    # job never runs eng/prepare-evil-corpus.sh. The mapping scheduled a
    # fifteen-minute job that validated nothing about the file that triggered it.
    # EvilPoolSweepGateTests and EvilPoolPinTests carry no Speed=Slow trait, so
    # the `test` job's `-trait- "Speed=Slow"` runs all of them; CODE=true above
    # is the whole coverage these paths have, and is enough.
    #
    #   eng/prepare-decompiler-package-sweep.cs
    #   eng/prepare-evil-corpus.sh
    #   docs/data/nuget-top-packages.json
    #   docs/data/nuget-top-packages.lock.json
    #
    # Map them here if this lane ever consumes the pool.
    *.md) ;;
    global.json) DECOMPILER=true ;;
    *.props|*.targets|*.slnx) DECOMPILER=true ;;
    .github/workflows/ci.yml) DECOMPILER=true ;;
    src/*|tests/*|tools/*)
      if ! skips_decompiler_project "$file"; then
        DECOMPILER=true
      fi
      ;;
  esac
  # The IL round-trip oracle has a fast PR subset plus a slow assembly
  # sweep. It depends on ILInspector.Metadata ->
  # { DotnetInspector.Core, ILInspector.MetadataPrimitives } plus the
  # vendored ILAssembler restore script and the test project itself.
  case "$file" in
    tests/DotnetInspector.ILRoundtrip.Tests/*) ILROUNDTRIP=true ;;
    eng/restore-ilassembler.sh) ILROUNDTRIP=true ;;
    src/ILInspector.Metadata*) ILROUNDTRIP=true ;;
    src/DotnetInspector.Core/*) ILROUNDTRIP=true ;;
    *.props|*.targets|*.sln|*.slnx) ILROUNDTRIP=true ;;
  esac
  # Packaging/pack-smoke validation only needs to run when something
  # that affects the *packaged* tool changes. The pack job runs
  # `dotnet pack src/dotnet-inspect`, so the output is determined by
  # that project's csproj plus shared build infra (root + src build
  # props/targets and global.json) and the pack workflow
  # itself. Test/harness csproj changes and unrelated workflow edits
  # don't affect the package, so they must not trip this. Most PRs
  # (e.g. decompiler raises) touch none of these, keeping pack off the
  # hot PR path. The pack job is a PR-only packaging smoke test and
  # never produces release artifacts: release.yml builds all packages
  # fresh at publish time from the resolved commit SHA.
  case "$file" in
    src/dotnet-inspect/dotnet-inspect.csproj) PACKAGING=true ;;
    Directory.Build.props|Directory.Build.targets|Directory.Packages.props) PACKAGING=true ;;
    src/Directory.Build.props) PACKAGING=true ;;
    global.json) PACKAGING=true ;;
    .github/workflows/ci.yml|.github/workflows/release.yml) PACKAGING=true ;;
  esac
  # net10.0 fallback build coverage. The shipped tool graph
  # (src/dotnet-inspect + its product library references) must compile
  # against the net10.0 LTS floor of the official "any" package, but
  # the full test lane builds net11 only. Fire on shipped source (any
  # non-test, non-fixture src/ project) and shared build infra; test
  # projects, fixtures, harnesses, and docs can't introduce a
  # net11-only API into the shipped tool, so they don't trip this.
  case "$file" in
    src/*Tests/*) ;;
    src/*Fixtures*/*|src/DiffFixtures*/*) ;;
    src/*) SHIPPED=true ;;
    Directory.Build.props|Directory.Build.targets|Directory.Packages.props) SHIPPED=true ;;
    src/Directory.Build.props) SHIPPED=true ;;
    global.json) SHIPPED=true ;;
    .github/workflows/ci.yml) SHIPPED=true ;;
  esac
done < "$DECODED_FILES"
rm -f "$DECODED_FILES" || true

# The pull-request Files API describes the merge-base-to-PR-head diff, while
# the job checks GitHub's current synthetic merge candidate. A base rename can
# therefore move a PR-authored edit into or out of a model path without that
# path appearing in the API response. Recompute only the TLA scheduling
# decision from the same current-base-to-HEAD diff the TLA job will consume.
if [ "$GITHUB_EVENT_NAME" = "pull_request" ]; then
  TLA=false
  tla_candidate_files=$(mktemp) || tla_candidate_files=""
  if [ -n "$tla_candidate_files" ] \
     && [ -n "${CI_BEFORE_SHA:-}" ] \
     && git cat-file -e "${CI_BEFORE_SHA}^{commit}" 2>/dev/null \
     && git diff --no-renames --name-only -z "$CI_BEFORE_SHA" HEAD -- \
       > "$tla_candidate_files"; then
    tla_candidate_stream_valid=true
    if [ -s "$tla_candidate_files" ]; then
      tla_candidate_last_byte=$(
        tail -c 1 "$tla_candidate_files" | od -An -t u1 | tr -d '[:space:]'
      ) || tla_candidate_stream_valid=false
      if [ "$tla_candidate_last_byte" != "0" ]; then
        tla_candidate_stream_valid=false
      fi
    fi
    if [ "$tla_candidate_stream_valid" = "true" ]; then
      while IFS= read -r -d '' tla_candidate_file; do
        if selects_tla_job "$tla_candidate_file"; then
          TLA=true
        fi
      done < "$tla_candidate_files"
    else
      TLA=true
      echo "::warning title=TLA+ change filter fell back::The candidate changed-file stream was incomplete; running the TLA+ job so the failure is visible there."
    fi
  else
    TLA=true
    echo "::warning title=TLA+ change filter fell back::Could not compare the pull-request base with the checked-out candidate; running the TLA+ job so the failure is visible there."
  fi
  if [ -n "$tla_candidate_files" ]; then
    rm -f "$tla_candidate_files" || true
  fi
fi
# `ilroundtrip` does not have a job of its own: its two steps live
# inside the `test` job, which is gated on `code`. So an input that
# sets only `ilroundtrip` computes an output nothing can act on --
# the job never starts and the steps never run. Every other member of
# the ilroundtrip list happens to set `code` too, which is why this
# was invisible; `eng/restore-ilassembler.sh` did not, and its
# validation silently never ran.
#
# State the implication once here rather than duplicating `code` into
# each ilroundtrip case, so a future addition to that list cannot
# reintroduce the gap by forgetting it.
if [ "$ILROUNDTRIP" = true ]; then
  CODE=true
fi
echo "code=$CODE" >> "$GITHUB_OUTPUT"
echo "csharpdiff=$CSHARPDIFF" >> "$GITHUB_OUTPUT"
echo "decompiler=$DECOMPILER" >> "$GITHUB_OUTPUT"
echo "docs=$DOCS" >> "$GITHUB_OUTPUT"
echo "ildiff=$ILDIFF" >> "$GITHUB_OUTPUT"
echo "ilroundtrip=$ILROUNDTRIP" >> "$GITHUB_OUTPUT"
echo "packaging=$PACKAGING" >> "$GITHUB_OUTPUT"
echo "shipped=$SHIPPED" >> "$GITHUB_OUTPUT"
echo "web=$WEB" >> "$GITHUB_OUTPUT"
echo "skills=$SKILLS" >> "$GITHUB_OUTPUT"
echo "tla=$TLA" >> "$GITHUB_OUTPUT"
echo "Changed files were decoded and classified without logging raw path bytes."
echo "code=$CODE csharpdiff=$CSHARPDIFF decompiler=$DECOMPILER docs=$DOCS ildiff=$ILDIFF ilroundtrip=$ILROUNDTRIP packaging=$PACKAGING shipped=$SHIPPED web=$WEB skills=$SKILLS tla=$TLA"