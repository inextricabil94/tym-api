const { test } = require('node:test');
const assert = require('node:assert/strict');
const { measure } = require('../../ui/Tym.Ui/wwwroot/viewport.js');
test('window resolution is measured in CSS pixels and diagram width follows renderer bounds', () => {
  assert.deepEqual(measure(1440, 900), { width: 1440, height: 900, key: '1440x900', diagramWidth: 1440 });
  assert.equal(measure(390, 844).diagramWidth, 720);
  assert.equal(measure(3840, 2160).diagramWidth, 2400);
  assert.equal(measure(1200.8, 900.9).key, '1200x900');
});
test('height-only and small-screen width changes have distinct cache resolutions', () => {
  assert.notEqual(measure(1440, 900).key, measure(1440, 901).key);
  assert.notEqual(measure(320, 844).key, measure(390, 844).key);
  assert.equal(measure(NaN, -1).key, '1200x800');
});
