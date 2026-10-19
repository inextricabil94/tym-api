const { test } = require('node:test');
const assert = require('node:assert/strict');
const { cleanBookText, bookSections } = require('../book-text.cjs');

test('prose normalization preserves Romanian words, dialogue, punctuation and intentional line breaks', () => {
  const original = '\uFEFF\r\n\tCapitolul UNU  \r\n\r\n    \u0218tefan: - Bun\u0103!\t\r\n  Un cuv\u00e2nt-\r\n  compus.  \r\n\r\n\r\n\tAlt paragraf.\u00a0';
  assert.equal(cleanBookText(original), 'Capitolul UNU\n\n\u0218tefan: - Bun\u0103!\nUn cuv\u00e2nt-\ncompus.\n\nAlt paragraf.\n');
});
test('normalization is idempotent and rejects empty files', () => {
  const text = cleanBookText(' One\r\n\r\n Two\t\n');
  assert.equal(cleanBookText(text), text);
  assert.throws(() => cleanBookText(' \r\n\t'), /no readable text/);
});
test('chapter sections cover every UTF-16 character without overlaps', () => {
  const text = cleanBookText('Title\n\nCapitolul UNU\nAna a plecat.\n\nCapitolul DOI\n\u0218tefan a venit.');
  const sections = bookSections(text);
  assert.ok(sections.some(section => section.kind === 'chapter'));
  assert.equal(sections.map(section => text.slice(section.start, section.end)).join(''), text);
  assert.equal(new Set(sections.map(section => section.id)).size, sections.length);
});
test('long inputs without headings are completely covered by bounded reading sections', () => {
  const text = cleanBookText(('\ud83d\ude42 Ana a venit.\n\n').repeat(30000));
  const sections = bookSections(text);
  assert.ok(sections.length > 1);
  assert.equal(sections.map(section => text.slice(section.start, section.end)).join(''), text);
  for (const section of sections) {
    assert.ok(section.character_count <= 100000);
    assert.equal(section.character_count, section.end - section.start);
    assert.doesNotMatch(text.slice(section.start, section.end), /^[\uDC00-\uDFFF]|[\uD800-\uDBFF]$/u);
  }
});
