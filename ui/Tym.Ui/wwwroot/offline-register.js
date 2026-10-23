(function () {
  'use strict';
  if (!('serviceWorker' in navigator) || !window.isSecureContext || window.TYM_CONFIG?.examples?.length) return;
  navigator.serviceWorker.register('/service-worker.js', { updateViaCache: 'none' }).catch(() => {});
})();
