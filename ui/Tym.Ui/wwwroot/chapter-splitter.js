(function (root) {
  'use strict';

  const fold = text => text.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
  const numbers = new Map();
  function add(value, ...words) { for (const word of words) numbers.set(word, value); }
  const en = ['zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'eleven', 'twelve',
    'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen', 'eighteen', 'nineteen'];
  const ro = ['zero', 'unu', 'doi', 'trei', 'patru', 'cinci', 'sase', 'sapte', 'opt', 'noua', 'zece', 'unsprezece',
    'doisprezece', 'treisprezece', 'paisprezece', 'cincisprezece', 'saisprezece', 'saptesprezece', 'optsprezece', 'nouasprezece'];
  en.forEach((word, value) => add(value, word));
  ro.forEach((word, value) => add(value, word));
  add(1, 'un', 'o', 'una', 'unul', 'intai', 'intaiul', 'intaia', 'primul', 'prima', 'first', 'unulea');
  add(2, 'doua', 'second'); add(12, 'douasprezece');
  const ordinals = ['third', 'fourth', 'fifth', 'sixth', 'seventh', 'eighth', 'ninth', 'tenth', 'eleventh', 'twelfth',
    'thirteenth', 'fourteenth', 'fifteenth', 'sixteenth', 'seventeenth', 'eighteenth', 'nineteenth'];
  ordinals.forEach((word, index) => add(index + 3, word));
  ['doilea', 'treilea', 'patrulea', 'cincilea', 'saselea', 'saptelea', 'optulea', 'noualea', 'zecelea']
    .forEach((word, index) => add(index + 2, word));
  ['treia', 'patra', 'cincea', 'sasea', 'saptea', 'opta', 'zecea'].forEach((word, index) =>
    add([3, 4, 5, 6, 7, 8, 10][index], word));
  for (let value = 11; value < 20; value++) add(value, ro[value] + 'lea', ro[value] + 'a');
  const enTens = ['twenty', 'thirty', 'forty', 'fifty', 'sixty', 'seventy', 'eighty', 'ninety'];
  const enOrdinalTens = ['twentieth', 'thirtieth', 'fortieth', 'fiftieth', 'sixtieth', 'seventieth', 'eightieth', 'ninetieth'];
  const roTens = ['douazeci', 'treizeci', 'patruzeci', 'cincizeci', 'saizeci', 'saptezeci', 'optzeci', 'nouazeci'];
  enTens.forEach((word, index) => add((index + 2) * 10, word, enOrdinalTens[index], roTens[index], roTens[index] + 'lea', roTens[index].slice(0, -1) + 'ea'));
  add(12, 'douasprezecelea', 'douasprezecea');
  const roman = /^(?=.+)M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$/;
  function romanValue(word) {
    const values = { I: 1, V: 5, X: 10, L: 50, C: 100, D: 500, M: 1000 };
    return Array.from(word).reduce((sum, letter, i) => sum + (values[letter] < values[word[i + 1]] ? -values[letter] : values[letter]), 0);
  }

  function wordValue(words) {
    let total = 0, group = 0, previous = 0, conjunction = false;
    for (const word of words) {
      if (word === 'and' || word === 'si') {
        if (!(group || total) || conjunction) return null;
        conjunction = true;
        continue;
      }
      if (['hundred', 'hundredth', 'suta', 'sute', 'sutelea', 'sutea'].includes(word)) {
        if (group < 1 || group > 9) return null;
        group *= 100; previous = 100; conjunction = false;
        continue;
      }
      if (['thousand', 'thousandth', 'mie', 'mii', 'miilea', 'mia'].includes(word)) {
        if (!group || total) return null;
        total = group * 1000; group = 0; previous = 0; conjunction = false;
        continue;
      }
      const value = numbers.get(word);
      if (value === undefined || (previous && previous < 100 && !(previous >= 20 && previous % 10 === 0 && value > 0 && value < 10))) return null;
      group += value; previous = value; conjunction = false;
    }
    return !conjunction && total + group > 0 ? total + group : null;
  }

  function parseChapterNumber(text) {
    const rest = text.trim().replace(/^(?:al|a|the|num\u0103rul|numarul|number|nr\.?)[\t ]+/i, '');
    const digit = rest.match(/^(\d+)(?:(?:st|nd|rd|th)|(?:[\t ]*-[\t ]*(?:lea|a)))?(?=$|[\s.:)\-\u2013\u2014])/i);
    if (digit && Number.isSafeInteger(Number(digit[1]))) return { number: digit[1], value: Number(digit[1]), consumed: text.length - rest.length + digit[0].length };
    const romanMatch = rest.match(/^([ivxlcdm]+)(?:-lea)?(?=$|[\s.:)\-\u2013\u2014])/i);
    if (romanMatch && roman.test(romanMatch[1].toUpperCase()))
      return { number: romanMatch[1], value: romanValue(romanMatch[1].toUpperCase()), consumed: text.length - rest.length + romanMatch[0].length };
    const words = Array.from(rest.matchAll(/[\p{L}]+/gu));
    let best = null;
    for (let count = 1; count <= Math.min(12, words.length); count++) {
      const current = words[count - 1];
      if (count === 1 && current.index !== 0) break;
      if (count > 1 && !/^[\s\-\u2010-\u2014]+$/.test(rest.slice(words[count - 2].index + words[count - 2][0].length, current.index))) break;
      const normalized = words.slice(0, count).map(word => fold(word[0]));
      const value = wordValue(normalized);
      if (value !== null && (count > 1 || !['un', 'o'].includes(normalized[0]))) {
        const end = current.index + current[0].length;
        best = { number: rest.slice(0, end), value, consumed: text.length - rest.length + end };
      }
      if (value === null && !['si', 'and'].includes(normalized.at(-1))) break;
    }
    return best;
  }

  function splitChapters(text, { standalone = 'auto' } = {}) {
    let headings = [];
    const pattern = /^[\t \u00a0\ufeff\f]*(?:capitolul|capitol|chapter)[\t \u00a0]+([^\r\n]+)/gim;
    for (const match of text.matchAll(pattern)) {
      const parsed = parseChapterNumber(match[1]);
      if (!parsed) continue;
      headings.push({ kind: 'chapter', number: parsed.number, value: parsed.value, label: match[0].trim(), start: match.index, detection: 'prefixed' });
    }
    // Bare numbering requires a sustained sequence and substantial prose; PDF folios are excluded.
    if (!headings.length && standalone !== 'off' && (standalone === 'always' || !text.includes('\f'))) {
      const candidates = [];
      for (const match of text.matchAll(/^[\t \ufeff]*([^\r\n]{1,90})[\t ]*$/gm)) {
        const label = match[1].trim();
        const parsed = parseChapterNumber(label);
        if (!parsed || !/^[\t .):\-]*$/.test(label.slice(parsed.consumed))) continue;
        if (/^[ivxlcdm]+[.)]?$/i.test(label) && label !== label.toUpperCase()) continue;
        candidates.push({ kind: 'chapter', number: parsed.number, value: parsed.value, label, start: match.index, detection: 'standalone-sequence' });
      }
      const runs = [];
      for (const candidate of candidates) {
        const run = runs.at(-1);
        const previous = run?.at(-1);
        if (previous && candidate.value === previous.value + 1 && candidate.start - previous.start >= (standalone === 'always' ? 0 : 500)) run.push(candidate);
        else runs.push([candidate]);
      }
      headings = runs.filter(run => run.length >= (standalone === 'always' ? 2 : 3)).flat();
    }
    if (!headings.length) return [];

    const sections = [];
    if (text.slice(0, headings[0].start).trim()) {
      sections.push({ kind: 'preamble', number: null, label: 'Front matter', start: 0 });
    } else {
      headings[0].start = 0;
    }
    sections.push(...headings);
    return sections.map((section, index) => ({ ...section, end: sections[index + 1]?.start ?? text.length }));
  }

  if (typeof module === 'object' && module.exports) module.exports = { splitChapters, parseChapterNumber };
  else root.TymChapters = { splitChapters, parseChapterNumber };
})(typeof window === 'object' ? window : globalThis);
