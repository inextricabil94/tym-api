const test = require('node:test');
const assert = require('node:assert/strict');
const { choices, select } = require('../../ui/Tym.Ui/wwwroot/density-cache.js');

function fixture() {
  return {
    id: 'authored',
    track_generation: { density: 50, title_method: 'extractive-event-summary-v1' },
    diagram: { events: [{ id: 'EV1', text: 'Adam waited.' }], time_ml: { events: [{ id: 'e1' }] },
      tracks: [{ id: 'TT1' }], segments: [{ id: 'TS1', track_id: 'TT1', story_order: 1, text: 'Adam waited.' },
        { id: 'TS2', track_id: 'TT1', story_order: 2, text: 'Karl left.' }], endpoints: [], relations: [], boundaries: [] },
    analysis: { track_count: 1 }, render: { svg: 'base svg' }, xml: 'base xml', warnings: [],
    density_cache: { options: [{ density: 1, view_id: 'base', track_count: 1 }, { density: 50, view_id: 'base', track_count: 1 },
      { density: 100, view_id: 'v1', track_count: 2 }], views: { v1: {
      tracks: [{ id: 'TT1' }, { id: 'TT2' }], segment_placements: [{ id: 'TS1', track_id: 'TT1', story_order: 1 }, { id: 'TS2', track_id: 'TT2', story_order: 1 }],
      endpoints: [{ id: 'EP1' }], relations: [{ rel: 'BEFORE' }], boundaries: [{ id: 'B1' }],
      analysis: { track_count: 2 }, render: { svg: 'detailed svg' }, xml: 'detailed xml', warnings: ['detailed warning']
    } } }
  };
}

test('cached view keeps shared event/TimeML data and updates all density-dependent fields', () => {
  const base = fixture();
  const original = JSON.stringify(base);
  const view = select(base, 100);
  assert.strictEqual(view.diagram.events, base.diagram.events);
  assert.strictEqual(view.diagram.time_ml, base.diagram.time_ml);
  assert.deepEqual(view.diagram.segments[1], { id: 'TS2', track_id: 'TT2', story_order: 1, text: 'Karl left.' });
  assert.equal(view.analysis.track_count, 2);
  assert.equal(view.diagram.tracks.length, 2);
  assert.equal(view.diagram.endpoints[0].id, 'EP1');
  assert.equal(view.diagram.relations[0].rel, 'BEFORE');
  assert.equal(view.diagram.boundaries[0].id, 'B1');
  assert.equal(view.render.svg, 'detailed svg');
  assert.equal(view.xml, 'detailed xml');
  assert.deepEqual(view.warnings, ['detailed warning']);
  assert.equal(view.track_generation.density, 100);
  assert.ok(!Object.hasOwn(view, 'density_cache'));
  assert.equal(JSON.stringify(base), original);
});

test('densities with the same partition reuse the base SVG but retain selected metadata', () => {
  const base = fixture();
  assert.equal(select(base, 1).render.svg, 'base svg');
  assert.equal(select(base, 1).track_generation.density, 1);
  assert.equal(select(base, 50).track_generation.density, 50);
  assert.equal(select(base, 100).render.svg, 'detailed svg');
});

test('a nonpreset requested density remains selectable alongside cached presets', () => {
  const base = fixture();
  base.track_generation.density = 37;
  assert.deepEqual(choices(base).map(option => option.density), [1, 37, 50, 100]);
  assert.equal(select(base, 37).render.svg, 'base svg');
});

test('empty and legacy results work without advertising nonexistent cached views', () => {
  assert.deepEqual(choices(null), []);
  assert.equal(select(null, 50), null);
  const base = fixture();
  delete base.density_cache;
  assert.deepEqual(choices(base), [{ density: 50, view_id: 'base', track_count: 1 }]);
  assert.deepEqual(select(base, 100), base);
});

test('missing or out-of-range cached options are not advertised', () => {
  const base = fixture();
  base.density_cache.options.push({ density: 20, view_id: 'missing', track_count: 8 }, { density: 101, view_id: 'base', track_count: 1 });
  assert.deepEqual(choices(base).map(option => option.density), [1, 50, 100]);
});
