(function () {
  'use strict';
  function apply(theme) {
    const value = theme === 'light' ? 'light' : 'dark';
    document.documentElement.dataset.theme = value;
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', value === 'light' ? '#f3f8f5' : '#292b2a');
    return value;
  }
  let initial = 'dark';
  try { initial = localStorage.getItem('tym-ui-theme') || initial; } catch {}
  apply(initial);
  window.TymTheme = {
    set(theme) {
      const value = apply(theme);
      try { localStorage.setItem('tym-ui-theme', value); } catch {}
      return value;
    }
  };
})();
