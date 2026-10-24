import type { EngineClient } from "./engine-client.ts";
import type {
  BrowserResourceExplanationResult,
  BrowserWorkspaceComponentInput,
} from "./facades/inspect-web-catalog.d.ts";

export interface WorkspaceExplanationState {
  open: boolean;
  loading: boolean;
  error: string;
  packet: string | null;
  components: readonly BrowserWorkspaceComponentInput[];
  result: BrowserResourceExplanationResult | null;
}

export function createWorkspaceExplanation(dependencies: {
  catalog: Pick<EngineClient["catalog"],
    "explainResources" | "describeWorkspaceComponents" | "explainWorkspaceComponent">;
  capturePacket: () => Promise<string>;
  contextKey: () => unknown;
  changed: () => void;
}) {
  const empty = (): WorkspaceExplanationState => ({
    open: false, loading: false, error: "", packet: null, components: [], result: null,
  });
  let state = empty();
  let context = dependencies.contextKey();
  let generation = 0;

  function view(): WorkspaceExplanationState {
    const current = dependencies.contextKey();
    if (current !== context) {
      context = current;
      generation++;
      state = empty();
    }
    return state;
  }

  async function run(read: () => Promise<Partial<WorkspaceExplanationState>>) {
    view();
    const requestContext = context;
    const request = ++generation;
    state = { ...state, open: true, loading: true, error: "", result: null };
    dependencies.changed();
    const current = () => request === generation
      && requestContext === dependencies.contextKey();
    try {
      const result = await read();
      if (current()) state = { ...state, ...result, loading: false };
    } catch (error) {
      if (current()) state = { ...state, loading: false,
        error: error instanceof Error ? error.message : String(error) };
    } finally {
      if (current()) dependencies.changed();
    }
  }

  return {
    view,
    explore: (path = "root") => run(async () => ({
      result: await dependencies.catalog.explainResources(path, "hal"),
    })),
    declarations: () => run(async () => {
      const packet = await dependencies.capturePacket();
      const inventory = await dependencies.catalog.describeWorkspaceComponents(packet);
      if (inventory.outcome !== "Described")
        throw new Error(inventory.error ?? "Workspace declarations could not be read.");
      return { packet, components: inventory.components };
    }),
    explain: (path: string, withParent: boolean) => {
      const snapshot = view();
      const component = snapshot.components.find(item => item.path === path);
      return run(async () => {
        if (!component) throw new Error("Choose a declaration from this Workspace.");
        return { result: await dependencies.catalog.explainWorkspaceComponent(
          component.declaration, withParent ? snapshot.packet : null, "hal") };
      });
    },
    close: () => {
      generation++;
      state = empty();
      dependencies.changed();
    },
  };
}

type RecordValue = Record<string, unknown>;
function isRecord(value: unknown): value is RecordValue {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}
function record(value: unknown): RecordValue | null {
  return isRecord(value) ? value : null;
}

function links(content: unknown): { href: string; title: string }[] {
  const found = new Map<string, { href: string; title: string }>();
  const names = new Map<string, string>();
  function visit(value: unknown, depth: number) {
    const resource = record(value);
    if (!resource || depth > 8) return;
    const self = record(record(resource._links)?.self);
    const facts = record(record(record(resource._embedded)?.data)?.facts);
    if (typeof self?.href === "string" && typeof facts?.name === "string")
      names.set(self.href, facts.name);
    for (const [relation, targets] of Object.entries(record(resource._links) ?? {})) {
      for (const target of Array.isArray(targets) ? targets : [targets]) {
        const link = record(target);
        if (relation !== "self" && typeof link?.href === "string"
          && link.href.startsWith("inspect-resource:/")) {
          found.set(link.href, { href: link.href,
            title: typeof link.title === "string" ? link.title : relation });
        }
      }
    }
    for (const embedded of Object.values(record(resource._embedded) ?? {})) {
      if (Array.isArray(embedded)) embedded.forEach(item => visit(item, depth + 1));
      else visit(embedded, depth + 1);
    }
  }
  visit(content, 0);
  return [...found.values()].map(link => ({ ...link, title: names.get(link.href) ?? link.title }));
}

export function renderWorkspaceExplanation(
  state: WorkspaceExplanationState,
  available: boolean,
  escapeHtml: (value: unknown) => string,
): string {
  const disabled = !available || state.loading;
  const controls = `<button type="button" data-explanation-root${disabled ? " disabled" : ""}>Explore descriptions</button>
    <button type="button" data-explanation-declarations${disabled ? " disabled" : ""}>Explain declarations</button>`;
  if (!state.open) return `<section class="document-section workspace-section"><div class="section-title"><h2>Explain</h2></div>${controls}</section>`;
  const result = state.result;
  const explanation = result?.explanation;
  const error = state.error || result?.error || "";
  const content = record(explanation?.content);
  const data = record(record(content?._embedded)?.data);
  const facts = record(data?.facts) ?? {};
  const states = record(data?.fact_states) ?? {};
  const names = new Set([...Object.keys(facts), ...Object.keys(states)]);
  const rows = [...names].map(name => {
    const observation = record(states[name]);
    const value = name in facts ? facts[name] : observation?.outcome;
    return `<tr><th scope="row">${escapeHtml(name)}</th><td>${escapeHtml(observation?.state ?? "Available")}</td><td>${escapeHtml(typeof value === "string" ? value : JSON.stringify(value) ?? "")}</td></tr>`;
  }).join("");
  const navigation = links(explanation?.content).map(link =>
    `<li><button type="button" data-explanation-resource="${escapeHtml(link.href)}"${disabled ? " disabled" : ""}>${escapeHtml(link.title)}</button> <code>${escapeHtml(link.href)}</code></li>`).join("");
  const components = state.components.map(component =>
    `<li><strong>${escapeHtml(component.name)}</strong> <code>${escapeHtml(component.path)}</code>
      <button type="button" data-explanation-component="${escapeHtml(component.path)}"${disabled ? " disabled" : ""}>Explain declaration</button>
      <button type="button" data-explanation-parent="${escapeHtml(component.path)}"${disabled ? " disabled" : ""}>Explain in Workspace</button></li>`).join("");
  const diagnostics = explanation?.diagnostics.map(diagnostic =>
    `<li>${escapeHtml(diagnostic.severity)}: ${escapeHtml(diagnostic.summary)} (${escapeHtml(diagnostic.code)})</li>`).join("") ?? "";
  return `<section class="document-section workspace-section" aria-label="Explanation">
    <div class="section-title"><h2>Explain</h2><button type="button" data-explanation-close>Close</button></div>
    ${controls}
    ${state.loading ? '<p role="status">Reading description…</p>' : ""}
    ${error ? `<p role="alert">${escapeHtml(result?.outcome ?? "Unavailable")}: ${escapeHtml(error)}</p>` : ""}
    ${components ? `<h3>Package declarations</h3><ul>${components}</ul>` : ""}
    ${state.packet && !components ? "<p>No Package declarations are present in this packet.</p>" : ""}
    ${rows ? `<table><thead><tr><th>Field</th><th>State</th><th>Value</th></tr></thead><tbody>${rows}</tbody></table>` : ""}
    ${diagnostics ? `<ul role="status">${diagnostics}</ul>` : ""}
    ${navigation ? `<h3>Related descriptions</h3><ul>${navigation}</ul>` : ""}
    ${explanation ? `<details><summary>JSON</summary><pre>${escapeHtml(JSON.stringify(explanation, null, 2))}</pre></details>` : ""}
  </section>`;
}

export function bindWorkspaceExplanation(
  root: ParentNode,
  actions: ReturnType<typeof createWorkspaceExplanation>,
): void {
  root.querySelector("[data-explanation-root]")?.addEventListener("click",
    () => { void actions.explore(); });
  root.querySelector("[data-explanation-declarations]")?.addEventListener("click",
    () => { void actions.declarations(); });
  root.querySelector("[data-explanation-close]")?.addEventListener("click", actions.close);
  for (const attribute of ["resource", "component", "parent"] as const) {
    root.querySelectorAll<HTMLElement>(`[data-explanation-${attribute}]`).forEach(button =>
      button.addEventListener("click", () => {
        const value = button.getAttribute(`data-explanation-${attribute}`);
        if (value === null) return;
        if (attribute === "resource") void actions.explore(value);
        else void actions.explain(value, attribute === "parent");
      }));
  }
}
