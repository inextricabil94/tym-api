import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { select } = require('../ui/Tym.Ui/wwwroot/density-cache.js');
const api = (process.env.TYM_RESEARCH_API_URL || 'http://127.0.0.1:8765').replace(/\/$/, '');
const text = 'Adam and Johan grew up in the same house. Years earlier, Karl had carried Adam through the rain. Margaret remembered her mother in Jakarta. Meanwhile, Karl traveled alone across the island. Adam searched for Margaret after the storm. Margaret found Adam at the doorway. Adam disappeared before dawn.';
// One authored request consumes one normal diagram permit. No books or external NLP services are used.
const response = await fetch(api + '/v1/diagrams', { method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ text, options: { language: 'en', width: 1200, track_density: 50,
    cache_track_densities: true, progressive_density_cache: false, include_debug: true } }) });
if (!response.ok) throw new Error(`API returned HTTP ${response.status}`);
const data = await response.json();
const fields = segment => ({ id: segment.id, text: segment.text, span_start: segment.span_start, span_end: segment.span_end,
  type: segment.type, perspective: segment.perspective, event_ids: segment.event_ids });
const baseline = select(data, 50), spanKey = JSON.stringify(baseline.diagram.segments.map(fields));
const rows = [10, 50, 100].map(density => {
  const view = select(data, density);
  return { density, tracks: view.diagram.tracks.length, relations: view.diagram.relations.length,
    segment_relations: view.diagram.relations.filter(item => item.from_id.startsWith('TS') && item.to_id.startsWith('TS')).length,
    endpoints: view.diagram.endpoints.length,
    boundaries: Object.fromEntries([...new Set(view.diagram.boundaries.map(item => item.type))].sort().map(type =>
      [type, view.diagram.boundaries.filter(item => item.type === type).length])),
    changed_story_orders_vs_50: view.diagram.segments.filter((item, index) => item.story_order !== baseline.diagram.segments[index].story_order).length,
    source_spans_types_and_event_ids_unchanged: JSON.stringify(view.diagram.segments.map(fields)) === spanKey,
    timeml_unchanged: JSON.stringify(view.diagram.time_ml) === JSON.stringify(baseline.diagram.time_ml),
    svg_utf8_bytes: Buffer.byteLength(view.render.svg) };
});
console.log(JSON.stringify({ authored_example: true, characters: text.length, events: baseline.diagram.events.length,
  segments: baseline.diagram.segments.length, rows,
  seed_methods: [...new Set(baseline.diagram.events.map(item => item.classifier))],
  no_cue_segment_links: baseline.diagram.relations.filter(item => item.from_id.startsWith('TS') && !item.cue).map(item =>
    ({ from: item.from_id, to: item.to_id, relation: item.rel, evidence: item.evidence })),
  note: 'Descriptive outputs only: no gold annotations, accuracy estimate or controlled performance benchmark.' }, null, 2));
