'use strict';
importScripts('/ordering-sort.js?v=20261023-workbench');
self.onmessage = event => {
  const { requestId, algorithm, order, keys } = event.data;
  try { self.postMessage({ requestId, result: self.TymSorting.run(algorithm, order, keys) }); }
  catch (error) { self.postMessage({ requestId, error: { code: error.code || 'sort-failed', message: error.message } }); }
};
