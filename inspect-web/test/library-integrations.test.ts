import assert from "node:assert/strict";
import { test } from "node:test";
import {
  renderLibraryIntegrationsSurface,
  type LibraryIntegrationsOptions,
} from "../src/library-integrations.ts";
import type {
  BrowserPackageIntegrations,
  BrowserPackageOpportunities,
} from "../src/facades/inspect-web-analysis.d.ts";

const integrations: BrowserPackageIntegrations = {
  package: "Example.Package",
  version: "1.0.0",
  framework: "net10.0",
  categories: [
    {
      integration: "Dependency Injection",
      signals: [
        {
          name: "Example.Extensions.ZAdd()",
          shape: "Method",
          kind: "Extension method",
        },
        {
          name: "Example.Service",
          shape: "Type",
          kind: "Implementation",
        },
        {
          name: "Example.Extensions.Add()",
          shape: "Method",
          kind: "Extension method",
        },
      ],
    },
    {
      integration: "Logging",
      signals: [{
        name: "Example.Logger.Write(ILogger logger)",
        shape: "Method",
        kind: "Parameter",
      }],
    },
  ],
  totalSignals: 4,
  isComplete: true,
  inspectionError: null,
  compileLibrary: {
    status: "Selected",
    targetFramework: "net10.0",
    message: null,
  },
  inspection: null,
};

const suggestions: BrowserPackageOpportunities = {
  package: "Example.Package",
  version: "1.0.0",
  activeFramework: "net10.0",
  categories: [
    {
      integration: "Dependency Injection",
      items: [{
        api: "Example.Widget",
        integrationType:
          "Microsoft.Extensions.DependencyInjection IServiceCollection registration",
        lookFor: "AddWidget",
        sourceDefinitionId: "Example.Widget",
        sourceAssembly: "Example.Core",
        sourceAssemblyVersion: "1.0.0.0",
        sourceAssemblyCulture: null,
        sourceAssemblyPublicKeyToken: null,
      }],
    },
    {
      integration: "AI",
      items: [{
        api: "Example.ChatWidget",
        integrationType: "Microsoft.Extensions.AI IChatClient extension",
        lookFor: "AddChatClient",
        sourceDefinitionId: "Example.ChatWidget",
        sourceAssembly: "Example.Core",
        sourceAssemblyVersion: "1.0.0.0",
        sourceAssemblyCulture: null,
        sourceAssemblyPublicKeyToken: null,
      }],
    },
  ],
  totalOpportunities: 2,
  isComplete: true,
  inspectionError: null,
  compileLibrary: {
    status: "Selected",
    targetFramework: "net10.0",
    message: null,
  },
  inspection: null,
};

function render(overrides: Partial<LibraryIntegrationsOptions> = {}) {
  return renderLibraryIntegrationsSurface({
    libraryName: "Example.Core",
    assemblyIdentity:
      "Example.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    assetPath: "lib/net10.0/Example.Core.dll",
    coordinate: "net10.0 / Example.Package@1.0.0",
    requireLibrary: false,
    pickerHtml: "",
    integrationsFresh: true,
    integrationsLoading: false,
    integrationsError: "",
    integrationsData: integrations,
    suggestionsFresh: true,
    suggestionsLoading: false,
    suggestionsError: "",
    suggestionsData: suggestions,
    escapeHtml: value => String(value).replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;").replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;").replaceAll("'", "&#39;"),
    ...overrides,
  });
}

test("Integrations uses one Analysis tab and merges detected and suggested entries", () => {
  const html = render();
  assert.equal(html.match(/<h1\b/g)?.length, 1);
  assert.match(html, /<h1 id="library-analysis-title">Analysis<\/h1>/);
  assert.match(
    html,
    /data-analysis-mode="integrations" aria-selected="true"/,
  );
  assert.doesNotMatch(html, /data-analysis-mode="opportunities"/);
  assert.match(html, /3 categories.*4 detected.*2 suggested/);
  assert.match(
    html,
    /<h2 id="integration-category-0">Dependency Injection<\/h2><span>1 type &middot; 2 APIs &middot; 1 suggested<\/span>/,
  );
  assert.equal(
    html.match(/aria-labelledby="integration-category-/g)?.length,
    3,
  );
  assert.equal(html.match(/class="signal-row"/g)?.length, 4);
  assert.equal(html.match(/class="opp-row"/g)?.length, 2);
  assert.match(
    html,
    /<footer[\s\S]*lib\/net10.0\/Example.Core.dll.*Example.Core, Version=1.0.0.0/,
  );
  assert.match(
    html,
    /<footer[\s\S]*net10.0 \/ Example.Package@1.0.0/,
  );
});

test("detected rows retain category order and type-first sorting without mutating data", () => {
  const original = JSON.stringify(integrations);
  const html = render();
  const names = [...html.matchAll(/class="signal-name">([^<]*)<\/span>/g)]
    .map(match => match[1]);
  assert.deepEqual(
    names,
    ["Service", "Add()", "ZAdd()", "Write(ILogger logger)"],
  );
  assert.ok(
    html.indexOf(">Dependency Injection</h2>")
      < html.indexOf(">Logging</h2>"),
  );
  assert.ok(html.indexOf(">Logging</h2>") < html.indexOf(">AI</h2>"));
  assert.equal(JSON.stringify(integrations), original);
});

test("either evidence source can populate the shared list independently", () => {
  const detectedOnly = render({
    suggestionsFresh: true,
    suggestionsData: {
      ...suggestions,
      categories: [],
      totalOpportunities: 0,
    },
  });
  const suggestedOnly = render({
    integrationsFresh: true,
    integrationsData: {
      ...integrations,
      categories: [],
      totalSignals: 0,
    },
  });

  assert.equal(detectedOnly.match(/class="signal-row"/g)?.length, 4);
  assert.doesNotMatch(detectedOnly, /class="opp-row"/);
  assert.match(detectedOnly, /4 detected.*0 suggested/);
  assert.equal(suggestedOnly.match(/class="opp-row"/g)?.length, 2);
  assert.doesNotMatch(suggestedOnly, /class="signal-row"/);
  assert.match(suggestedOnly, /0 detected.*2 suggested/);
});

test("one source can fail without hiding results from the other", () => {
  const html = render({
    suggestionsData: null,
    suggestionsError: "Suggestion query unavailable.",
  });

  assert.equal(html.match(/class="signal-row"/g)?.length, 4);
  assert.doesNotMatch(html, /class="opp-row"/);
  assert.match(html, /4 detected.*0 suggested.*partial/);
  assert.match(html, /Suggested integrations: Suggestion query unavailable/);
});

test("a scan still running shows available rows and a visible pending state", () => {
  const html = render({
    suggestionsData: null,
    suggestionsLoading: true,
  });

  assert.equal(html.match(/class="signal-row"/g)?.length, 4);
  assert.match(html, /4 detected.*0 suggested.*scanning/);
  assert.match(html, /The integration scan is still running/);
});

test("a failed source remains visible while the other source is still scanning", () => {
  const html = render({
    integrationsData: null,
    integrationsError: "Detected query unavailable.",
    suggestionsData: null,
    suggestionsLoading: true,
  });

  assert.match(html, /Scanning integrations/);
  assert.match(html, /Part of the integration scan failed/);
  assert.match(html, /Detected integrations: Detected query unavailable/);
});

test("platform selection stays outside the shared scroller", () => {
  const pickerHtml =
    '<select class="scope-select platform-library-select" data-platform-analysis-library aria-label="Select a platform library"><option>Example.Core</option></select>';
  const html = render({
    requireLibrary: true,
    pickerHtml,
    integrationsLoading: true,
    suggestionsLoading: true,
  });

  assert.match(html, /library-integrations-with-controls/);
  assert.match(
    html,
    /library-integrations-controls[\s\S]*data-platform-analysis-library[\s\S]*library-integrations-scroll/,
  );
  assert.match(html, /Pick a library to scan/);
  assert.doesNotMatch(html, /role="listitem"|4 detected|2 suggested/);
});

test("complete and incomplete empty scans remain distinct", () => {
  const emptyIntegrations = {
    ...integrations,
    categories: [],
    totalSignals: 0,
  };
  const emptySuggestions = {
    ...suggestions,
    categories: [],
    totalOpportunities: 0,
  };
  const complete = render({
    integrationsData: emptyIntegrations,
    suggestionsData: emptySuggestions,
  });
  const incomplete = render({
    integrationsData: {
      ...emptyIntegrations,
      isComplete: false,
      inspectionError: "Participant unavailable.",
    },
    suggestionsData: emptySuggestions,
  });

  assert.match(complete, /No ecosystem integrations found/);
  assert.doesNotMatch(complete, /partial|metadata-warning/);
  assert.match(incomplete, /Integration scan incomplete/);
  assert.match(incomplete, /Participant unavailable/);
  assert.doesNotMatch(incomplete, /No ecosystem integrations found/);
});

test("rendered facts, context, and errors use the supplied text escape boundary", () => {
  const html = render({
    assemblyIdentity: 'Example."Core"',
    assetPath: "lib/Core&Other.dll",
    coordinate: "Example<Package>@1",
    integrationsData: null,
    suggestionsData: null,
    integrationsError: "Read <failed>",
    suggestionsError: "Suggest <failed>",
  });

  assert.match(
    html,
    /title="lib\/Core&amp;Other.dll.*Example.&quot;Core&quot;"/,
  );
  assert.match(html, /Example&lt;Package&gt;/);
  assert.match(html, /Detected integrations: Read &lt;failed&gt;/);
  assert.match(html, /Suggested integrations: Suggest &lt;failed&gt;/);
});
