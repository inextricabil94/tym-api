(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.TymViewport = api;
})(typeof globalThis === 'object' ? globalThis : this, function () {
  function measure(width, height) {
    const normalize = (value, fallback) => Number.isFinite(value) && value > 0 ? Math.floor(value) : fallback;
    const windowWidth = normalize(width, 1200);
    const windowHeight = normalize(height, 800);
    return { width: windowWidth, height: windowHeight, key: `${windowWidth}x${windowHeight}`,
      diagramWidth: Math.max(720, Math.min(2400, windowWidth)) };
  }
  return { measure };
});
