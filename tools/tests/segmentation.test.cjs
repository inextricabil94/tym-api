const test = require('node:test');
const assert = require('node:assert/strict');
const { algorithms, sourcePage, hasTemporalOrdering } = require('../../ui/Tym.Ui/wwwroot/segmentation.js');
const cache = require('../../ui/Tym.Ui/wwwroot/density-cache.js');

test('three explicit algorithms retain the article-guided default', () => {
  assert.deepEqual(algorithms.map(item => item.id), ['article_rules', 'mlnet_logistic', 'mlnet_perceptron']);
});
test('evidence-based ordering rejects older or incomplete API/cache responses', () => {
  const response = { diagram: { temporal_ordering: { method: 'evidence_partial_order_v1', constraints: [], unresolved: [], conflicts: [], placements: [] } } };
  assert.equal(hasTemporalOrdering(response), true);
  for (const invalid of [null, {}, { diagram: {} },
    { diagram: { temporal_ordering: { ...response.diagram.temporal_ordering, method: 'text_order' } } },
    { diagram: { temporal_ordering: { ...response.diagram.temporal_ordering, unresolved: undefined } } }])
    assert.equal(Boolean(hasTemporalOrdering(invalid)), false);
});
test('marks exact UTF-16 source, preserving emoji, accents, gaps and whitespace', () => {
  const text = '\ud83d\ude42 Prefa\u021b\u0103\nAna a venit.\n\n\u0218tefan a plecat.  ';
  const first = text.indexOf('Ana'), second = text.indexOf('\u0218tefan');
  const segments = [{ id: 'TS1', span_start: first, span_end: text.indexOf('.' ) + 1 },
    { id: 'TS2', span_start: second, span_end: text.lastIndexOf('.') + 1 }];
  const pages = [sourcePage(text, segments, 0, 1), sourcePage(text, segments, 1, 1)];
  assert.equal(pages.flatMap(page => page.parts.map(part => part.text)).join(''), text);
  assert.equal(pages[1].parts.find(part => part.kind === 'segment').text, '\u0218tefan a plecat.');
});
test('rejects corrupt, overlapping, out-of-range or source-unavailable spans', () => {
  for (const ranges of [[[-1, 2]], [[0, 99]], [[2, 1]], [[0, 3], [2, 4]], [[0.5, 2]]])
    assert.equal(sourcePage('text', ranges.map(([span_start, span_end]) => ({ span_start, span_end }))), null);
  assert.equal(sourcePage(null, []), null);
  assert.equal(sourcePage('text', [], 0, 0), null);
});
test('paginates long texts without dropping source or rendering every segment', () => {
  const text = Array.from({ length: 1003 }, (_, i) => `Event ${i}.\n`).join('');
  let cursor = 0;
  const segments = Array.from({ length: 1003 }, (_, i) => {
    const start = cursor, end = text.indexOf('\n', cursor);
    cursor = end + 1;
    return { id: `TS${i + 1}`, span_start: start, span_end: end };
  });
  const pages = Array.from({ length: 51 }, (_, page) => sourcePage(text, segments, page));
  assert.ok(pages.every(page => page.segments.length <= 20));
  assert.equal(pages.flatMap(page => page.parts.map(part => part.text)).join(''), text);
  assert.equal(sourcePage(text, segments, 999).index, 50);
  assert.equal(sourcePage('', []).parts.length, 0);
});
test('density selection retains boundary metadata and UTF-16 spans', () => {
  const segmentation = { algorithm: 'mlnet_logistic', decisions: [{ event_id: 'E1', is_boundary: true }] };
  const temporal_ordering = { method: 'evidence_partial_order_v1', constraints: [],
    unresolved: [{ from_id: 'EV1', to_id: 'EV2' }], placements: [{ segment_id: 'TS1', layout_order: 1, has_unresolved_order: true }] };
  const result = { segmentation, track_generation: { density: 50 }, diagram: { temporal_ordering,
    segments: [{ id: 'TS1', span_start: 2, span_end: 12, story_order: 1 }] },
    density_cache: { options: [{ density: 100, view_id: 'v1' }], views: { v1: { tracks: [], segment_placements: [{ id: 'TS1', track_id: 'TT2' }] } } } };
  const selected = cache.select(result, 100);
  assert.equal(selected.segmentation, segmentation);
  assert.equal(selected.diagram.segments[0].span_start, 2);
  assert.equal(selected.diagram.segments[0].track_id, 'TT2');
  assert.equal(selected.diagram.temporal_ordering, temporal_ordering);
  assert.equal(selected.diagram.segments[0].story_order, 1);
});
