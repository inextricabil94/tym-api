(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.TymSegmentation = api;
})(typeof globalThis === 'object' ? globalThis : this, function () {
  'use strict';
  const algorithms = [
    { id: 'article_rules', label: 'Article-guided rules' },
    { id: 'mlnet_logistic', label: 'ML.NET logistic regression (seed)' },
    { id: 'mlnet_perceptron', label: 'ML.NET averaged perceptron (seed)' }
  ];

  function sourcePage(text, segments, page = 0, size = 20) {
    if (typeof text !== 'string' || !Array.isArray(segments) || !Number.isInteger(size) || size < 1) return null;
    let previous = 0;
    for (const segment of segments) {
      if (!Number.isInteger(segment.span_start) || !Number.isInteger(segment.span_end)
        || segment.span_start < previous || segment.span_end <= segment.span_start || segment.span_end > text.length) return null;
      previous = segment.span_end;
    }
    const pages = Math.max(1, Math.ceil(segments.length / size));
    const index = Math.min(pages - 1, Math.max(0, Math.trunc(page) || 0));
    const startIndex = index * size, visible = segments.slice(startIndex, startIndex + size);
    let cursor = startIndex ? segments[startIndex - 1].span_end : 0;
    const parts = [];
    for (const segment of visible) {
      if (cursor < segment.span_start) parts.push({ kind: 'gap', text: text.slice(cursor, segment.span_start) });
      parts.push({ kind: 'segment', text: text.slice(segment.span_start, segment.span_end), segment });
      cursor = segment.span_end;
    }
    const end = index === pages - 1 ? text.length : cursor;
    if (cursor < end) parts.push({ kind: 'gap', text: text.slice(cursor, end) });
    return { index, pages, startIndex, segments: visible, parts };
  }

  return { algorithms, sourcePage };
});
