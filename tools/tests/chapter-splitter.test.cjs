const assert = require('node:assert/strict');
const { test } = require('node:test');
const { splitChapters, parseChapterNumber } = require('../../ui/Tym.Ui/wwwroot/chapter-splitter.js');

function check(text, expected) {
  const parts = splitChapters(text);
  assert.deepEqual(parts.map(part => part.number), expected);
  if (parts.length) {
    assert.equal(parts[0].start, 0);
    assert.equal(parts.at(-1).end, text.length);
    assert.equal(parts.map(part => text.slice(part.start, part.end)).join(''), text);
    parts.forEach((part, index) => {
      assert.ok(part.end > part.start);
      if (index) assert.equal(part.start, parts[index - 1].end);
    });
  }
  return parts;
}

test('Arabic chapter headings, front matter and numbering starting at 37', () => {
  check('Preface\r\n\r\nCAPITOLUL 37\r\nText.\r\nCAPITOLUL 38\r\nEnd.', [null, '37', '38']);
});
test('Roman numerals with inline Romanian titles', () => {
  const parts = check('Capitolul I. O serata\nText.\nCapitolul XIV. Excursii\nEnd.', ['I', 'XIV']);
  assert.equal(parts[1].label, 'Capitolul XIV. Excursii');
});
test('Capitol, English Chapter, case and CR-only line endings', () => {
  check('capitol 2: Titlu\rText\rCHAPTER iii - Title\rText', ['2', 'iii']);
});
test('numbers can repeat between parts; sections retain distinct offsets', () => {
  const parts = check('Capitolul I\nPart 1.\nCapitolul II\nText.\nCapitolul I\nPart 2.', ['I', 'II', 'I']);
  assert.notEqual(parts[0].start, parts[2].start);
});
test('headings must be line-anchored and numerals must be complete tokens', () => {
  check('A spus in capitolul 2 ceva.\nCapitolul 12abc\nCapitolul IIV\nCapitolul CIVIL', []);
});
test('blank prefix, indentation, BOM and form feeds are never discarded', () => {
  check('\ufeff \r\n\tCapitolul I\r\nText.\n\fCapitolul II\nText.\f', ['I', 'II']);
});
test('UTF-16 offsets preserve astral Unicode and Romanian diacritics', () => {
  const text = '\ud83d\ude42 Prefa\u021b\u0103\nCapitolul 1\n\u0218tefan.\nCapitolul 2\nSf\u00e2r\u0219it.';
  const parts = check(text, [null, '1', '2']);
  assert.equal(parts[1].start, text.indexOf('Capitolul 1'));
});
test('no fabricated chapters for unmarked text or isolated numbers', () => {
  check('', []);
  check('1\nText.\n2\nText.', []);
  check('A normal book without headings.', []);
});
test('single chapter retains its final newline', () => {
  check('Capitolul 1\nText.\n', ['1']);
});
test('large text preserves every character and every marked chapter', () => {
  const text = Array.from({ length: 100 }, (_, i) => `Capitolul ${i + 1}\n${'Text. '.repeat(2000)}\n`).join('');
  check(text, Array.from({ length: 100 }, (_, i) => String(i + 1)));
});

test('Romanian and English cardinal, ordinal and spelled-out heading forms', () => {
  for (const [heading, expected] of [
    ['al doilea', 2], ['a doua', 2], ['\u00eent\u00e2i', 1], ['\u00eent\u00e2iul', 1], ['primul', 1],
    ['dou\u0103zeci \u0219i unu', 21], ['al dou\u0103zeci \u0219i unulea', 21], ['al treizecilea', 30], ['a dou\u0103zecea', 20],
    ['al 21-lea', 21], ['a 2-a', 2], ['al II-lea', 2], ['nr. 12', 12], ['num\u0103rul treisprezece', 13],
    ['One', 1], ['Twenty-First', 21], ['the third', 3], ['21st', 21], ['32nd', 32], ['One Hundred and Twenty-Three', 123],
    ['o sut\u0103 dou\u0103zeci \u0219i trei', 123], ['One Thousand and One', 1001], ['dou\u0103 mii treizeci \u0219i unu', 2031]
  ]) assert.equal(parseChapterNumber(heading)?.value, expected, heading);
});

test('spelled-out headings preserve original labels, all text and UTF-16 source coordinates', () => {
  const text = '\ud83d\ude42 Preface\r\nCAPITOLUL \u00ceNT\u00c2I\r\nText.\r\nCapitolul al doilea: Un drum\r\nEnd.\nChapter Twenty-First - Home\nLast.';
  const parts = splitChapters(text);
  assert.deepEqual(parts.filter(part => part.kind === 'chapter').map(part => part.value), [1, 2, 21]);
  assert.equal(parts.map(part => text.slice(part.start, part.end)).join(''), text);
  assert.equal(parts[1].start, text.indexOf('CAPITOLUL'));
  assert.equal(parts.at(-1).label, 'Chapter Twenty-First - Home');
});

test('standalone Arabic, Roman and word numbering needs sustained prose-separated sequences', () => {
  for (const headings of [['1', '2', '3', '4'], ['I', 'II', 'III', 'IV'], ['UNU', 'DOI', 'TREI', 'PATRU'],
    ['ONE', 'TWO', 'THREE', 'FOUR'], ['Unu', 'Doi', 'trei', 'Patru'], ['First', 'Second', 'Third', 'Fourth']]) {
    const text = 'Book\n\n' + headings.map(heading => `${heading}\n\n${'Long prose. '.repeat(60)}\n\n`).join('');
    const parts = splitChapters(text);
    assert.deepEqual(parts.filter(part => part.kind === 'chapter').map(part => part.value), [1, 2, 3, 4]);
    assert.equal(parts.map(part => text.slice(part.start, part.end)).join(''), text);
  }
  assert.deepEqual(splitChapters('1\nshort\n2\nlist\n3\nend'), []);
  assert.deepEqual(splitChapters(['1', '2', '3'].map(number => `${number}\n${'PDF page. '.repeat(100)}\f\n`).join('')), []);
});

test('short excerpts can opt into standalone word-numbered chapter detection', () => {
  for (const headings of [['UNU', 'DOI', 'TREI', 'PATRU'], ['ONE', 'TWO', 'THREE', 'FOUR']]) {
    const text = headings.map(heading => `${heading}\nAdam waited.\n\n`).join('');
    const sections = splitChapters(text, { standalone: 'always' });
    assert.deepEqual(sections.map(section => section.value), [1, 2, 3, 4]);
    assert.equal(sections.map(section => text.slice(section.start, section.end)).join(''), text);
    assert.deepEqual(splitChapters(text, { standalone: 'off' }), []);
  }
});
