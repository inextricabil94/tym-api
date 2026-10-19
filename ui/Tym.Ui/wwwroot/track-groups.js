(function (root) {
  'use strict';

  function partitionTracks(tracks, requestedGroups = 0, pageSize = 12) {
    if (!Number.isInteger(requestedGroups) || requestedGroups < 0 || requestedGroups > 10)
      throw new RangeError('Track display groups must be an integer from 0 to 10.');
    if (!Number.isInteger(pageSize) || pageSize < 1) throw new RangeError('Page size must be positive.');
    const count = requestedGroups ? Math.min(requestedGroups, tracks.length) : Math.ceil(tracks.length / pageSize);
    if (!count) return [];
    const size = Math.floor(tracks.length / count);
    const extra = tracks.length % count;
    let start = 0;
    return Array.from({ length: count }, (_, index) => {
      const end = requestedGroups ? start + size + (index < extra ? 1 : 0) : Math.min(start + pageSize, tracks.length);
      const group = { index, start, end, tracks: tracks.slice(start, end) };
      start = end;
      return group;
    });
  }

  if (typeof module === 'object' && module.exports) module.exports = { partitionTracks };
  else root.TymTrackGroups = { partitionTracks };
})(typeof window === 'object' ? window : globalThis);
