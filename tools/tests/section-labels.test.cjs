const test = require('node:test');
const assert = require('node:assert/strict');
const { sectionLabel } = require('../../ui/Tym.Ui/wwwroot/chapter-splitter.js');

test('chapter labels use Arabic numbers without modifying source metadata', () => {
  for (const heading of ['CAPITOLUL UNU', 'CAPITOLUL I', 'Capitolul 1', 'Capitolul intai', 'UNU']) {
    const section = Object.freeze({ label: heading, kind: 'chapter', start: 25, end: 90 });
    assert.equal(sectionLabel(section, 7, 'ro'), 'Capitolul 1');
    assert.equal(section.label, heading);
  }
  assert.equal(sectionLabel({ label: 'CHAPTER TWO - The Journey', kind: 'chapter' }, 0), 'Chapter 2');
  assert.equal(sectionLabel({ label: 'CAPITOLUL AL DOUAZECI SI TREILEA / Part 2', kind: 'chapter' }, 0, 'ro'), 'Capitolul 23 / Sec\u021biunea 2');
  assert.equal(sectionLabel({ label: 'Reading section / Part 3', kind: 'part' }, 9, 'ro'), 'Sec\u021biunea 3');
  assert.equal(sectionLabel({ label: 'Front matter', kind: 'preamble' }, 0, 'ro'), 'Introducere');
});
