import * as d3 from "https://cdn.jsdelivr.net/npm/d3@7/+esm";

const metricDefinitions = {
  complexity: {
    label: "Normal-flow branching",
    fieldLabel: "Normal-flow cyclomatic complexity",
    format: d3.format(","),
  },
  instructionCount: {
    label: "Implementation size",
    fieldLabel: "Instruction count",
    format: d3.format(","),
  },
  loops: { label: "Loops", fieldLabel: "Loop count", format: d3.format(",") },
  calls: { label: "Direct calls", fieldLabel: "Direct call count", format: d3.format(",") },
  allocations: { label: "Allocations", fieldLabel: "Allocation count", format: d3.format(",") },
};

const [analysis, research] = await Promise.all([
  fetch("./data/analysis.json").then(response => response.json()),
  fetch("./data/research.json").then(response => response.json()),
]);

const shortName = value => value.split(".").at(-1);
const palette = d3.scaleSequential(d3.interpolateYlOrRd);

function profileMetric(profile, metric) {
  return profile[metric];
}

function renderTreemap(data) {
  const svg = d3.select("#map-chart");
  const detail = d3.select("#map-detail");
  const breadcrumb = d3.select("#map-breadcrumb");
  const reset = d3.select("#map-reset");
  const width = 900;
  const height = 410;
  svg.attr("viewBox", `0 0 ${width} ${height}`);

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
  palette.domain([0, Math.log1p(maxDensity)]);
  const treemap = d3.treemap().size([width, height]).paddingOuter(6).paddingTop(20).paddingInner(2);
  const rootLayout = treemap(root);
  const rootX = d3.scaleLinear().domain([0, width]).range([0, width]);
  const rootY = d3.scaleLinear().domain([0, height]).range([0, height]);

  const cells = svg.selectAll("g").data(rootLayout.leaves()).join("g")
    .attr("class", "treemap-cell")
    .on("click", (_, node) => zoom(node.parent));
  cells.append("rect")
    .attr("x", node => node.x0).attr("y", node => node.y0)
    .attr("width", node => Math.max(0, node.x1 - node.x0))
    .attr("height", node => Math.max(0, node.y1 - node.y0))
    .attr("fill", node => palette(Math.log1p(node.data.complexity || 0)));
  cells.append("text").attr("x", node => node.x0 + 7).attr("y", node => node.y0 + 15)
    .text(node => node.x1 - node.x0 > 78 ? shortName(node.data.name) : "");
  cells.append("text").attr("class", "sub-label").attr("x", node => node.x0 + 7).attr("y", node => node.y0 + 29)
    .text(node => node.x1 - node.x0 > 100 && node.y1 - node.y0 > 42 ? `${node.data.bodies} bodies` : "");

  function zoom(target) {
    const focus = target?.depth === 1 ? target : root;
    const x = d3.scaleLinear().domain([focus.x0, focus.x1]).range([0, width]);
    const y = d3.scaleLinear().domain([focus.y0, focus.y1]).range([0, height]);
    cells.transition().duration(500)
      .attr("transform", node => `translate(${x(node.x0) - node.x0},${y(node.y0) - node.y0})`)
      .select("rect").attr("x", node => x(node.x0)).attr("y", node => y(node.y0))
      .attr("width", node => Math.max(0, x(node.x1) - x(node.x0)))
      .attr("height", node => Math.max(0, y(node.y1) - y(node.y0)));
    cells.select("text").transition().duration(500)
      .attr("x", node => x(node.x0) + 7).attr("y", node => y(node.y0) + 15);
    breadcrumb.text(focus === root ? data.assembly : `${data.assembly} / ${focus.data.name}`);
    detail.html(focus === root
      ? `<strong>${data.bodyCount.toLocaleString()}</strong> physical bodies across <strong>${root.children.length}</strong> namespaces. Click a namespace's type regions to zoom into its implementation.`
      : `<strong>${focus.data.name}</strong> contains <strong>${focus.leaves().length.toLocaleString()}</strong> method bodies. Branching density is shown by color, not as a severity score.`);
  }
  reset.on("click", () => zoom(root));
  detail.html(`<strong>${data.bodyCount.toLocaleString()}</strong> physical bodies across <strong>${root.children.length}</strong> namespaces. Area shows implementation volume; color shows average branching density.`);
}

function renderField(data, metric = "complexity") {
  const svg = d3.select("#field-chart");
  const detail = d3.select("#field-detail");
  const summary = d3.select("#field-summary");
  const label = d3.select("#field-label");
  const definition = metricDefinitions[metric];
  const width = 900;
  const height = 330;
  const margin = { top: 18, right: 20, bottom: 45, left: 52 };
  svg.attr("viewBox", `0 0 ${width} ${height}`);
  svg.selectAll("*").remove();
  label.text(definition.fieldLabel);
  const values = data.profiles.map(profile => profileMetric(profile, metric));
  const sorted = [...values].sort(d3.ascending);
  const p50 = d3.quantileSorted(sorted, .5) ?? 0;
  const p95 = d3.quantileSorted(sorted, .95) ?? 0;
  const maximum = d3.max(values) ?? 0;
  const x = d3.scaleLog().domain([Math.max(1, d3.min(values) || 1), Math.max(2, maximum)]).range([margin.left, width - margin.right]);
  const y = d3.scaleLinear().domain([0, 1]).range([height - margin.bottom, margin.top]);
  svg.append("g").attr("class", "grid").attr("transform", `translate(0,${height - margin.bottom})`)
    .call(d3.axisBottom(x).ticks(8, "s").tickSize(-height + margin.top + margin.bottom).tickFormat(""));
  svg.append("g").attr("class", "axis").attr("transform", `translate(0,${height - margin.bottom})`)
    .call(d3.axisBottom(x).ticks(8, "s"));
  const points = data.profiles.map((profile, index) => ({
    profile,
    value: profileMetric(profile, metric),
    lane: (Math.sin(index * 12.9898) + 1) / 2,
  }));
  const top = new Set([...points].sort((a, b) => b.value - a.value).slice(0, 10).map(point => point.profile.id));
  svg.append("g").selectAll("circle").data(points).join("circle")
    .attr("class", point => `field-dot${top.has(point.profile.id) ? " is-tail" : ""}`)
    .attr("cx", point => x(Math.max(1, point.value)))
    .attr("cy", point => y(point.lane))
    .attr("r", point => top.has(point.profile.id) ? 3.6 : 2.2)
    .on("mouseenter", (_, point) => {
      detail.html(`<strong>${point.profile.type}.${point.profile.method}</strong> · ${definition.label.toLowerCase()} ${definition.format(point.value)} · token ${point.profile.token}`);
    })
    .on("mouseleave", () => detail.text("Hover a point to identify the method body."));
  for (const [value, text] of [[p50, "middle"], [p95, "upper tail"]]) {
    if (!value) continue;
    svg.append("line").attr("class", "field-guide").attr("x1", x(value)).attr("x2", x(value)).attr("y1", margin.top).attr("y2", height - margin.bottom);
    svg.append("text").attr("class", "field-label").attr("x", x(value) + 5).attr("y", margin.top + 12).text(`${text} ${definition.format(value)}`);
  }
  summary.html(`<strong>${data.bodyCount.toLocaleString()}</strong> bodies · middle ${definition.format(p50)} · upper tail ${definition.format(p95)} · largest ${definition.format(maximum)}`);
  detail.text("Hover a point to identify the method body.");
}

function renderComparison(left, right) {
  const svg = d3.select("#compare-chart");
  const detail = d3.select("#compare-detail");
  const width = 900;
  const height = 390;
  const margin = { top: 30, right: 26, bottom: 28, left: 180 };
  svg.attr("viewBox", `0 0 ${width} ${height}`);
  const metrics = Object.keys(metricDefinitions);
  const summaries = [left, right].map(data => ({
    name: data.assembly,
    values: Object.fromEntries(metrics.map(metric => {
      const values = data.profiles.map(profile => profileMetric(profile, metric)).sort(d3.ascending);
      return [metric, { p50: d3.quantileSorted(values, .5) || 0, p95: d3.quantileSorted(values, .95) || 0, max: d3.max(values) || 0 }];
    })),
  }));
  const rowHeight = (height - margin.top - margin.bottom) / metrics.length;
  metrics.forEach((metric, index) => {
    const y = margin.top + index * rowHeight + rowHeight / 2;
    const max = Math.max(summaries[0].values[metric].max, summaries[1].values[metric].max, 2);
    const x = d3.scaleLog().domain([1, max]).range([margin.left, width - margin.right]);
    const row = svg.append("g");
    row.append("line").attr("class", "compare-row").attr("x1", margin.left).attr("x2", width - margin.right).attr("y1", y + rowHeight / 2 - 5).attr("y2", y + rowHeight / 2 - 5);
    row.append("text").attr("class", "compare-label").attr("x", 0).attr("y", y + 4).text(metricDefinitions[metric].label);
    row.append("line").attr("class", "compare-axis-label").attr("x1", margin.left).attr("x2", width - margin.right).attr("y1", y + 12).attr("y2", y + 12);
    [summaries[0], summaries[1]].forEach((summary, seriesIndex) => {
      const values = summary.values[metric];
      const offset = seriesIndex === 0 ? -7 : 7;
      row.append("line").attr("class", "compare-line").attr("stroke", seriesIndex === 0 ? "var(--blue)" : "var(--teal)")
        .attr("x1", x(Math.max(1, values.p50))).attr("x2", x(Math.max(1, values.max))).attr("y1", y + offset).attr("y2", y + offset);
      [[values.p50, "p50"], [values.p95, "p95"], [values.max, "max"]].forEach(([value, name]) => {
        row.append("circle").attr("class", "compare-point").attr("fill", seriesIndex === 0 ? "var(--blue)" : "var(--teal)")
          .attr("cx", x(Math.max(1, value))).attr("cy", y + offset).attr("r", name === "max" ? 5 : 3.5)
          .on("mouseenter", () => detail.html(`<strong>${summary.name}</strong> · ${metricDefinitions[metric].label} · ${name} ${metricDefinitions[metric].format(value)}`));
      });
    });
    row.append("text").attr("class", "compare-axis-label").attr("x", margin.left).attr("y", y + rowHeight / 2 - 11).text("1");
    row.append("text").attr("class", "compare-axis-label").attr("text-anchor", "end").attr("x", width - margin.right).attr("y", y + rowHeight / 2 - 11).text(max.toLocaleString());
  });
  detail.text("Hover a landmark to compare the same measure across the two libraries.");
}

renderTreemap(analysis);
renderField(analysis);
renderComparison(analysis, research);
d3.select("#field-metric").on("change", event => renderField(analysis, event.target.value));
