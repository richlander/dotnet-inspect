import {
  isRoutedEntryPath,
  ROUTED_ENTRY_PATHS,
} from "./entry-routes.ts";
import { renderBrand } from "./brand.ts";

export function isCreditsPath(pathname: string): boolean {
  return isRoutedEntryPath(pathname, ROUTED_ENTRY_PATHS.credits);
}

export interface CreditsPanelBindingActions {
  onClose: () => void;
  onToggleTheme: () => "light" | "dark";
}

export function bindCreditsPanel(
  root: ParentNode,
  actions: CreditsPanelBindingActions,
) {
  root.querySelector("#credits-close")
    ?.addEventListener("click", actions.onClose);
  const theme = root.querySelector("#credits-theme");
  theme?.addEventListener("click", () => {
    const activeTheme = actions.onToggleTheme();
    theme.textContent = activeTheme === "dark" ? "light" : "dark";
  });
}

export function renderCreditsPage(theme: "dark" | "light"): string {
  return `
    <div class="credits-page">
      <header class="credits-bar">
        ${renderBrand()}
        <div class="credits-bar-actions">
          <button id="credits-theme" type="button" aria-label="Switch theme">${theme === "dark" ? "light" : "dark"}</button>
          <button id="credits-close" class="credits-close" type="button">back to home ✕</button>
        </div>
      </header>
      <main class="credits-main">
        <div class="credits-head">
          <p class="credits-kicker">Built on an open stack</p>
          <h1>Credits</h1>
          <p class="credits-lede">dotnet-inspect brings the .NET inspection stack into your browser with open-source runtimes, tools, and libraries.</p>
        </div>

        <section class="credits-section">
          <h2>Core platform</h2>
          <div class="credits-grid">
            <a href="https://dotnet.microsoft.com/" target="_blank" rel="noopener noreferrer"><strong>.NET 11</strong><span>Browser-hosted inspection engine</span></a>
            <a href="https://webassembly.org/" target="_blank" rel="noopener noreferrer"><strong>WebAssembly</strong><span>Runs .NET locally in the browser</span></a>
            <a href="https://www.typescriptlang.org/" target="_blank" rel="noopener noreferrer"><strong>TypeScript 7</strong><span>Typed browser application</span></a>
            <a href="https://www.nuget.org/" target="_blank" rel="noopener noreferrer"><strong>NuGet</strong><span>Package catalog and acquisition</span></a>
            <a href="https://learn.microsoft.com/dotnet/api/system.reflection.metadata" target="_blank" rel="noopener noreferrer"><strong>System.Reflection.Metadata</strong><span>Assembly inspection without loading user code</span></a>
          </div>
        </section>

        <section class="credits-section">
          <h2>Experience</h2>
          <div class="credits-links">
            <a href="https://vite.dev/" target="_blank" rel="noopener noreferrer">Vite</a>
            <a href="https://mermaid.js.org/" target="_blank" rel="noopener noreferrer">Mermaid</a>
            <a href="https://prismjs.com/" target="_blank" rel="noopener noreferrer">Prism.js</a>
            <a href="https://marked.js.org/" target="_blank" rel="noopener noreferrer">Marked</a>
            <a href="https://github.com/cure53/DOMPurify" target="_blank" rel="noopener noreferrer">DOMPurify</a>
          </div>
        </section>

        <section class="credits-section">
          <h2>Hosting</h2>
          <p>Hosted on <a href="https://azure.microsoft.com/products/app-service/static" target="_blank" rel="noopener noreferrer">Azure Static Web Apps</a>, with <a href="https://azure.microsoft.com/products/functions" target="_blank" rel="noopener noreferrer">Azure Functions</a> supporting symbol acquisition.</p>
        </section>

        <p class="credits-source">dotnet-inspect is <a href="https://github.com/richlander/dotnet-inspect" target="_blank" rel="noopener noreferrer">open source on GitHub</a>.</p>
      </main>
    </div>`;
}
