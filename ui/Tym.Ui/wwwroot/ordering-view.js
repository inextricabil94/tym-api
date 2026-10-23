(function (root) {
  'use strict';
  const R = root.React, e = R.createElement, O = root.TymOrdering, S = root.TymSorting, P = root.TymReplay;
  const PAGE = 24;
  const storage = () => { try { return root.localStorage; } catch { return { getItem() { throw Error(); }, setItem() { throw Error(); } }; } };
  function Workbench({ diagram, documentKey, allowText = true, active = true }) {
    const t = root.TymI18n.t;
    const model = R.useMemo(() => O.createModel(diagram), [diagram]);
    const originalPositions = R.useMemo(() => new Map(model.original.map((id, i) => [id, i + 1])), [model]);
    const unresolvedIds = R.useMemo(() => new Set((model.data.temporal_ordering?.placements || []).filter(item => item.has_unresolved_order).map(item => item.segment_id)), [model]);
    const initialize = () => {
      const initial = O.createState(model, documentKey);
      const restored = O.restore(model, initial, storage());
      return { model, state: restored.ok ? restored.state : initial, message: restored.ok ? 'Saved arrangement restored.' : restored.code === 'no-saved-layout' ? '' : restored.code };
    };
    const [session, setSession] = R.useState(initialize);
    const [page, setPage] = R.useState(0), [position, setPosition] = R.useState('1');
    const [drag, setDrag] = R.useState(null), [hover, setHover] = R.useState(null);
    const [targetGroupId, setTargetGroupId] = R.useState(''), [inspectorTab, setInspectorTab] = R.useState('Overview');
    const dragGroup = R.useRef(null);
    const [mode, setMode] = R.useState('Arrange'), [algorithm, setAlgorithm] = R.useState('merge');
    const [basis, setBasis] = R.useState('text_order'), [customKeys, setCustomKeys] = R.useState('{}');
    const [replay, replayDispatch] = R.useReducer(P.transition, undefined, P.create);
    const [computing, setComputing] = R.useState(false), [sortError, setSortError] = R.useState('');
    const [statistics, setStatistics] = R.useState([]), [syncReplay, setSyncReplay] = R.useState(false);
    const [reduced, setReduced] = R.useState(() => root.matchMedia('(prefers-reduced-motion: reduce)').matches);
    const worker = R.useRef(null), request = R.useRef(0);
    const pendingFocus = R.useRef(null);
    R.useEffect(() => {
      setSession(previous => previous.state.documentKey === documentKey && previous.model.documentFingerprint === model.documentFingerprint
        && previous.model.constraintFingerprint === model.constraintFingerprint
        ? { ...previous, model, state: { ...previous.state, validation: O.validate(model, previous.state.user) } } : initialize());
      setPage(0); setHover(null); setStatistics([]); setComputing(false); setSortError('');
      setCustomKeys(JSON.stringify(Object.fromEntries(model.original.map((id, i) => [id, model.byId[id].text_order ?? i + 1])), null, 2));
      request.current++; worker.current?.terminate(); worker.current = null; replayDispatch({ type: 'cancel' });
    }, [model, documentKey]);
    R.useEffect(() => () => { request.current++; worker.current?.terminate(); }, []);
    R.useEffect(() => { if (!active) replayDispatch({ type: 'pause' }); }, [active]);
    R.useEffect(() => {
      const media = root.matchMedia('(prefers-reduced-motion: reduce)');
      const changed = () => { setReduced(media.matches); if (media.matches) replayDispatch({ type: 'pause' }); };
      media.addEventListener('change', changed); return () => media.removeEventListener('change', changed);
    }, []);
    R.useEffect(() => {
      if (!replay.playing || reduced || !active) return;
      const timer = setTimeout(() => replayDispatch({ type: 'tick', epoch: replay.epoch }), 1000 / replay.speed);
      return () => clearTimeout(timer);
    }, [replay.playing, replay.step, replay.speed, replay.epoch, reduced, active]);
    const state = session.state, selected = model.byId[state.selected];
    const dispatch = action => setSession(previous => ({ ...previous, state: O.transition(model, previous.state, action), message: '' }));
    R.useEffect(() => { const current = state.user.indexOf(state.selected) + 1; setPosition(String(current)); setTargetGroupId(O.groupAt(state, current)?.id || ''); }, [state.user, state.selected]);
    const targetGroup = state.groups.find(group => group.id === targetGroupId) || O.groupAt(state, state.user.indexOf(state.selected) + 1);
    const preview = selected ? O.previewGroupMove(model, state, selected.id, Number(position), targetGroup?.id) : null;
    const currentPage = Math.min(page, Math.max(0, Math.ceil(state.user.length / PAGE) - 1));
    const visible = state.user.slice(currentPage * PAGE, (currentPage + 1) * PAGE);
    const diagnostics = hover || (preview && !preview.valid ? preview : state.validation);
    const conflicts = new Set(diagnostics.diagnostics.flatMap(item => item.segmentIds));
    const related = new Set(model.relations.filter(item => item.from === state.selected || item.to === state.selected).flatMap(item => [item.from, item.to]));
    const select = id => { dispatch({ type: 'select', id }); setPosition(String(state.user.indexOf(id) + 1)); setTargetGroupId(O.groupAt(state, state.user.indexOf(id) + 1)?.id || ''); };
    function chooseGroup(id) {
      const group = state.groups.find(item => item.id === id); if (!group) return;
      setTargetGroupId(id); editPosition(String(Math.max(group.start, Math.min(group.end, state.user.indexOf(state.selected) + 1))));
      setPage(Math.floor((group.start - 1) / PAGE));
    }
    function move(id, value, focus = false, groupId = targetGroup?.id) {
      const proposal = O.previewGroupMove(model, state, id, value, groupId);
      dispatch({ type: 'move-in-group', id, position: value, groupId }); setHover(null);
      if (proposal.valid) { setPage(Math.floor((value - 1) / PAGE)); if (focus) pendingFocus.current = id; }
    }
    R.useEffect(() => {
      if (!pendingFocus.current) return;
      root.document.querySelector(`.ordering-main .ordering-block[data-ts-id="${root.CSS.escape(pendingFocus.current)}"]`)?.focus();
      pendingFocus.current = null;
    }, [state.user, page]);
    function editPosition(value) { setPosition(value); setSession(previous => ({ ...previous, message: '', state: { ...previous.state, validation: O.validate(model, previous.state.user), notice: null } })); }
    function save() { const result = O.save(model, state, storage()); setSession(previous => ({ ...previous, message: result.ok ? 'Arrangement saved locally.' : result.code })); }
    function restore() { const result = O.restore(model, state, storage()); setSession(previous => ({ ...previous,
      state: result.ok ? result.state : previous.state, message: result.ok ? 'Saved arrangement restored.' : result.code })); }
    function cancel() { request.current++; worker.current?.terminate(); worker.current = null; setComputing(false); replayDispatch({ type: 'cancel' }); }
    function generateTrace() {
      cancel(); setSortError('');
      let keys;
      try {
        keys = basis === 'custom' ? JSON.parse(customKeys) : Object.fromEntries(state.user.map(id => [id, model.byId[id][basis]]));
        if (!keys || typeof keys !== 'object' || Array.isArray(keys)) throw Error('Prepared keys must be a JSON object of TS IDs and numeric values.');
        const compatible = S.compatibility(algorithm, S.prepare(state.user, keys)); if (!compatible.ok) throw Error(compatible.message);
      } catch (error) { setSortError(error.message); return; }
      const requestId = ++request.current; setComputing(true);
      try {
        const job = new Worker('/ordering-worker.js?v=20261023-workbench'); worker.current = job;
        job.onmessage = event => {
          if (request.current !== requestId || event.data.requestId !== requestId) return;
          job.terminate(); worker.current = null; setComputing(false);
          if (event.data.error) { setSortError(event.data.error.message); return; }
          const run = event.data.result, validation = O.validate(model, run.order);
          run.basis = basis; run.temporal = validation;
          replayDispatch({ type: 'load', run });
          setSession(previous => ({ ...previous, state: O.transition(model, previous.state, { type: 'suggest', order: run.order, algorithm, correctness: run.correctness }) }));
          setStatistics(previous => [...previous.filter(item => item.algorithm !== algorithm), run]);
        };
        job.onerror = () => { if (request.current === requestId) { job.terminate(); worker.current = null; setComputing(false); setSortError('Sorting worker failed. Retry or use a smaller document.'); } };
        job.postMessage({ requestId, algorithm, order: [...state.user], keys });
      } catch (error) { setComputing(false); setSortError(error.message); }
    }
    const frame = R.useMemo(() => replay.run ? S.reconstruct(replay.run, replay.step) : null, [replay.run, replay.step]);
    const destinations = R.useMemo(() => drag ? new Map(visible.map((id, i) => {
      const position = currentPage * PAGE + i + 1;
      return [position, O.previewGroupMove(model, state, drag, position, dragGroup.current).valid];
    })) : new Map(), [drag, state.user, targetGroupId, currentPage, model]);
    const button = (label, onClick, disabled = false, extra = {}) => e('button', { type: 'button', className: 'tool', onClick, disabled, title: t(label), ...extra }, t(label));
    const eventIds = new Set(selected?.event_ids || []);
    const evidence = [...model.relations.filter(item => item.from === selected?.id || item.to === selected?.id),
      ...(model.data.temporal_ordering?.constraints || []).filter(item => O.isAccepted(item) && (eventIds.has(item.from_id) || eventIds.has(item.to_id))).map(item => ({ from: item.from_id, to: item.to_id, type: item.rel, provenance: item }))];
    const property = (label, value) => e('div', { key: label }, e('dt', null, t(label)), e('dd', null, value == null || value === '' ? t('Unavailable') : value));
    const chooseMode = value => { setMode(value); replayDispatch({ type: 'pause' }); };
    function playbackControls() {
      return e('div', { className: 'ordering-playback' },
        e('div', { className: 'ordering-toolbar' },
          button(replay.playing ? 'Pause' : 'Play', () => replayDispatch({ type: replay.playing ? 'pause' : 'play' }), !replay.run || reduced),
          button('Previous operation', () => replayDispatch({ type: 'previous' }), !replay.step),
          button('Next operation', () => replayDispatch({ type: 'next' }), !replay.run || replay.step >= replay.run.operations.length),
          button('Restart replay', () => replayDispatch({ type: 'restart' }), !replay.run), button('Cancel replay', cancel, !replay.run && !computing),
          e('label', null, t('Operations per second'), e('input', { type: 'number', 'aria-label': t('Operations per second'), min: 1, max: 60, value: replay.speed, onChange: event => replayDispatch({ type: 'speed', value: event.target.value }) }))),
        e('label', null, t('Operation timeline'), e('input', { className: 'ordering-scrubber', type: 'range', min: 0, max: replay.run?.operations.length || 0, value: replay.step,
          disabled: !replay.run, 'aria-label': t('Operation timeline'), onChange: event => replayDispatch({ type: 'seek', step: Number(event.target.value) }) })),
        e('output', { 'data-replay-step': replay.step }, `${replay.step} / ${replay.run?.operations.length || 0}`),
        reduced ? e('p', { className: 'muted-line' }, t('Reduced motion: use operation steps or the timeline.')) : null);
    }
    function compactTimeline(order, label, visual = null) {
      const start = currentPage * PAGE, ids = order.slice(start, start + PAGE), active = new Set(visual?.operation?.ids || []);
      const validation = O.validate(model, order), conflictIds = new Set(validation.diagnostics.filter(item => item.code !== 'invalid-permutation').flatMap(item => item.segmentIds));
      return e('section', { className: 'ordering-comparison-column', key: label, 'aria-label': t(label) }, e('h3', null, t(label)),
        e('div', { className: 'ordering-scroll', tabIndex: 0, 'aria-label': `${t(label)}: ${t('Layout position')}` },
          e('div', { className: 'ordering-grid', style: { gridTemplateColumns: `64px repeat(${ids.length}, 116px)` } },
            e('span', { className: 'ordering-cell ordering-track-corner' }, 'TT'), ids.map((id, i) => e('span', { className: 'ordering-cell', key: `pos${i}` }, start + i + 1)),
            [...new Set(ids.map(id => model.byId[id]?.track_id || t('Unavailable')))].flatMap(track => [e('strong', { key: track, className: 'ordering-cell' }, track),
              ...ids.map((id, i) => { const segment = model.byId[id], index = start + i, moved = originalPositions.get(id) !== index + 1;
                const sorted = visual?.operation?.type === 'complete' || index < (visual?.visual.sortedUntil || 0) || index >= (visual?.visual.sortedFrom ?? Infinity);
                return e('div', { className: 'ordering-cell', key: `${track}:${i}` }, segment?.track_id === track ? e('button', { type: 'button',
                  className: `ordering-block ${selected?.id === id ? 'selected' : ''} ${active.has(id) ? visual?.operation?.type === 'compare' ? 'comparing' : 'moving' : ''} ${sorted ? 'sorted' : ''} ${conflictIds.has(id) ? 'conflict' : ''} ${visual?.visual.pivot === id ? 'pivot' : ''} ${moved ? 'moved' : ''}`,
                  'data-ts-id': id, 'data-track-id': segment.track_id, 'aria-pressed': selected?.id === id, onClick: () => select(id),
                  title: `${id} · ${track} · ${t('Original position')} ${originalPositions.get(id)} → ${index + 1}` },
                  id, unresolvedIds.has(id) ? e('span', { title: t('Order unresolved'), 'aria-label': t('Order unresolved') }, ' ?') : null,
                  e('small', null, `${originalPositions.get(id)} → ${index + 1}`)) : null); })]))),
        ids.length ? e('p', { className: validation.valid ? 'ordering-success' : 'ordering-error' },
          visual && validation.diagnostics.some(item => item.code === 'invalid-permutation') ? t('Intermediate working array; temporal validation applies to complete permutations.')
            : `${t('Temporal compatibility')}: ${validation.valid ? t('Compatible') : t('Conflict')}`) : null,
        !ids.length ? e('p', null, t('No suggested order yet.')) : null);
    }
    function replayContent() {
      const visual = frame?.visual;
      return e(R.Fragment, null, playbackControls(),
        e('div', { className: 'ordering-pagination' }, button('Previous positions', () => setPage(currentPage - 1), !currentPage), `${currentPage * PAGE + 1} / ${state.user.length}`,
          button('Next positions', () => setPage(currentPage + 1), (currentPage + 1) * PAGE >= state.user.length)),
        frame ? compactTimeline(frame.order, 'Algorithm replay', frame) : e('p', null, t('Build a trace to start replay.')),
        frame ? e('div', { className: 'ordering-structures', 'aria-label': t('Algorithm structures') },
          e('p', null, `${t('Operation')}: ${frame.operation?.type || t('Initial arrangement')}`),
          visual.pivot ? e('p', null, `${t('Pivot')}: ${visual.pivot} · ${t('Partition')}: ${visual.range ? `${visual.range[0] + 1}–${visual.range[1]}` : t('Unavailable')}`) : null,
          visual.heapEnd != null ? e('p', null, `${t('Heap boundary')}: ${visual.heapEnd}`) : null,
          visual.gap != null ? e('p', null, `${t('Gap')}: ${visual.gap}`) : null,
          visual.buffer ? e('p', null, `${t('Merge buffer')}: ${visual.buffer.join(', ')} (${visual.bufferSize})`) : null,
          visual.digit ? e('p', null, `${t('Digit place')}: ${visual.digit}`) : null,
          visual.counts ? e('details', { open: true }, e('summary', null, t('Counts')), e('pre', null, Object.entries(visual.counts).slice(0, 40).map(([key, count]) => `${key}: ${count}`).join(' · '))) : null,
          visual.buckets ? e('details', { open: true }, e('summary', null, t('Buckets')), Object.entries(visual.buckets).slice(0, 40).map(([bucket, value]) => e('p', { key: bucket }, `${bucket}: ${value.ids.join(', ')} (${value.size})`))) : null,
          e('small', null, t('Auxiliary previews show up to 40 entries; buffered writes may temporarily repeat a TS in the main array.')),
          e('h4', null, t('Operation history')),
          e('ol', { className: 'ordering-history', start: Math.max(0, replay.step - 20) + 1 }, replay.run.operations.slice(Math.max(0, replay.step - 20), replay.step).map((operation, i) =>
            e('li', { key: i }, button(`${Math.max(0, replay.step - 20) + i + 1}: ${operation.type} ${(operation.ids || []).join(', ')}`,
              () => replayDispatch({ type: 'seek', step: Math.max(0, replay.step - 20) + i + 1 })))))) : null);
    }
    function statisticsTable() {
      const headers = ['Algorithm', 'Keys', 'Comparisons', 'Swaps', 'Writes', 'Movements', 'Auxiliary slots', 'Operations', 'Computation ms', 'Trace overhead ms', 'Checkpoint ms', 'Sorting correct', 'Temporal compatibility'];
      return e('div', { className: 'ordering-scroll' }, e('table', { className: 'ordering-statistics' }, e('caption', null, t('Algorithm statistics')),
        e('thead', null, e('tr', null, headers.map(label => e('th', { key: label, scope: 'col' }, t(label))))),
        e('tbody', null, statistics.map(run => e('tr', { key: run.algorithm },
          [S.catalog.find(item => item.id === run.algorithm).label, run.basis, run.metrics.comparisons, run.metrics.swaps, run.metrics.writes, run.metrics.movements,
            run.metrics.auxiliaryMemory, run.metrics.operationCount, run.metrics.computationTime.toFixed(3), run.metrics.traceGenerationOverhead.toFixed(3), run.metrics.checkpointTime.toFixed(3),
            run.correctness ? t('Pass') : t('Fail'), run.temporal.valid ? t('Compatible') : t('Conflict')].map((value, i) => e('td', { key: i }, String(value))))))));
    }
    return e('section', { className: 'ordering-workbench', 'aria-label': t('Ordering Workbench'), onKeyDown: event => {
      if (!active || !(event.ctrlKey || event.metaKey)) return;
      const key = event.key.toLowerCase(), editing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(event.target.tagName);
      if (key === 's') { event.preventDefault(); save(); }
      else if (!editing && (key === 'z' || key === 'y')) { event.preventDefault(); dispatch({ type: key === 'y' || event.shiftKey ? 'redo' : 'undo' }); }
    } },
      e('div', { className: 'ordering-modes', role: 'group', 'aria-label': t('Ordering modes') },
        ['Arrange', 'Replay', 'Compare'].map(value => button(value, () => chooseMode(value), false, { key: value, 'aria-pressed': mode === value }))),
      e('p', { className: 'muted-line' }, t('Display preferences do not establish chronology or modify accepted annotations.')),
      e('div', { className: 'ordering-toolbar' },
        button('Undo', () => dispatch({ type: 'undo' }), !state.past.length), button('Redo', () => dispatch({ type: 'redo' }), !state.future.length),
        button('Reset to original', () => dispatch({ type: 'reset' })), button('Save Arrangement', save, !O.validate(model, state.user).valid), button('Restore saved', restore)),
      e('div', { role: 'status', className: 'ordering-message', 'aria-live': 'polite' }, t(session.message || state.notice || '')),
      model.errors.length ? e('p', { role: 'alert', className: 'ordering-error' }, t('Accepted temporal constraints are inconsistent. Movement is blocked.')) : null,
      model.withheld.length ? e('p', { className: 'muted-line' }, t('Withheld source conflicts: {count}', { count: model.withheld.length })) : null,
      model.warnings.length ? e('p', { role: 'status' }, model.warnings.map(item => item.message).join(' ')) : null,
      mode !== 'Arrange' ? e('section', { className: 'ordering-sort-controls', 'aria-label': t('Prepared ordering keys') },
        e('div', { className: 'ordering-toolbar' },
          e('label', null, t('Algorithm'), e('select', { 'aria-label': t('Algorithm'), value: algorithm, onChange: event => { cancel(); setAlgorithm(event.target.value); } }, S.catalog.map(item => e('option', { key: item.id, value: item.id }, item.label)))),
          e('label', null, t('Ordering keys'), e('select', { 'aria-label': t('Ordering keys'), value: basis, onChange: event => { cancel(); setBasis(event.target.value); } },
            e('option', { value: 'text_order' }, t('Narrative text_order')), e('option', { value: 'story_order' }, t('Partial-order layout story_order')), e('option', { value: 'custom' }, t('Custom numeric keys')))),
          button(computing ? 'Building trace…' : 'Build trace', generateTrace, computing || !state.user.length), button('Cancel computation', cancel, !computing),
          button('Apply Suggested Order', () => dispatch({ type: 'apply' }), !state.suggested?.validation.valid || !state.suggested?.correctness)),
        e('p', { className: 'muted-line' }, t('Input: User Adjusted. Equal keys retain input order. Numeric sorting creates no temporal evidence.')),
        e('small', null, t('Each trace captures User Adjusted and prepared keys when generated.')),
        basis === 'custom' ? e('label', null, t('Prepared keys as JSON'), e('textarea', { 'aria-label': t('Prepared keys as JSON'), value: customKeys, onChange: event => { cancel(); setCustomKeys(event.target.value); }, rows: 5 })) : null,
        e('p', null, `${t('Key compatibility')}: ${S.catalog.find(item => item.id === algorithm).keys}`),
        sortError ? e('p', { role: 'alert', className: 'ordering-error' }, sortError) : null,
        state.suggested ? e('p', { className: state.suggested.validation.valid ? 'ordering-success' : 'ordering-error', 'data-suggestion-result': true },
          `${t('Algorithm Suggested')}: ${state.suggested.algorithm} · ${t('Sorting correct')}: ${state.suggested.correctness ? t('Pass') : t('Fail')} · ${t('Temporal compatibility')}: ${state.suggested.validation.valid ? t('Compatible') : t('Conflict')}`,
          !state.suggested.validation.valid ? e('span', null, ' · ', state.suggested.validation.diagnostics.slice(0, 5).map(item => item.message).join(' ')) : null) : null) : null,
      e('div', { className: 'ordering-layout' },
        mode === 'Arrange' ? e('div', { className: 'ordering-main' },
          e('h3', null, t('User Adjusted')), e('strong', null, t('Layout position')),
          e('div', { className: 'ordering-pagination' }, button('Previous positions', () => setPage(currentPage - 1), currentPage === 0),
            e('span', null, `${currentPage * PAGE + (visible.length ? 1 : 0)}–${currentPage * PAGE + visible.length} / ${state.user.length}`),
            button('Next positions', () => setPage(currentPage + 1), (currentPage + 1) * PAGE >= state.user.length)),
          e('div', { className: 'ordering-scroll', tabIndex: 0, 'aria-label': t('Track layout, horizontally scrollable') },
            e('div', { className: 'ordering-grid', style: { gridTemplateColumns: `64px repeat(${visible.length}, 116px)` } },
              e('span', { className: 'ordering-cell ordering-group-label', style: { gridRow: 1, gridColumn: 1 } }, t('Groups')),
              state.groups.filter(group => group.start <= currentPage * PAGE + visible.length && group.end > currentPage * PAGE).map(group => {
                const start = Math.max(group.start, currentPage * PAGE + 1), end = Math.min(group.end, currentPage * PAGE + visible.length);
                return e('button', { key: `group-${group.id}`, className: 'ordering-group-band', type: 'button', 'data-group': group.id,
                  style: { gridRow: 1, gridColumn: `${start - currentPage * PAGE + 1} / span ${end - start + 1}` },
                  onClick: () => chooseGroup(group.id), title: `${t('Display group')} ${group.id}: ${group.start}–${group.end}` }, `${t('Display group')} ${group.id}`);
              }),
              e('span', { className: 'ordering-cell ordering-track-corner' }, 'TT'), visible.map((id, index) => e('span', { className: 'ordering-cell', key: `p${id}` }, currentPage * PAGE + index + 1)),
              [...new Set(visible.map(id => model.byId[id]?.track_id || t('Unavailable')))].flatMap(track => [e('strong', { key: track, className: 'ordering-cell' }, track),
                ...visible.map((id, index) => { const segment = model.byId[id], destination = currentPage * PAGE + index + 1;
                  const unresolved = unresolvedIds.has(id);
                  return e('div', { key: `${track}:${id}`, className: `ordering-cell ordering-drop ${drag ? destinations.get(destination) ? 'valid' : 'invalid' : ''} ${hover?.position === destination ? 'hovered' : ''}`,
                    'data-position': destination, title: hover?.position === destination && !hover.valid ? hover.diagnostics.map(item => item.message).join(' ') : undefined,
                    onDragOver: event => { if (!drag) return; event.preventDefault(); event.dataTransfer.dropEffect = destinations.get(destination) ? 'move' : 'none'; setHover({ ...O.previewGroupMove(model, state, drag, destination, dragGroup.current), position: destination }); },
                    onDrop: event => { event.preventDefault(); const dragged = event.dataTransfer.getData('text/tym-segment'); if (dragged === drag) move(dragged, destination, false, dragGroup.current); setDrag(null); setHover(null); } },
                    segment && (segment.track_id || t('Unavailable')) === track ? e('button', { type: 'button', draggable: true,
                      className: `ordering-block ${selected?.id === id ? 'selected' : ''} ${related.has(id) ? 'related' : ''} ${conflicts.has(id) ? 'conflict' : ''}`,
                      'data-ts-id': id, 'data-track-id': segment.track_id, 'data-group': O.groupAt(state, destination)?.id, 'aria-pressed': selected?.id === id,
                      'aria-label': `${id}, ${track}, ${t('Layout position')} ${destination}`, title: `${id} · ${track}`, onClick: () => select(id),
                      onKeyDown: event => { if (['ArrowLeft', 'ArrowRight'].includes(event.key)) {
                        event.preventDefault(); const group = selected?.id === id ? targetGroup : O.groupAt(state, destination);
                        const position = event.ctrlKey || event.metaKey ? event.key === 'ArrowLeft' ? group.start : group.end : destination + (event.key === 'ArrowLeft' ? -1 : 1);
                        dispatch({ type: 'select', id }); move(id, position, true, group?.id);
                      } },
                      onDragStart: event => { dragGroup.current = selected?.id === id ? targetGroup?.id : O.groupAt(state, destination)?.id; setDrag(id); if (selected?.id !== id) select(id); event.dataTransfer.setData('text/tym-segment', id); event.dataTransfer.effectAllowed = 'move'; },
                      onDragEnd: () => { setDrag(null); setHover(null); } }, e('strong', null, id), unresolved ? e('span', { title: t('Order unresolved'), 'aria-label': t('Order unresolved') }, ' ?') : null,
                        e('small', { className: 'ordering-excerpt' }, allowText ? segment.text?.slice(0, 70) || t('Unavailable') : t('Unavailable'))) : null);
                })]))),
          e('details', { className: 'ordering-original' }, e('summary', null, t('Original source sequence')),
            e('p', null, state.original.slice(currentPage * PAGE, (currentPage + 1) * PAGE).join(' → ')), e('small', null, t('Original source order is immutable; the same position page is shown.'))),
          e('div', { className: 'ordering-diagnostics', role: diagnostics.valid ? 'status' : 'alert' },
            diagnostics.valid ? t('Proposed arrangement is temporally compatible.') : diagnostics.diagnostics.slice(0, 30).map((item, i) => e('p', { key: i },
              item.message, ' ', item.relations.map(rel => `${rel.type}: ${rel.provenance?.evidence || t('Unavailable')} (${rel.provenance?.source || t('Unavailable')})`).join('; ')))),
          !state.user.length ? e('p', null, t('Generate a diagram to arrange its segments.')) : null) : null,
        mode === 'Replay' ? e('div', { className: 'ordering-main' }, replayContent()) : null,
        mode === 'Compare' ? e('div', { className: 'ordering-main' },
          e('div', { className: 'ordering-pagination' }, button('Previous positions', () => setPage(currentPage - 1), !currentPage), `${currentPage * PAGE + 1} / ${state.user.length}`,
            button('Next positions', () => setPage(currentPage + 1), (currentPage + 1) * PAGE >= state.user.length)),
          e('label', null, e('input', { type: 'checkbox', checked: syncReplay, onChange: event => setSyncReplay(event.target.checked) }), t('Show replay step in comparison')),
          syncReplay ? playbackControls() : null,
          e('div', { className: 'ordering-comparison' }, compactTimeline(state.original, 'Original'), compactTimeline(syncReplay && frame ? frame.order : state.suggested?.order || [], 'Algorithm Suggested', syncReplay ? frame : null), compactTimeline(state.user, 'User Adjusted')),
          statisticsTable(), e('p', { className: 'muted-line' }, t('Times exclude playback. Trace overhead is a noisy traced-minus-untraced estimate. Auxiliary memory is peak logical slots; trace storage is separate.')),
          e('p', null, `${t('Original')}: ${O.validate(model, state.original).valid ? t('Compatible') : t('Conflict')} · ${t('User Adjusted')}: ${O.validate(model, state.user).valid ? t('Compatible') : t('Conflict')}`)) : null,
        e('aside', { className: 'ordering-inspector', 'aria-label': t('Selected segment inspector') },
          e('h3', null, t('Selected segment'), selected ? ` — ${selected.id}` : ''),
          e('div', { className: 'ordering-inspector-tabs', role: 'group', 'aria-label': t('Inspector sections') }, ['Overview', 'Events', 'Relationships', 'Evidence'].map(tab =>
            button(tab, () => setInspectorTab(tab), false, { key: tab, 'aria-pressed': inspectorTab === tab }))), selected ? e(R.Fragment, null,
            e('dl', { className: 'segment-properties', hidden: inspectorTab !== 'Overview' }, property('TS ID', selected.id), property('TT ID', selected.track_id),
              property('Text excerpt', allowText ? selected.text?.slice(0, 800) : null), property('Source offsets', Number.isInteger(selected.span_start) && Number.isInteger(selected.span_end) ? `${selected.span_start}–${selected.span_end} (UTF-16)` : null),
              property('Original position', state.original.indexOf(selected.id) + 1), property('Current position', state.user.indexOf(selected.id) + 1),
              property('Temporal reference', selected.temporal_anchor), property('Events', selected.event_ids?.join(', ')), property('Participants', selected.actors?.join(', '))),
            mode !== 'Arrange' ? e('dl', { className: 'segment-properties' }, property('Suggested position', state.suggested ? state.suggested.order.indexOf(selected.id) + 1 : null),
              property('Prepared key', replay.run?.keys[selected.id]), property('Trace input position', replay.run ? replay.run.initial.indexOf(selected.id) + 1 : null),
              property('Replay position', frame ? frame.order.includes(selected.id) ? frame.order.indexOf(selected.id) + 1 : t('Auxiliary buffer') : null)) : null,
            e('div', { className: 'ordering-assignment' }, e('label', { htmlFor: 'ordering-group' }, t('Assign to display group')),
              e('select', { id: 'ordering-group', value: targetGroup?.id || '', onChange: event => chooseGroup(event.target.value) }, state.groups.map(group =>
                e('option', { key: group.id, value: group.id }, `${t('Display group')} ${group.id} (${group.start}–${group.end})`))),
              e('small', null, t('Choose a group, then confirm a position. TT membership stays locked.'))),
            e('label', { htmlFor: 'ordering-position' }, t('Preferred position')),
            e('div', { className: 'ordering-toolbar' }, button('Move left', () => move(selected.id, state.user.indexOf(selected.id)), !O.previewGroupMove(model, state, selected.id, state.user.indexOf(selected.id), targetGroup?.id).valid),
              e('input', { id: 'ordering-position', type: 'number', min: 1, max: state.user.length, value: position, onChange: event => editPosition(event.target.value), 'aria-invalid': !preview?.valid }),
              button('Move right', () => move(selected.id, state.user.indexOf(selected.id) + 2), !O.previewGroupMove(model, state, selected.id, state.user.indexOf(selected.id) + 2, targetGroup?.id).valid),
              button('Move to position', () => move(selected.id, Number(position)), !preview?.valid)),
            inspectorTab === 'Events' ? e('div', { className: 'ordering-event-details' }, e('h4', null, t('Events')), (selected.event_ids || []).map(id => {
              const event = (model.data.events || []).find(item => item.id === id);
              return e('p', { key: id }, e('strong', null, id), ' · ', allowText ? event?.text || t('Unavailable') : t('Unavailable'));
            })) : null,
            ['Relationships', 'Evidence'].includes(inspectorTab) ? e(R.Fragment, null, e('h4', null, t('Accepted relationships and provenance')),
            evidence.length ? e('ul', null, evidence.slice(0, 100).map((item, i) => e('li', { key: i },
              e('strong', null, `${item.from} ${item.type} ${item.to}`), e('p', null, `${t('Evidence')}: ${item.provenance.evidence || t('Unavailable')}`),
              e('p', null, `${t('Source')}: ${item.provenance.source || t('Unavailable')} · ${t('Cue')}: ${item.provenance.cue || t('Unavailable')}`))),
              evidence.length > 100 ? e('li', null, `${t('Relationships')}: 100 / ${evidence.length}`) : null) : e('p', null, t('Unavailable'))) : null,
            !diagnostics.valid ? e('div', { className: 'ordering-conflict-card', role: 'status' }, e('strong', null, t('Cannot move here')),
              e('p', null, diagnostics.diagnostics.slice(0, 3).map(item => item.message).join(' ')), button('View evidence', () => setInspectorTab('Evidence'))) : null
          ) : e('p', null, t('Select a segment.')))),
      e('div', { className: 'ordering-legend', 'aria-label': t('Layout legend') }, ['Comparing', 'Selected / Moving', 'Completed', 'Conflict', 'Unresolved'].map((label, i) => e('span', { key: label, 'data-semantic': i }, t(label)))),
      e('details', { className: 'ordering-guide' }, e('summary', null, t('Assignment workflow and keyboard shortcuts')),
        e('p', null, t('Groups describe position ranges; displaced items follow their new range.')),
        e('ol', null, ['Select a segment.', 'Choose a display group.', 'Preview a position or drag within the selected group.', 'Confirm the move; accepted temporal constraints are checked.'].map(label => e('li', { key: label }, t(label)))),
        e('p', null, t('Arrow keys: move. Ctrl+Arrow: group start/end. Ctrl+S: save. Ctrl+Z / Ctrl+Y: undo/redo.'))));
  }
  root.TymOrderingView = Workbench;
})(window);
