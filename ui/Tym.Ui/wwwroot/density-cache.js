(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.TymDensityCache = api;
})(typeof globalThis === 'object' ? globalThis : this, function () {
  'use strict';

  function choices(response) {
    if (!response) return [];
    const density = response.track_generation?.density ?? 50;
    const result = (response.density_cache?.options || []).filter(option =>
      Number.isInteger(option.density) && option.density >= 0 && option.density <= 100
      && (option.view_id === 'base' || response.density_cache.views?.[option.view_id]));
    if (!result.some(option => option.density === density)) {
      result.push({ density, view_id: 'base', track_count: response.diagram?.tracks?.length || 0 });
    }
    return result.slice().sort((a, b) => a.density - b.density);
  }

  function select(response, density) {
    if (!response) return null;
    const { density_cache: cache, ...base } = response;
    const choice = choices(response).find(option => option.density === density);
    if (!choice) return base;
    const metadata = { ...base.track_generation, density };
    if (choice.view_id === 'base') return { ...base, track_generation: metadata };
    const view = cache.views[choice.view_id];
    const placements = new Map(view.segment_placements.map(segment => [segment.id, segment]));
    return {
      ...base,
      diagram: {
        ...base.diagram,
        tracks: view.tracks,
        segments: base.diagram.segments.map(segment => ({ ...segment, ...placements.get(segment.id) })),
        endpoints: view.endpoints,
        relations: view.relations,
        boundaries: view.boundaries
      },
      analysis: view.analysis,
      render: view.render,
      xml: view.xml,
      warnings: view.warnings,
      track_generation: metadata
    };
  }

  function append(response, density, view) {
    if (!response?.density_cache || !view?.render?.svg || !Array.isArray(view.segment_placements)) return response;
    const id = `density-${density}`;
    const cache = response.density_cache;
    return { ...response, density_cache: { ...cache,
      options: [...cache.options.filter(option => option.density !== density), { density, view_id: id, track_count: view.tracks.length }],
      views: { ...cache.views, [id]: view },
      pending_densities: (cache.pending_densities || []).filter(value => value !== density) } };
  }

  return { choices, select, append };
});
