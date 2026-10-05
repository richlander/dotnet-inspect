import assert from "node:assert/strict";
import test from "node:test";
import {
  bindPackageOpportunities,
  renderOpportunityRow,
  type OpportunityItem,
  type PackageOpportunitiesBindingActions,
  type PackageOpportunityTarget,
} from "../src/package-opportunities.ts";
import { fakeDom } from "./fake-dom.ts";

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
      listener(fakeDom.event());
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
    onTypeSelect: target => calls.push(`type:${target.typeId}`),
  };
}

test("suggested integration bindings dispatch type, package, and search actions", () => {
  const root = new FakeRoot();
  const type = new FakeElement({ oppType: "Contoso.Widget" });
  const packageChip = new FakeElement({ oppPackage: "Contoso.Extensions" });
  const lookFor = new FakeElement({ oppLookfor: "AddWidgets" });
  root.add("[data-opp-type]", type);
  root.add("[data-opp-package]", packageChip);
  root.add("[data-opp-lookfor]", lookFor);
  const calls: string[] = [];
  bindPackageOpportunities(
    fakeDom.parentNode(root),
    recordingActions(calls));

  type.dispatch("click");
  packageChip.dispatch("click");
  lookFor.dispatch("click");

  assert.deepEqual(calls, [
    "type:Contoso.Widget",
    "package:Contoso.Extensions",
    "look:AddWidgets",
  ]);
});

test("suggested integration bindings preserve empty values for malformed controls", () => {
  const root = new FakeRoot();
  const type = new FakeElement();
  const packageChip = new FakeElement();
  const lookFor = new FakeElement();
  root.add("[data-opp-type]", type);
  root.add("[data-opp-package]", packageChip);
  root.add("[data-opp-lookfor]", lookFor);
  const calls: string[] = [];
  bindPackageOpportunities(
    fakeDom.parentNode(root),
    recordingActions(calls));

  type.dispatch("click");
  packageChip.dispatch("click");
  lookFor.dispatch("click");

  assert.deepEqual(calls, ["type:", "package:", "look:"]);
});

test("suggested integration bindings preserve exact source identity", () => {
  const root = new FakeRoot();
  const type = new FakeElement({
    oppType: "Contoso.Widget",
    oppSourceIdentity: "exact",
    oppSourceDefinition: "Contoso.Widget",
    oppSourceAssembly: "Contoso.Core",
    oppSourceVersion: "2.0.0.0",
    oppSourceCulture: "neutral",
    oppSourceToken: "0011223344556677",
  });
  root.add("[data-opp-type]", type);
  let selected: PackageOpportunityTarget | null = null;
  bindPackageOpportunities(fakeDom.parentNode(root), {
    ...recordingActions([]),
    onTypeSelect: target => { selected = target; },
  });

  type.dispatch("click");

  assert.deepEqual(selected, {
    typeId: "Contoso.Widget",
    sourceIdentity: "exact",
    sourceDefinitionId: "Contoso.Widget",
    sourceAssembly: "Contoso.Core",
    sourceAssemblyVersion: "2.0.0.0",
    sourceAssemblyCulture: "neutral",
    sourceAssemblyPublicKeyToken: "0011223344556677",
  });
});

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function suggestion(
  item: Pick<OpportunityItem, "api" | "integrationType" | "lookFor">
    & Partial<OpportunityItem>,
): OpportunityItem {
  return {
    sourceDefinitionId: item.api,
    sourceAssembly: "Test.Assembly",
    sourceAssemblyVersion: "1.0.0.0",
    sourceAssemblyCulture: null,
    sourceAssemblyPublicKeyToken: null,
    ...item,
  };
}

test("a suggested integration row retains exact source identity and qualified API display", () => {
  const html = renderOpportunityRow(suggestion({
    api: "System.ClientModel.Primitives.PipelineMessage",
    integrationType: "IServiceCollection registration",
    lookFor: "",
    sourceDefinitionId: 'System.ClientModel.Primitives.Pipeline"Message',
    sourceAssembly: "System.ClientModel",
    sourceAssemblyVersion: "1.2.3.4",
    sourceAssemblyPublicKeyToken: "0011223344556677",
  }), escapeHtml);

  assert.match(html, /<span class="opp-type-name">PipelineMessage<\/span><span class="opp-type-ns">System\.ClientModel\.Primitives<\/span>/);
  assert.match(html, /data-opp-source-definition="System\.ClientModel\.Primitives\.Pipeline&quot;Message"/);
  assert.match(html, /data-opp-source-assembly="System\.ClientModel"/);
  assert.match(html, /data-opp-source-version="1\.2\.3\.4"/);
  assert.match(html, /data-opp-source-token="0011223344556677"/);
});

test("a suggested package and concrete APIs remain interactive", () => {
  const html = renderOpportunityRow(suggestion({
    api: "Widget",
    integrationType: "Microsoft.Extensions.AI IChatClient extension",
    lookFor: "AddChatClient, AddEmbeddingGenerator",
  }), escapeHtml);

  assert.match(html, /data-opp-package="Microsoft\.Extensions\.AI"/);
  assert.match(html, /<span class="opp-kind-text">IChatClient extension<\/span>/);
  assert.match(html, /data-opp-lookfor="AddChatClient"/);
  assert.match(html, /data-opp-lookfor="AddEmbeddingGenerator"/);
});

test("wildcard and empty API hints remain non-interactive guidance", () => {
  const wildcard = renderOpportunityRow(suggestion({
    api: "Widget",
    integrationType: "IServiceCollection registration",
    lookFor: "Add*",
  }), escapeHtml);
  const empty = renderOpportunityRow(suggestion({
    api: "Widget",
    integrationType: "IServiceCollection registration",
    lookFor: "",
  }), escapeHtml);

  assert.match(wildcard, /<span class="opp-pattern" title="Naming pattern">Add\*<\/span>/);
  assert.doesNotMatch(wildcard, /opp-chip/);
  assert.match(empty, /<span class="opp-pattern">any registration surface<\/span>/);
});

test("suggested integration row text uses the supplied escape boundary", () => {
  const html = renderOpportunityRow(suggestion({
    api: "<Widget>",
    integrationType: "<bad> kind",
    lookFor: "<bad>",
  }), escapeHtml);

  assert.match(html, /&lt;Widget&gt;/);
  assert.match(html, /&lt;bad&gt; kind/);
  assert.doesNotMatch(html, /<Widget>/);
});
