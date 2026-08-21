import assert from "node:assert/strict";
import test from "node:test";
import {
  bindPackageOpportunities,
  renderPackageOpportunities,
  type PackageOpportunitiesBindingActions,
} from "../src/package-opportunities.ts";

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  private readonly listeners = new Map<string, EventListener[]>();

  constructor(dataset: Record<string, string | undefined> = {}) {
    this.dataset = dataset;
  }

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener({} as Event);
    }
  }
}

class FakeRoot {
  private readonly elements = new Map<string, FakeElement[]>();

  add(selector: string, ...elements: FakeElement[]) {
    this.elements.set(selector, elements);
    return elements;
  }

  querySelectorAll(selector: string) {
    return this.elements.get(selector) ?? [];
  }
}

function recordingActions(calls: string[]): PackageOpportunitiesBindingActions {
  return {
    onLookForSelect: query => calls.push(`look:${query}`),
    onPackageSelect: packageId => calls.push(`package:${packageId}`),
    onTypeSelect: typeId => calls.push(`type:${typeId}`),
  };
}

test("opportunity bindings dispatch type, package, and search actions", () => {
  const root = new FakeRoot();
  const type = new FakeElement({ oppType: "Contoso.Widget" });
  const secondType = new FakeElement({ oppType: "Contoso.Gadget" });
  const packageChip = new FakeElement({ oppPackage: "Contoso.Extensions" });
  const secondPackage = new FakeElement({ oppPackage: "Contoso.Hosting" });
  const lookFor = new FakeElement({ oppLookfor: "AddWidgets" });
  const secondLookFor = new FakeElement({ oppLookfor: "AddGadgets" });
  root.add("[data-opp-type]", type, secondType);
  root.add("[data-opp-package]", packageChip, secondPackage);
  root.add("[data-opp-lookfor]", lookFor, secondLookFor);
  const calls: string[] = [];
  bindPackageOpportunities(
    root as unknown as ParentNode,
    recordingActions(calls));

  assert.deepEqual(calls, []);
  type.dispatch("click");
  assert.deepEqual(calls, ["type:Contoso.Widget"]);
  packageChip.dispatch("click");
  assert.deepEqual(calls, [
    "type:Contoso.Widget",
    "package:Contoso.Extensions",
  ]);
  lookFor.dispatch("click");

  assert.deepEqual(calls, [
    "type:Contoso.Widget",
    "package:Contoso.Extensions",
    "look:AddWidgets",
  ]);
  secondType.dispatch("click");
  secondPackage.dispatch("click");
  secondLookFor.dispatch("click");

  assert.deepEqual(calls, [
    "type:Contoso.Widget",
    "package:Contoso.Extensions",
    "look:AddWidgets",
    "type:Contoso.Gadget",
    "package:Contoso.Hosting",
    "look:AddGadgets",
  ]);
});

test("opportunity bindings preserve empty values for malformed controls", () => {
  const root = new FakeRoot();
  const type = new FakeElement();
  const packageChip = new FakeElement();
  const lookFor = new FakeElement();
  root.add("[data-opp-type]", type);
  root.add("[data-opp-package]", packageChip);
  root.add("[data-opp-lookfor]", lookFor);
  const calls: string[] = [];
  bindPackageOpportunities(
    root as unknown as ParentNode,
    recordingActions(calls));

  assert.deepEqual(calls, []);
  type.dispatch("click");
  assert.deepEqual(calls, ["type:"]);
  packageChip.dispatch("click");
  assert.deepEqual(calls, ["type:", "package:"]);
  lookFor.dispatch("click");

  assert.deepEqual(calls, ["type:", "package:", "look:"]);
});

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const baseOptions = {
  isPlatform: false,
  scopedLibrary: null,
  activeFramework: "net10.0",
  picker: "",
  fresh: true,
  loading: false,
  error: "",
  data: null,
  escapeHtml,
};

test("a platform package with no scoped library prompts to pick one, before any scan runs", () => {
  const html = renderPackageOpportunities({
    ...baseOptions,
    isPlatform: true,
    scopedLibrary: null,
    picker: "<PICKER>",
    fresh: false,
  });

  assert.match(html, /^<PICKER>/);
  assert.match(html, /Pick a library to scan/);
});

test("a fresh scan in progress shows the scanning status", () => {
  const html = renderPackageOpportunities({ ...baseOptions, loading: true });

  assert.match(html, /Scanning opportunities…/);
});

test("a loading flag from a stale (non-fresh) scope does not show the scanning status", () => {
  const html = renderPackageOpportunities({ ...baseOptions, loading: true, fresh: false });

  assert.doesNotMatch(html, /Scanning opportunities…/);
  assert.match(html, /Loading…/);
});

test("a fresh scan error shows the failure message, escaped", () => {
  const html = renderPackageOpportunities({ ...baseOptions, error: "<boom> failed to load" });

  assert.match(html, /Opportunity scan failed/);
  assert.match(html, /&lt;boom&gt; failed to load/);
});

test("no data yet (fresh, no error, no data) shows a generic loading placeholder", () => {
  const html = renderPackageOpportunities(baseOptions);

  assert.match(html, /Loading…/);
});

test("empty categories render the no-opportunities message with the scan scope", () => {
  const html = renderPackageOpportunities({
    ...baseOptions,
    data: { categories: [], totalOpportunities: 0, inspectionError: null },
  });

  assert.match(html, /No integration opportunities/);
  assert.match(html, /net10\.0/);
});

test("a platform scan scope names the scoped library and framework", () => {
  const html = renderPackageOpportunities({
    ...baseOptions,
    isPlatform: true,
    scopedLibrary: "System.Text.Json",
    data: { categories: [], totalOpportunities: 0, inspectionError: null },
  });

  assert.match(html, /System\.Text\.Json · net10\.0/);
});

test("an inspection error renders a warning banner alongside categories", () => {
  const html = renderPackageOpportunities({
    ...baseOptions,
    data: {
      categories: [{ integration: "Auth", items: [] }],
      totalOpportunities: 0,
      inspectionError: "<bad> assembly",
    },
  });

  assert.match(html, /Some assemblies could not be scanned/);
  assert.match(html, /&lt;bad&gt; assembly/);
});

test("categories render a summary with area\/suggestion counts and a chip per category", () => {
  const html = renderPackageOpportunities({
    ...baseOptions,
    data: {
      categories: [
        { integration: "Auth", items: [{ api: "Widget", integrationType: "IServiceCollection registration", lookFor: "" }] },
        { integration: "Database", items: [] },
      ],
      totalOpportunities: 1,
      inspectionError: null,
    },
  });

  assert.match(html, /2 areas · 1 suggestion · net10\.0/);
  assert.match(html, /<span class="type-chip">Auth <span class="ns-count">1<\/span><\/span>/);
  assert.match(html, /<span class="type-chip">Database <span class="ns-count">0<\/span><\/span>/);
});

test("an opportunity row splits the API into short name and qualifier", () => {
  const html = renderPackageOpportunities({
    ...baseOptions,
    data: {
      categories: [{
        integration: "AI",
        items: [{ api: "System.ClientModel.Primitives.PipelineMessage", integrationType: "IServiceCollection registration", lookFor: "" }],
      }],
      totalOpportunities: 1,
      inspectionError: null,
    },
  });

  assert.match(html, /<span class="opp-type-name">PipelineMessage<\/span><span class="opp-type-ns">System\.ClientModel\.Primitives<\/span>/);
  assert.match(html, /data-opp-type="System\.ClientModel\.Primitives\.PipelineMessage"/);
});

test("an integration kind with a leading dotted namespace renders a load-on-demand package chip", () => {
  const html = renderPackageOpportunities({
    ...baseOptions,
    data: {
      categories: [{
        integration: "AI",
        items: [{ api: "Widget", integrationType: "Microsoft.Extensions.AI IChatClient extension", lookFor: "" }],
      }],
      totalOpportunities: 1,
      inspectionError: null,
    },
  });

  assert.match(html, /<button class="opp-package-chip" data-opp-package="Microsoft\.Extensions\.AI"/);
  assert.match(html, /<span class="opp-kind-text">IChatClient extension<\/span>/);
});

test("an integration kind with no dotted namespace renders as plain muted text", () => {
  const html = renderPackageOpportunities({
    ...baseOptions,
    data: {
      categories: [{
        integration: "Config",
        items: [{ api: "Widget", integrationType: "IServiceCollection registration", lookFor: "" }],
      }],
      totalOpportunities: 1,
      inspectionError: null,
    },
  });

  assert.doesNotMatch(html, /opp-package-chip/);
  assert.match(html, /<span class="opp-kind-text">IServiceCollection registration<\/span>/);
});

test("look-for tokens render as spotlight-seeded chips, one per comma-separated identifier", () => {
  const html = renderPackageOpportunities({
    ...baseOptions,
    data: {
      categories: [{
        integration: "AI",
        items: [{ api: "Widget", integrationType: "IServiceCollection registration", lookFor: "AddChatClient, AddEmbeddingGenerator" }],
      }],
      totalOpportunities: 1,
      inspectionError: null,
    },
  });

  assert.match(html, /<button class="opp-chip" data-opp-lookfor="AddChatClient"[^>]*>AddChatClient<\/button>/);
  assert.match(html, /<button class="opp-chip" data-opp-lookfor="AddEmbeddingGenerator"[^>]*>AddEmbeddingGenerator<\/button>/);
});

test("a wildcard look-for pattern renders as a muted, non-interactive hint", () => {
  const html = renderPackageOpportunities({
    ...baseOptions,
    data: {
      categories: [{
        integration: "Config",
        items: [{ api: "Widget", integrationType: "IServiceCollection registration", lookFor: "Add*" }],
      }],
      totalOpportunities: 1,
      inspectionError: null,
    },
  });

  assert.match(html, /<span class="opp-pattern" title="Naming pattern">Add\*<\/span>/);
  assert.doesNotMatch(html, /opp-chip/);
});

test("an empty look-for hint renders a generic any-registration-surface hint", () => {
  const html = renderPackageOpportunities({
    ...baseOptions,
    data: {
      categories: [{
        integration: "Config",
        items: [{ api: "Widget", integrationType: "IServiceCollection registration", lookFor: "" }],
      }],
      totalOpportunities: 1,
      inspectionError: null,
    },
  });

  assert.match(html, /<span class="opp-pattern">any registration surface<\/span>/);
});

test("API and integration-type text is escaped", () => {
  const html = renderPackageOpportunities({
    ...baseOptions,
    data: {
      categories: [{
        integration: "<Cat>",
        items: [{ api: "<Widget>", integrationType: "<bad> kind", lookFor: "<bad>" }],
      }],
      totalOpportunities: 1,
      inspectionError: null,
    },
  });

  assert.match(html, /&lt;Widget&gt;/);
  assert.match(html, /&lt;bad&gt; kind/);
  assert.match(html, /&lt;Cat&gt;/);
  assert.doesNotMatch(html, /<Widget>/);
});
