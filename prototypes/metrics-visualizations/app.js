import * as d3 from "https://cdn.jsdelivr.net/npm/d3@7/+esm";
import {
  sankey,
  sankeyLinkHorizontal,
} from "https://cdn.jsdelivr.net/npm/d3-sankey@0.12.3/+esm";

const [analysis, research] = await Promise.all([
  fetch("./data/analysis.json").then(response => response.json()),
  fetch("./data/research.json").then(response => response.json()),
]);

const metricLabels = {
  instructionCount: "implementation size",
  complexity: "branching",
  loops: "loops",
  calls: "direct calls",
  allocations: "allocations",
};

const shortName = value => value.split(".").at(-1);
const format = d3.format(",");

function renderTreemap(data) {
  const svg = d3.select("#map-chart");
  const detail = d3.select("#map-detail");
  const breadcrumb = d3.select("#map-breadcrumb");
  const reset = d3.select("#map-reset");
  const width = 900;
  const height = 410;
  svg.attr("viewBox", `0 0 ${width} ${height}`);
  svg.selectAll("*").remove();

  const groups = d3.rollups(
    data.profiles,
    values => ({
      instructions: d3.sum(values, value => value.instructionCount),
      complexity: d3.mean(values, value => value.complexity),
      bodies: values.length,
    }),
    value => value.namespace || "(global)",
    value => value.type,
  );
  const root = d3.hierarchy({
    name: data.assembly,
    children: groups.map(([namespace, types]) => ({
      name: namespace,
      children: types.map(([type, values]) => ({
        name: type,
        ...values,
      })),
    })),
  }).sum(node => node.instructions || 0).sort((a, b) => b.value - a.value);
  const maxDensity = d3.max(root.leaves(), leaf => leaf.data.complexity) || 1;
  const palette = d3.scaleSequential(d3.interpolateYlOrRd)
    .domain([0, Math.log1p(maxDensity)]);
  d3.treemap().size([width, height]).paddingOuter(6).paddingTop(20).paddingInner(2)(root);

  const cells = svg.selectAll("g").data(root.leaves()).join("g")
    .attr("class", "treemap-cell")
    .on("click", (_, node) => zoomTo(node.parent));
  cells.append("rect")
    .attr("x", node => node.x0).attr("y", node => node.y0)
    .attr("width", node => Math.max(0, node.x1 - node.x0))
    .attr("height", node => Math.max(0, node.y1 - node.y0))
    .attr("fill", node => palette(Math.log1p(node.data.complexity || 0)));
  cells.append("text").attr("x", node => node.x0 + 7).attr("y", node => node.y0 + 15)
    .text(node => node.x1 - node.x0 > 78 ? shortName(node.data.name) : "");
  cells.append("text").attr("class", "sub-label").attr("x", node => node.x0 + 7).attr("y", node => node.y0 + 29)
    .text(node => node.x1 - node.x0 > 100 && node.y1 - node.y0 > 42 ? `${format(node.data.bodies)} bodies` : "");

  const zoom = d3.zoom().scaleExtent([1, 12]).on("zoom", event => {
    cells.attr("transform", event.transform);
  });
  svg.call(zoom).on("dblclick.zoom", null);

  function zoomTo(target) {
    const focus = target?.depth === 1 ? target : root;
    const x = d3.scaleLinear().domain([focus.x0, focus.x1]).range([0, width]);
    const y = d3.scaleLinear().domain([focus.y0, focus.y1]).range([0, height]);
    cells.transition().duration(450)
      .attr("transform", node => `translate(${x(node.x0) - node.x0},${y(node.y0) - node.y0})`)
      .select("rect").attr("x", node => x(node.x0)).attr("y", node => y(node.y0))
      .attr("width", node => Math.max(0, x(node.x1) - x(node.x0)))
      .attr("height", node => Math.max(0, y(node.y1) - y(node.y0)));
    cells.select("text").transition().duration(450)
      .attr("x", node => x(node.x0) + 7).attr("y", node => y(node.y0) + 15);
    breadcrumb.text(focus === root ? data.assembly : `${data.assembly} / ${focus.data.name}`);
    detail.html(focus === root
      ? `<strong>${format(data.bodyCount)}</strong> physical bodies across <strong>${root.children.length}</strong> namespaces. Click a region, or scroll and drag to explore smaller shapes.`
      : `<strong>${focus.data.name}</strong> contains <strong>${format(focus.leaves().length)}</strong> method bodies. Branching density is shown by color, not as a severity score.`);
  }

  reset.on("click", () => {
    svg.transition().duration(300).call(zoom.transform, d3.zoomIdentity);
    zoomTo(root);
  });
  detail.html(`<strong>${format(data.bodyCount)}</strong> physical bodies across <strong>${root.children.length}</strong> namespaces. Area shows implementation volume; color shows average branching density. Scroll or pinch to zoom; drag to pan.`);
}

function relationshipGraph(data, limit = 18) {
  const raw = data.calls.filter(call => call.source !== call.target);
  const weights = new Map();
  for (const call of raw) {
    weights.set(call.source, (weights.get(call.source) || 0) + call.value);
    weights.set(call.target, (weights.get(call.target) || 0) + call.value);
  }
  const names = [...weights.entries()].sort((a, b) => b[1] - a[1])
    .slice(0, limit).map(([name]) => name);
  const allowed = new Set(names);
  const links = raw.filter(call => allowed.has(call.source) && allowed.has(call.target));
  return {
    nodes: names.map(name => ({ name, short: shortName(name) })),
    links: links.map(link => ({ ...link })),
  };
}

function renderRelationships(data, view = "sankey") {
  const svg = d3.select("#flow-chart");
  const detail = d3.select("#flow-detail");
  const summary = d3.select("#flow-summary");
  const label = d3.select("#flow-label");
  const width = 900;
  const height = 410;
  svg.attr("viewBox", `0 0 ${width} ${height}`).selectAll("*").remove();
  const graph = relationshipGraph(data);
  summary.html(`<strong>${format(data.callCount)}</strong> direct call sites in the assembly. This view shows the strongest <strong>${graph.nodes.length}</strong> type-to-type relationships so the shape remains readable.`);
  detail.text("Hover a relationship to see its weight.");

  if (view === "sankey") {
    label.text("Sankey · type-to-type calls");
    const nodes = graph.nodes.map(node => ({ ...node }));
    const links = graph.links
      .filter(link => link.source < link.target)
      .map(link => ({ source: link.source, target: link.target, value: link.value }));
    const index = new Map(nodes.map((node, i) => [node.name, i]));
    const layout = sankey().nodeId(node => node.name).nodeWidth(12)
      .nodePadding(12).extent([[18, 18], [width - 18, height - 22]]);
    const flow = layout({
      nodes,
      links: links.map(link => ({ ...link })),
    });
    svg.append("g").selectAll("path").data(flow.links).join("path")
      .attr("class", "flow-link").attr("d", sankeyLinkHorizontal())
      .attr("stroke-width", link => Math.max(1, link.width))
      .on("mouseenter", (_, link) => detail.html(`<strong>${shortName(link.source.name)}</strong> calls <strong>${shortName(link.target.name)}</strong> · ${format(link.value)} sites`));
    const node = svg.append("g").selectAll("g").data(flow.nodes).join("g").attr("class", "flow-node");
    node.append("rect").attr("x", d => d.x0).attr("y", d => d.y0)
      .attr("width", d => d.x1 - d.x0).attr("height", d => Math.max(2, d.y1 - d.y0));
    node.append("text").attr("x", d => d.x0 < width / 2 ? d.x1 + 5 : d.x0 - 5)
      .attr("y", d => (d.y0 + d.y1) / 2).attr("text-anchor", d => d.x0 < width / 2 ? "start" : "end")
      .text(d => d.short);
    void index;
  } else if (view === "arc") {
    label.text("Arc diagram · crossing relationships");
    const nodes = graph.nodes;
    const x = d3.scalePoint().domain(nodes.map(node => node.name)).range([36, width - 36]);
    const baseline = height - 70;
    svg.append("line").attr("class", "compare-row").attr("x1", 24).attr("x2", width - 24).attr("y1", baseline).attr("y2", baseline);
    svg.append("g").selectAll("path").data(graph.links).join("path")
      .attr("class", "arc-link")
      .attr("d", link => {
        const x1 = x(link.source);
        const x2 = x(link.target);
        const radius = Math.abs(x2 - x1) / 2;
        return `M${x1},${baseline} A${radius},${radius} 0 0,${x1 < x2 ? 1 : 0} ${x2},${baseline}`;
      })
      .attr("stroke-width", link => Math.max(1, Math.log1p(link.value)))
      .on("mouseenter", (_, link) => detail.html(`<strong>${shortName(link.source)}</strong> → <strong>${shortName(link.target)}</strong> · ${format(link.value)} sites`));
    svg.append("g").selectAll("circle").data(nodes).join("circle")
      .attr("class", "arc-node").attr("cx", node => x(node.name)).attr("cy", baseline).attr("r", 5);
    svg.append("g").selectAll("text").data(nodes).join("text").attr("class", "flow-label")
      .attr("transform", node => `translate(${x(node.name)},${baseline + 18}) rotate(55)`)
      .text(node => node.short);
  } else {
    label.text("Chord diagram · mutual relationships");
    const names = graph.nodes.slice(0, 14).map(node => node.name);
    const index = new Map(names.map((name, i) => [name, i]));
    const matrix = names.map(() => names.map(() => 0));
    for (const link of graph.links) {
      if (index.has(link.source) && index.has(link.target)) {
        matrix[index.get(link.source)][index.get(link.target)] += link.value;
      }
    }
    const radius = Math.min(width, height) * .34;
    const center = [width / 2, height / 2 + 5];
    const chord = d3.chord().padAngle(.04).sortSubgroups(d3.descending)(matrix);
    const arc = d3.arc().innerRadius(radius).outerRadius(radius + 10);
    const ribbon = d3.ribbon().radius(radius);
    const group = svg.append("g").attr("transform", `translate(${center})`);
    group.append("g").selectAll("path").data(chord.groups).join("path")
      .attr("class", "chord-node").attr("d", arc)
      .on("mouseenter", (_, groupDatum) => detail.text(`${shortName(names[groupDatum.index])} participates in ${format(matrix[groupDatum.index].reduce((a, b) => a + b, 0))} directed sites`));
    group.append("g").selectAll("path").data(chord).join("path")
      .attr("class", "chord-link").attr("d", ribbon)
      .on("mouseenter", (_, link) => detail.html(`<strong>${shortName(names[link.source.index])}</strong> ↔ <strong>${shortName(names[link.target.index])}</strong>`));
    group.append("g").selectAll("text").data(chord.groups).join("text")
      .attr("class", "flow-label").attr("transform", groupDatum => {
        const angle = (groupDatum.startAngle + groupDatum.endAngle) / 2;
        return `rotate(${angle * 180 / Math.PI - 90}) translate(${radius + 17}) ${angle > Math.PI ? "rotate(180)" : ""}`;
      }).attr("text-anchor", groupDatum => (groupDatum.startAngle + groupDatum.endAngle) / 2 > Math.PI ? "end" : "start")
      .text(groupDatum => shortName(names[groupDatum.index]));
  }
}

function compositionHierarchy(data, metric) {
  const groups = d3.rollups(
    data.profiles,
    values => ({
      value: d3.sum(values, profile => profile[metric]),
      bodies: values.length,
    }),
    profile => profile.namespace || "(global)",
    profile => profile.type,
  );
  return {
    name: data.assembly,
    children: groups.map(([namespace, types]) => ({
      name: namespace,
      children: types.map(([type, values]) => ({ name: type, ...values })),
    })),
  };
}

function renderComposition(data, metric = "instructionCount") {
  const radial = d3.select("#radial-chart");
  const bands = d3.select("#band-chart");
  const detail = d3.select("#composition-detail");
  const summary = d3.select("#composition-summary");
  const label = d3.select("#composition-label");
  const width = 520;
  const height = 390;
  radial.attr("viewBox", `0 0 ${width} ${height}`).selectAll("*").remove();
  bands.attr("viewBox", `0 0 380 ${height}`).selectAll("*").remove();
  label.text(`Radial map · namespace → type · ${metricLabels[metric]}`);
  const hierarchy = d3.hierarchy(compositionHierarchy(data, metric)).sum(node => node.value || 0).sort((a, b) => b.value - a.value);
  const radius = Math.min(width, height) / 2 - 25;
  d3.partition().size([2 * Math.PI, radius])(hierarchy);
  const arc = d3.arc().startAngle(node => node.x0).endAngle(node => node.x1)
    .innerRadius(node => node.y0).outerRadius(node => node.y1 - 1);
  const color = d3.scaleOrdinal(d3.schemeTableau10);
  radial.append("g").attr("transform", `translate(${width / 2},${height / 2})`)
    .selectAll("path").data(hierarchy.descendants().filter(node => node.depth > 0)).join("path")
    .attr("class", "radial-node").attr("d", arc)
    .attr("fill", node => color(node.ancestors().at(-1).data.name))
    .attr("fill-opacity", node => node.depth === 1 ? .85 : .5)
    .on("mouseenter", (_, node) => detail.html(`<strong>${node.data.name}</strong> · ${format(node.data.bodies || node.leaves().reduce((sum, leaf) => sum + leaf.data.bodies, 0))} bodies · ${format(node.value)} ${metricLabels[metric]}`));
  const values = data.profiles.map(profile => profile[metric]).sort(d3.ascending);
  const p50 = d3.quantileSorted(values, .5) || 0;
  const p90 = d3.quantileSorted(values, .9) || 0;
  const p99 = d3.quantileSorted(values, .99) || 0;
  const bucket = value => value <= p50 ? "small" : value <= p90 ? "typical" : value <= p99 ? "large" : "very large";
  const bucketValues = d3.rollup(data.profiles, values => d3.sum(values, profile => profile[metric]), profile => bucket(profile[metric]));
  const ordered = ["small", "typical", "large", "very large"].map(name => ({ name, value: bucketValues.get(name) || 0 }));
  const pie = d3.pie().value(item => item.value)(ordered);
  const bandArc = d3.arc().innerRadius(70).outerRadius(138);
  bands.append("g").attr("transform", "translate(190,165)").selectAll("path").data(pie).join("path")
    .attr("class", "band-arc").attr("fill", (_, i) => ["#315e83", "#4c8caa", "#d49d4d", "#d75d55"][i]).attr("d", bandArc)
    .on("mouseenter", (_, item) => detail.html(`<strong>${item.data.name}</strong> bodies contribute ${format(item.data.value)} total ${metricLabels[metric]}`));
  bands.append("g").selectAll("text").data(pie).join("text").attr("class", "band-label")
    .attr("x", (_, i) => 26).attr("y", (_, i) => 270 + i * 18).text(item => `${item.data.name}: ${format(item.data.value)}`);
  summary.html(`<strong>${format(data.bodyCount)}</strong> bodies · ${metricLabels[metric]} bands use the middle, upper tail, and largest values as landmarks.`);
  detail.text("Hover a radial segment or band to identify what it represents.");
}

function groupedShare(data, scope) {
  const values = d3.rollup(
    data.profiles,
    profiles => d3.sum(profiles, profile => profile.instructionCount),
    profile => scope === "namespace" ? profile.namespace || "(global)" : profile.type,
  );
  const total = d3.sum(values.values());
  return new Map([...values].map(([name, value]) => [name, value / total]));
}

function renderDiverging(left, right, scope = "namespace") {
  const svg = d3.select("#compare-chart");
  const detail = d3.select("#compare-detail");
  const label = d3.select("#compare-label");
  const width = 900;
  const height = 390;
  const margin = { top: 24, right: 24, bottom: 30, left: 190 };
  svg.attr("viewBox", `0 0 ${width} ${height}`).selectAll("*").remove();
  let rows;
  if (scope === "metric") {
    const metrics = ["instructionCount", "complexity", "loops", "calls", "allocations"];
    const p95 = data => Object.fromEntries(metrics.map(metric => {
      const values = data.profiles.map(profile => profile[metric]).sort(d3.ascending);
      return [metric, d3.quantileSorted(values, .95) || 0];
    }));
    const leftValues = p95(left);
    const rightValues = p95(right);
    rows = metrics.map(metric => {
      const scale = Math.max(leftValues[metric], rightValues[metric], 1);
      return {
        name: metricLabels[metric],
        left: leftValues[metric],
        right: rightValues[metric],
        difference: (leftValues[metric] - rightValues[metric]) / scale,
      };
    });
    label.text("Diverging upper-tail measures · P95");
  } else {
    const leftShares = groupedShare(left, scope);
    const rightShares = groupedShare(right, scope);
    const names = [...new Set([...leftShares.keys(), ...rightShares.keys()])];
    rows = names.map(name => ({
      name,
      left: leftShares.get(name) || 0,
      right: rightShares.get(name) || 0,
      difference: (leftShares.get(name) || 0) - (rightShares.get(name) || 0),
    })).sort((a, b) => Math.abs(b.difference) - Math.abs(a.difference)).slice(0, 12);
    label.text(`Diverging implementation share · ${scope}`);
  }
  const domain = scope === "metric" ? 1.15 : d3.max(rows, row => Math.abs(row.difference)) * 1.15;
  const x = d3.scaleLinear().domain([-domain, domain]).range([margin.left, width - margin.right]);
  const y = d3.scaleBand().domain(rows.map(row => row.name)).range([margin.top, height - margin.bottom]).padding(.28);
  svg.append("line").attr("class", "compare-zero").attr("x1", x(0)).attr("x2", x(0)).attr("y1", margin.top - 8).attr("y2", height - margin.bottom + 4);
  svg.append("g").selectAll("rect").data(rows).join("rect")
    .attr("class", row => row.difference >= 0 ? "compare-bar-analysis" : "compare-bar-research")
    .attr("x", row => x(Math.min(0, row.difference))).attr("y", row => y(row.name))
    .attr("width", row => Math.abs(x(row.difference) - x(0))).attr("height", y.bandwidth())
    .on("mouseenter", (_, row) => detail.html(scope === "metric"
      ? `<strong>${row.name}</strong> · Analysis P95 ${format(row.left)} · Research P95 ${format(row.right)}`
      : `<strong>${row.name}</strong> · Analysis ${(row.left * 100).toFixed(1)}% · Research ${(row.right * 100).toFixed(1)}% of implementation volume`));
  svg.append("g").selectAll("text").data(rows).join("text").attr("class", "compare-name")
    .attr("x", margin.left - 10).attr("y", row => y(row.name) + y.bandwidth() / 2 + 4).attr("text-anchor", "end")
    .text(row => shortName(row.name));
  svg.append("g").selectAll("text").data([-1, 0, 1]).join("text").attr("class", "compare-axis")
    .attr("x", value => x(value * domain)).attr("y", height - 5)
    .attr("text-anchor", "middle").text(value => scope === "metric"
      ? `${value > 0 ? "+" : ""}${value * 100}%`
      : `${value > 0 ? "+" : ""}${value} share`);
  detail.text(scope === "metric"
    ? "Hover a bar to compare the upper-tail value across the two libraries."
    : "Hover a bar to compare each group's share of implementation volume.");
}

renderTreemap(analysis);
renderRelationships(analysis);
renderComposition(analysis);
renderDiverging(analysis, research, "metric");
d3.selectAll("[data-flow-view]").on("click", event => {
  d3.selectAll("[data-flow-view]").classed("is-selected", false);
  d3.select(event.currentTarget).classed("is-selected", true);
  renderRelationships(analysis, event.currentTarget.dataset.flowView);
});
d3.select("#composition-metric").on("change", event => renderComposition(analysis, event.target.value));
d3.select("#compare-scope").on("change", event => renderDiverging(analysis, research, event.target.value));
