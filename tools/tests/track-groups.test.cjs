const assert = require('node:assert/strict');
const { test } = require('node:test');
const { partitionTracks } = require('../../ui/Tym.Ui/wwwroot/track-groups.js');

test('0 keeps the twelve-track paged view', () => {
  const tracks = Array.from({ length: 25 }, (_, i) => ({ id: `TT${i + 1}` }));
  const groups = partitionTracks(tracks, 0);
  assert.deepEqual(groups.map(group => group.tracks.length), [12, 12, 1]);
  assert.deepEqual(groups.map(group => [group.start, group.end]), [[0, 12], [12, 24], [24, 25]]);
});

test('107 tracks split evenly into ten nonempty displays', () => {
  const groups = partitionTracks(Array.from({ length: 107 }, (_, i) => i), 10);
  assert.deepEqual(groups.map(group => group.tracks.length), [11, 11, 11, 11, 11, 11, 11, 10, 10, 10]);
  assert.equal(groups.at(-1).tracks.at(-1), 106);
});

test('all options preserve track order, identity and complete coverage', () => {
  for (const total of [...Array.from({ length: 111 }, (_, i) => i), 2056]) {
    const tracks = Array.from({ length: total }, (_, i) => ({ id: `TT${i + 1}` }));
    const original = tracks.slice();
    for (let requested = 0; requested <= 10; requested++) {
      const groups = partitionTracks(tracks, requested);
      assert.equal(groups.length, requested ? Math.min(requested, total) : Math.ceil(total / 12));
      assert.deepEqual(groups.flatMap(group => group.tracks), tracks);
      groups.forEach((group, i) => {
        assert.equal(group.index, i);
        assert.ok(group.tracks.length > 0);
        assert.equal(group.end - group.start, group.tracks.length);
        assert.equal(group.start, i ? groups[i - 1].end : 0);
        assert.equal(group.tracks[0], tracks[group.start]);
      });
      if (requested && groups.length) {
        const sizes = groups.map(group => group.tracks.length);
        assert.ok(Math.max(...sizes) - Math.min(...sizes) <= 1);
      }
    }
    assert.deepEqual(tracks, original);
  }
});

test('one group shows all tracks; excess groups never create empty displays', () => {
  assert.deepEqual(partitionTracks([1, 2, 3], 1)[0].tracks, [1, 2, 3]);
  assert.equal(partitionTracks([1, 2, 3], 10).length, 3);
  assert.deepEqual(partitionTracks([], 10), []);
});

test('invalid group counts and page sizes are rejected', () => {
  for (const value of [-1, 11, 0.5, NaN, '2']) assert.throws(() => partitionTracks([], value), RangeError);
  for (const value of [-1, 0, 0.5]) assert.throws(() => partitionTracks([], 0, value), RangeError);
});
