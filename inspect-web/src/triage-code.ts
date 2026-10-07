import { buildLines, validateDocument } from "./document-model.ts";
import type { BrowserMemberSourceResult } from "./facades/inspect-web-source.d.ts";

export interface TriageCodeTarget {
  assembly: string;
  typeId: string;
  memberName: string;
  selector: string;
  methodToken: number;
  issueOffsets?: readonly number[] | null;
}

export function triageMemberLabel(typeId: string, memberName: string): string {
  const generic = typeId.indexOf("<");
  const head = generic < 0 ? typeId : typeId.slice(0, generic);
  const tail = generic < 0 ? "" : typeId.slice(generic);
  const short = head.slice(head.lastIndexOf(".") + 1).replaceAll("+", ".") + tail;
  return short ? `${short}.${memberName}` : memberName;
}

export function renderTriageCode(target: TriageCodeTarget, escape: (value: string) => string, memberLabel?: string): string {
  return `<details class="triage-code" data-triage-code data-triage-assembly="${escape(target.assembly)}" data-triage-type="${escape(target.typeId)}" data-triage-member="${escape(target.memberName)}" data-triage-selector="${escape(target.selector)}" data-triage-token="${target.methodToken}" data-triage-offsets="${(target.issueOffsets ?? []).join(",")}"><summary>Code${memberLabel ? ` · ${escape(memberLabel)}` : ""}</summary><pre><code>Expand to inspect the affected code.</code></pre></details>`;
}

/** Returns one line only when all issue offsets have a unique nearest C# location. */
export function triageIssueLine(document: unknown, offsets: readonly number[]): string | null {
  validateDocument(document);
  if (offsets.length === 0) return null;
  const lines = buildLines(document.text);
  let selectedLine: number | null = null;
  for (const offset of offsets) {
    const matches = document.nodes.filter(node => node.medium === "CSharp"
      && node.provenance?.il_offsets.includes(offset) && node.spans.length > 0);
    if (matches.length === 0) return null;
    const width = (node: (typeof matches)[number]) => node.spans.reduce((sum, span) => sum + span.length, 0);
    const nearestWidth = Math.min(...matches.map(width));
    const touched = new Set<number>();
    for (const node of matches.filter(candidate => width(candidate) === nearestWidth)) {
      for (const span of node.spans) {
        if (span.length === 0) return null;
        const first = lines.findIndex(line => span.start >= line.start && span.start <= line.end);
        const last = lines.findIndex(line => span.start + span.length - 1 >= line.start
          && span.start + span.length - 1 <= line.end);
        if (first < 0 || first !== last) return null;
        touched.add(first);
      }
    }
    if (touched.size !== 1) return null;
    const line = [...touched][0]!;
    if (selectedLine !== null && selectedLine !== line) return null;
    selectedLine = line;
  }
  return selectedLine === null ? null : lines[selectedLine]!.text.trim();
}

export type TriageCodeResult = BrowserMemberSourceResult | { kind: "line"; text: string };

export function bindTriageCode(
  root: ParentNode,
  load: (target: TriageCodeTarget) => Promise<TriageCodeResult>,
): void {
  root.querySelectorAll<HTMLDetailsElement>("[data-triage-code]").forEach(details => {
    let pending = false;
    let loaded = false;
    const loadCode = async () => {
      if (!details.open || pending || loaded) return;
      const code = details.querySelector("code");
      if (!code) return;
      pending = true;
      code.textContent = "Decompiling…";
      try {
        const offsets = (details.dataset.triageOffsets ?? "").split(",")
          .filter(Boolean).map(Number);
        const result = await load({
          assembly: details.dataset.triageAssembly ?? "",
          typeId: details.dataset.triageType ?? "",
          memberName: details.dataset.triageMember ?? "",
          selector: details.dataset.triageSelector ?? "",
          methodToken: Number(details.dataset.triageToken),
          ...(offsets.length ? { issueOffsets: offsets } : {}),
        });
        if (!details.isConnected) return;
        if ("kind" in result) {
          code.textContent = result.text;
          loaded = true;
          return;
        }
        const source = result.value;
        const memberSpans = source?.parts.filter(part => part.kind === "Member")
          .flatMap(part => part.spans) ?? [];
        code.textContent = source
          ? memberSpans.length
            ? memberSpans.map(span => source.source.text.slice(span.start, span.start + span.length)).join("\n\n")
            : source.source.text
          : result.error ?? "Decompiled source unavailable.";
        loaded = Boolean(result.value);
      } catch (error) {
        if (details.isConnected) code.textContent = error instanceof Error ? error.message : "Decompilation failed.";
      } finally {
        pending = false;
      }
    };
    details.addEventListener("toggle", () => { void loadCode(); });
  });
}
