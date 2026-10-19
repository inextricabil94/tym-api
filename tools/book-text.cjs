const { splitChapters } = require('../ui/Tym.Ui/wwwroot/chapter-splitter.js');

function cleanBookText(source) {
  // Plain-text prose uses unindented paragraphs separated by blank lines.
  // Keep intentional line breaks, punctuation, hyphens and Unicode unchanged.
  const lines = source.replace(/^\uFEFF/u, '').replace(/\r\n?/gu, '\n').split('\n')
    .map(line => line.replace(/[\t \u00a0]+$/gu, '').replace(/^[\t \u00a0]+/gu, ''));
  const text = lines.join('\n').replace(/\n{3,}/gu, '\n\n').trim();
  if (!text) throw new Error('Book contains no readable text.');
  return `${text}\n`;
}

function bookSections(text) {
  const detected = splitChapters(text);
  const sections = detected.length ? detected : [{ label: 'Reading section', kind: 'part', start: 0, end: text.length }];
  const parts = sections.flatMap(section => {
    const result = [];
    let start = section.start;
    while (start < section.end) {
      let end = section.end;
      if (end - start > 100000) {
        const target = start + 80000;
        const paragraph = text.lastIndexOf('\n\n', target);
        const space = text.lastIndexOf(' ', target);
        end = paragraph > start + 40000 ? paragraph + 2 : space > start ? space + 1 : target;
        // Never split a supplementary Unicode character in half.
        if (/[\uD800-\uDBFF]/u.test(text[end - 1])) end--;
      }
      result.push({ ...section, start, end });
      start = end;
    }
    return result.map((part, index) => ({ ...part,
      label: result.length > 1 ? `${part.label} / Part ${index + 1}` : part.label }));
  });
  return parts.map((section, index) => ({ id: `s${index + 1}`,
    label: section.label.slice(0, 180), kind: section.kind, start: section.start, end: section.end,
    character_count: section.end - section.start }));
}

module.exports = { cleanBookText, bookSections };
