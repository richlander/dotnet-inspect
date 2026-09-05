import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import {
  createMethodBodyDiffState,
  methodBodySelectionKey,
  type MethodBodyDiffState,
} from "../src/method-body-comparison.ts";
import {
  renderMethodBodyComparisonAction,
  renderMethodBodyDiffModal,
} from "../src/method-body-diff-view.ts";
import type {
  BrowserMethodBodyComparison,
  BrowserMethodBodyProducer,
  BrowserMethodBodySelection,
  BrowserMethodBodyTargets,
} from "../src/facades/inspect-web-source.d.ts";

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const highlightCSharp = (value: string) => escapeHtml(value);

function selection(
  overrides: Partial<BrowserMethodBodySelection> = {},
): BrowserMethodBodySelection {
  return {
    typeIdentity: "Example.Widget",
    memberName: "Build",
    selectorKey: "method:Build()",
    metadataToken: 0x06000001,
    label: "Example.Widget.Build()",
    ...overrides,
  };
}

const before = selection();
const after = selection({
  memberName: "Rebuild",
  selectorKey: "method:Rebuild()",
  metadataToken: 0x06000002,
  label: "Example.Widget.Rebuild()",
});
const hostile = selection({
  memberName: "<script>alert(1)</script>",
  selectorKey: "method:<script>()",
  metadataToken: 0x06000009,
  label: "Example.Widget.<script>alert(1)</script>()",
});

const targets: BrowserMethodBodyTargets = {
  packageId: "Example.Package",
  version: "1.2.3",
  framework: "net10.0",
  assembly: "Example.Package",
  moduleVersionId: "0f5f6a4a-6d59-4b0c-9e9e-2b7d1a6c1234",
  before,
  methods: [after, hostile],
};

const request = {
  packageId: targets.packageId,
  version: targets.version,
  framework: targets.framework,
  assembly: targets.assembly,
  moduleVersionId: targets.moduleVersionId,
  before,
  after,
};

function endpoint(state: string, detail: string | null = null) {
  return {
    state,
    targetState: "Resolved",
    metadataToken: 0x06000001,
    moduleVersionId: targets.moduleVersionId,
    detail,
  };
}

const cSharpProducer: BrowserMethodBodyProducer = {
  producer: "CSharp",
  outcome: "Completed",
  nativeVerdict: "Different",
  before: endpoint("BodyAvailable"),
  after: endpoint("BodyAvailable"),
  cSharp: {
    isExact: false,
    rows: [
      {
        assemblyIdentity: "Example.Package, Version=1.2.3.0",
        stableMemberKey: "M:Example.Widget.Build",
        changeId: "csharp-3-1",
        kind: "Changed",
        hunkId: 3,
        line: 12,
        member: "Example.Widget.Build()",
        fidelity: "Exact",
        sourceCoordinate: "Widget.cs(12,9)",
        text: "return new Widget(\"<b>\");",
        oldValue: "new Widget(\"a\")",
        newValue: "new Widget(\"<b>\")",
        oldOperation: { kind: "ObjectCreation", value: "Widget..ctor" },
        newOperation: { kind: "ObjectCreation", value: "Widget..ctor" },
        message: "The constructed argument changed.",
      },
    ],
  },
  il: null,
  diagnostics: [],
};

const ilProducer: BrowserMethodBodyProducer = {
  producer: "IlBody",
  outcome: "Completed",
  nativeVerdict: "Different",
  before: endpoint("BodyAvailable"),
  after: endpoint("NoBody", "the After method has no implementation body"),
  cSharp: null,
  il: {
    isExact: false,
    isAvailable: true,
    outcome: "Compared",
    failure: null,
    rows: [
      {
        kind: "Removed",
        hunkId: 1,
        operation: {
          offset: 6,
          opcodeFamily: "Call",
          operand: { kind: "MemberReference", value: "Widget..ctor" },
        },
        message: "The call was removed.",
      },
    ],
  },
  diagnostics: [
    {
      kind: "EndpointEvidence",
      side: "after",
      mechanism: "IlBody",
      hunkId: null,
      subjectToken: 0x06000002,
      path: null,
      message: "The After endpoint reported no body.",
      detail: null,
    },
  ],
};

function comparison(
  producers: readonly BrowserMethodBodyProducer[],
  overrides: Partial<BrowserMethodBodyComparison> = {},
): BrowserMethodBodyComparison {
  return {
    request,
    stage: "Research",
    outcome: "Completed",
    producers,
    diagnostics: [],
    ...overrides,
  };
}

function openState(
  overrides: Partial<MethodBodyDiffState> = {},
): MethodBodyDiffState {
  return Object.assign(createMethodBodyDiffState(), {
    open: true,
    context: {
      packageId: targets.packageId,
      version: targets.version,
      framework: targets.framework,
      assembly: targets.assembly,
      typeIdentity: before.typeIdentity,
      memberName: before.memberName,
      selectorKey: before.selectorKey,
      metadataToken: before.metadataToken,
      label: before.label,
    },
    targets,
  }, overrides);
}

function render(state: MethodBodyDiffState): string {
  return renderMethodBodyDiffModal({ state, escapeHtml, highlightCSharp });
}

test("an unavailable context states its reason on a visible control", () => {
  const html = renderMethodBodyComparisonAction(
    {
      available: false,
      reason: "Select one accessor or body <first>.",
    },
    escapeHtml);

  assert.match(html, /id="compare-method-bodies"/);
  assert.match(html, / disabled/);
  assert.match(html, /aria-describedby="compare-method-bodies-reason"/);
  assert.match(html, /Select one accessor or body &lt;first&gt;\./);
  assert.doesNotMatch(html, /<first>/);

  const available = renderMethodBodyComparisonAction(
    { available: true, reason: "" },
    escapeHtml);
  assert.doesNotMatch(available, / disabled/);
  assert.doesNotMatch(available, /compare-method-bodies-reason/);
});

test("the dialog explains an unavailable launch instead of comparing", () => {
  const html = render(openState({
    targets: null,
    unavailableReason: "This member has no implementation method body.",
  }));

  assert.match(html, /role="dialog"/);
  assert.match(html, /id="method-body-diff-title"/);
  assert.match(html, /Comparison is unavailable here/);
  assert.match(html, /This member has no implementation method body\./);
  assert.doesNotMatch(html, /id="method-body-diff-compare"/);
});

test("Compare stays disabled until an After method is chosen", () => {
  const unchosen = render(openState());
  assert.match(
    unchosen,
    /id="method-body-diff-compare"[^>]*data-method-body-action="compare" disabled/);
  assert.match(unchosen, /Choose an After method to enable Compare\./);
  // The launching method is offered too: a same-method pair is a valid request.
  assert.match(unchosen, /3 of 3 methods shown/);
  assert.match(
    unchosen,
    /<option value="Example\.Widget␟Build␟method:Build\(\)␟100663297">/);

  const chosen = render(openState({ candidateKey: methodBodySelectionKey(after) }));
  assert.doesNotMatch(
    chosen,
    /id="method-body-diff-compare"[^>]*disabled/);
  assert.match(chosen, /Choose an After method, then select Compare\./);
});

test("both compared identities come from the submitted request", () => {
  const html = render(openState({
    candidateKey: methodBodySelectionKey(hostile),
    submittedRequest: request,
    comparison: comparison([cSharpProducer, ilProducer]),
  }));

  const beforeSection = html.slice(
    html.indexOf('data-method-body-side="before" aria-label="Compared Before method"'));
  const afterSection = beforeSection.slice(
    beforeSection.indexOf('data-method-body-side="after"'));
  assert.match(beforeSection, /Example\.Widget\.Build\(\)/);
  assert.match(afterSection, /Example\.Widget\.Rebuild\(\)/);
  assert.match(afterSection, /0x06000002/);
  assert.doesNotMatch(afterSection, /alert\(1\)/);
});

test("each native mechanism keeps its own outcome, verdict and evidence", () => {
  const failedCSharp: BrowserMethodBodyProducer = {
    ...cSharpProducer,
    outcome: "Failed",
    nativeVerdict: "Unavailable",
    cSharp: null,
    diagnostics: [
      {
        kind: "MechanismFailure",
        side: null,
        mechanism: "CSharp",
        hunkId: null,
        subjectToken: null,
        path: null,
        message: "The C# mechanism could not raise a body.",
        detail: null,
      },
    ],
  };
  const html = render(openState({
    comparison: comparison([ilProducer, failedCSharp]),
  }));

  const cSharpIndex = html.indexOf('data-method-body-producer="CSharp"');
  const ilIndex = html.indexOf('data-method-body-producer="IlBody"');
  assert.ok(cSharpIndex >= 0 && ilIndex > cSharpIndex, "C# is the primary region");

  const cSharpSection = html.slice(cSharpIndex, ilIndex);
  assert.match(cSharpSection, /<span data-method-body-outcome>Failed<\/span>/);
  assert.match(cSharpSection, /<span data-method-body-verdict>Unavailable<\/span>/);
  assert.match(cSharpSection, /This mechanism returned no aligned body evidence\./);
  assert.doesNotMatch(cSharpSection, /data-method-body-exact="true"/);
  assert.match(cSharpSection, /The C# mechanism could not raise a body\./);

  const ilSection = html.slice(ilIndex);
  assert.match(ilSection, /<span data-method-body-il-outcome>Compared<\/span>/);
  assert.match(ilSection, /<details class="method-body-il-disclosure">/);
  assert.match(ilSection, /IL_0006/);
  assert.match(ilSection, /MemberReference: Widget\.\.ctor/);
  assert.match(
    ilSection,
    /data-method-body-endpoint="after">\s*<strong>After<\/strong>\s*<span data-method-body-endpoint-state>NoBody<\/span>/);
  assert.match(ilSection, /the After method has no implementation body/);
});

test("C# rows keep their kind, coordinates and both operation values", () => {
  const html = render(openState({
    comparison: comparison([cSharpProducer]),
  }));

  assert.match(
    html,
    /data-method-body-row="csharp"\s+data-method-body-kind="Changed"\s+data-method-body-hunk="3"/);
  assert.match(html, /line 12 · hunk 3 · Widget\.cs\(12,9\) · Exact/);
  assert.match(
    html,
    /data-method-body-value="old"[\s\S]*?new Widget\(&quot;a&quot;\)[\s\S]*?ObjectCreation: Widget\.\.ctor/);
  assert.match(
    html,
    /data-method-body-value="new"[\s\S]*?new Widget\(&quot;&lt;b&gt;&quot;\)/);
  assert.match(html, /data-method-body-change="csharp-3-1"/);
  assert.match(html, /data-method-body-member-key="M:Example\.Widget\.Build"/);
  assert.match(html, /not exact under this mechanism/);
  assert.doesNotMatch(html, /<b>/);
});

test("native line operations supply the paired C# text without duplicate body lines", () => {
  const evidence = cSharpProducer.cSharp;
  assert.ok(evidence);
  const row = evidence.rows[0];
  assert.ok(row);
  const html = render(openState({
    comparison: comparison([{
      ...cSharpProducer,
      cSharp: {
        isExact: false,
        rows: [{
          ...row, text: "return 2;", oldValue: null, newValue: null,
          oldOperation: null, newOperation: { kind: "Line", value: "return 2;" },
        }],
      },
    }]),
  }));
  assert.match(html, /data-method-body-value="new"[\s\S]*<code class="language-csharp">return 2;<\/code>/);
  assert.equal(html.split("return 2;").length - 1, 1);
});

test("hostile inventory text is escaped in the chooser and diagnostics", () => {
  const html = render(openState({
    candidateKey: methodBodySelectionKey(hostile),
    comparison: comparison([], {
      diagnostics: [
        {
          kind: "Request",
          side: null,
          mechanism: null,
          hunkId: null,
          subjectToken: null,
          path: "<img src=x onerror=alert(1)>",
          message: "<script>alert(2)</script>",
          detail: "<b>detail</b>",
        },
      ],
    }),
  }));

  assert.doesNotMatch(html, /<script>/);
  assert.doesNotMatch(html, /<img /);
  assert.doesNotMatch(html, /<b>detail<\/b>/);
  assert.match(html, /&lt;script&gt;alert\(2\)&lt;\/script&gt;/);
  assert.match(html, /&lt;img src=x onerror=alert\(1\)&gt;/);
});

test("an inventory failure stays visible in the chooser region", () => {
  const html = render(openState({
    targets: null,
    targetsError: "The implementation assembly is unavailable.",
  }));

  assert.match(
    html,
    /class="method-body-failure" role="alert">The implementation assembly is unavailable\./);
});

test("paired regions carry side labels that survive stacking", () => {
  const html = render(openState());
  assert.match(html, /data-method-body-side="before" aria-label="Before method"/);
  assert.match(html, /data-method-body-side="after" aria-label="After method"/);

  const styles = readFileSync(
    new URL("../src/styles.css", import.meta.url),
    "utf8");
  const responsive = styles.slice(styles.lastIndexOf("@media (max-width: 820px)"));
  assert.match(
    responsive,
    /\.method-body-pair,\s*\.method-body-endpoints,\s*\.method-body-row-values \{ grid-template-columns: 1fr; \}/);
});

test("the app hosts the action, dialog and its release points", () => {
  const source = readFileSync(
    new URL("../src/dotnet-inspect.ts", import.meta.url),
    "utf8");

  assert.match(
    source,
    /methodBodyPageContext\s*\?\s*renderMethodBodyComparisonAction\(/);
  assert.match(source, /renderMethodBodyDiffModal\(\{\s*state: state\.methodBodyDiff/);
  assert.match(
    source,
    /function clearMemberContentCache\(\) \{[\s\S]{0,220}methodBodyComparison\.dispose\(\);/);
  assert.match(
    source,
    /id: "method-body-diff\.dismiss",\s*key: "Escape",/);
  assert.match(source, /state\.methodBodyDiff\.open/);
});
