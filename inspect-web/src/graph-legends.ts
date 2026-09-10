export function callGraphLegendHtml(): string {
  return `<div class="graph-legend" aria-label="Graph legend">
    <span><i class="legend-swatch target"></i>target member</span>
    <span><i class="legend-swatch same-type"></i>same declaring type</span>
    <span><i class="legend-swatch different-type"></i>different type, same assembly</span>
    <span><i class="legend-swatch different-assembly"></i>different assembly</span>
    <span><i class="legend-swatch loaded-node"></i>solid border: no platform lookup</span>
    <span><i class="legend-swatch platform-node"></i>dashed border: platform lookup on click</span>
  </div>`;
}

export function typeGraphLegendHtml(): string {
  return `<div class="graph-legend" aria-label="Graph legend">
    <span><i class="legend-swatch target"></i>inspected type</span>
    <span><i class="legend-swatch base-type"></i>base type</span>
    <span><i class="legend-swatch interface-type"></i>interface</span>
    <span><i class="legend-swatch derived-type"></i>derived type</span>
    <span><i class="legend-swatch unavailable-node"></i>dashed border: not in browsable surface</span>
  </div>`;
}

export function dependencyGraphLegendHtml(): string {
  return `<div class="graph-legend" aria-label="Graph legend">
    <span><i class="legend-swatch target"></i>inspected package</span>
    <span><i class="legend-swatch open-package"></i>open in workspace</span>
    <span><i class="legend-swatch external-package"></i>load on selection</span>
  </div>`;
}
