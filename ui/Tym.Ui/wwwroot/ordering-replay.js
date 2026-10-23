(function (root, factory) {
  'use strict'; const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api; else root.TymReplay = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  const create = () => Object.freeze({ run: null, step: 0, playing: false, speed: 8, epoch: 0 });
  function transition(state, action) {
    const total = state.run?.operations.length || 0;
    switch (action.type) {
      case 'load': return Object.freeze({ ...state, run: action.run, step: 0, playing: false, epoch: state.epoch + 1 });
      case 'play': return Object.freeze({ ...state, step: state.step === total ? 0 : state.step, playing: Boolean(total), epoch: state.epoch + 1 });
      case 'pause': return Object.freeze({ ...state, playing: false, epoch: state.epoch + 1 });
      case 'cancel': return Object.freeze({ ...state, run: null, step: 0, playing: false, epoch: state.epoch + 1 });
      case 'restart': return Object.freeze({ ...state, step: 0, playing: false, epoch: state.epoch + 1 });
      case 'speed': return Object.freeze({ ...state, speed: Math.max(1, Math.min(60, Number(action.value) || 1)) });
      case 'seek': case 'next': case 'previous': {
        const value = action.type === 'seek' ? action.step : state.step + (action.type === 'next' ? 1 : -1);
        return Object.freeze({ ...state, step: Math.max(0, Math.min(total, Math.trunc(value) || 0)), playing: false, epoch: state.epoch + 1 });
      }
      case 'tick': {
        if (!state.playing || action.epoch !== state.epoch) return state;
        const step = Math.min(total, state.step + 1); return Object.freeze({ ...state, step, playing: step < total });
      }
      default: return state;
    }
  }
  return { create, transition };
});
