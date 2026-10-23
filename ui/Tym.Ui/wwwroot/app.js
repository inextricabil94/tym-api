const e = React.createElement;
const I18n = window.TymI18n;
const t = I18n.t;
const offline = window.TymOffline.create();

const apiDefault = (window.TYM_CONFIG && window.TYM_CONFIG.apiBaseUrl)
  || 'https://tym-api-serban.livelyrock-2726c024.eastus.azurecontainerapps.io';

const examples = [
  {
    id: 'en',
    label: 'English',
    language: 'en',
    highlight: 'Adam',
    text: 'Adam and Johan grew up in the same house. Years earlier, Karl had carried Adam through the rain. Margaret remembered her mother in Jakarta. Meanwhile, Karl traveled alone across the island. Adam searched for Margaret after the storm. Margaret found Adam at the doorway. Adam disappeared before dawn.'
  },
  {
    id: 'ro',
    label: 'Romanian',
    language: 'ro',
    highlight: 'Adam',
    text: 'Adam si Johan au crescut in aceeasi casa. Cu ani in urma, Karl l-a dus pe Adam prin ploaie. Margaret si-a amintit de mama ei in Jakarta. Intre timp, Karl a calatorit singur pe insula. Adam a cautat-o pe Margaret dupa furtuna. Margaret l-a gasit pe Adam la usa. Adam a disparut inainte de zori.'
  }
];

const privateExamples = window.TYM_CONFIG && window.TYM_CONFIG.examples;
if (Array.isArray(privateExamples)) {
  for (const example of privateExamples) {
    if (example && typeof example.id === 'string' && typeof example.label === 'string'
      && typeof example.text === 'string' && example.text.trim()
      && ['en', 'ro'].includes(example.language)
      && !examples.some(existing => existing.id === example.id)) {
      examples.push({ ...example, highlight: typeof example.highlight === 'string' ? example.highlight : '' });
    }
  }
}

const bookExamples = examples.filter(example => example.is_private);
const { splitChapters } = window.TymChapters;
const { partitionTracks } = window.TymTrackGroups;

function examplePicture(id) {
  if (id === 'en' || id === 'ro' || id.startsWith('tash-aw')) return {
    file: 'jakarta.jpg', alt: 'Jakarta skyline photographed in 2017', caption: 'Jakarta, 2017',
    credit: 'Georgi Kovachev', license: 'CC BY-SA 4.0',
    source: 'https://commons.wikimedia.org/wiki/File:JakartaSkyline.jpg',
    licenseUrl: 'https://creativecommons.org/licenses/by-sa/4.0/'
  };
  if (id === 'leghe') return { file: 'leagues-frontispiece.jpg', alt: 'Frontispiece of the 1871 Hetzel edition of Twenty Thousand Leagues Under the Seas',
    caption: 'Twenty Thousand Leagues, 1871', credit: 'Houghton Library, Harvard University', license: 'Public domain',
    source: 'https://commons.wikimedia.org/wiki/File:Houghton_FC8_V5946_869ve_-_Verne,_frontispiece.jpg' };
  if (id.startsWith('tinutul')) return { file: 'jules-verne.jpg', alt: 'Portrait of Jules Verne in 1892',
    caption: 'Jules Verne, 1892', credit: 'Ch. Herbert, Amiens', license: 'Public domain',
    source: 'https://commons.wikimedia.org/wiki/File:Jules_Verne_in_1892.jpg' };
  if (id === 'colind') return { file: 'christmas-carol.jpg', alt: 'Cover of the 1843 edition of A Christmas Carol',
    caption: 'A Christmas Carol, 1843', credit: 'John Leech / Wikimedia Commons', license: 'Public domain',
    source: 'https://commons.wikimedia.org/wiki/File:Christmascarol1843_--_Cover.jpg' };
  return null;
}

function themeIcon(dark) {
  return e('svg', { width: 18, height: 18, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor',
    strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round', 'aria-hidden': true }, dark
    ? [e('circle', { key: 'circle', cx: 12, cy: 12, r: 4 }),
      e('path', { key: 'rays', d: 'M12 2v2M12 20v2m4.93-17.07-1.42 1.42M6.49 17.51l-1.42 1.42M22 12h-2M4 12H2m17.07 7.07-1.42-1.42M6.49 6.49 5.07 5.07' })]
    : e('path', { d: 'M20.985 12.486a9 9 0 1 1-9.473-9.472c.405-.022.617.46.402.803a6.25 6.25 0 0 0 8.268 8.268c.344-.215.825-.004.803.401' }));
}

// Lucide chevron-left/right (ISC), as credited in THIRD_PARTY_NOTICES.md.
function chapterArrow(next) {
  return e('svg', { width: 18, height: 18, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor',
    strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round', 'aria-hidden': true },
    e('path', { d: next ? 'm9 18 6-6-6-6' : 'm15 18-6-6 6-6' }));
}

function count(value) {
  return Array.isArray(value) ? value.length : 0;
}

function metric(label, value) {
  return e('div', { className: 'metric', key: label },
    e('b', null, value),
    e('span', null, t(label))
  );
}

function parseSvgSize(svg) {
  const widthMatch = svg.match(/\swidth="([\d.]+)"/i);
  const heightMatch = svg.match(/\sheight="([\d.]+)"/i);

  return {
    width: widthMatch ? Number(widthMatch[1]) : 1200,
    height: heightMatch ? Number(heightMatch[1]) : 800
  };
}

function parseTrackSvg(svg) {
  if (!svg) return null;
  const root = new DOMParser().parseFromString(svg, 'image/svg+xml').documentElement;
  if (root.localName !== 'svg' || root.querySelector('parsererror')) return null;
  const tracks = new Map(Array.from(root.querySelectorAll('g.time-track[data-track-id]'))
    .map(track => [track.getAttribute('data-track-id'), track]));
  return tracks.size ? { root, tracks } : null;
}

function renderTrackPage(parsed, tracks) {
  if (!parsed) return '';
  const root = parsed.root.cloneNode(false);
  for (const child of parsed.root.children) {
    if (['rect', 'style', 'defs', 'metadata'].includes(child.localName)) root.appendChild(child.cloneNode(true));
  }
  let y = 24;
  for (const track of tracks) {
    const original = parsed.tracks.get(track.id);
    if (!original) continue;
    const group = original.cloneNode(true);
    group.setAttribute('transform', `translate(0 ${y})`);
    group.setAttribute('data-y', String(y));
    root.appendChild(group);
    y += Number(group.getAttribute('data-height')) + 16;
  }
  const legend = parsed.root.querySelector('g[data-section="legend"]');
  if (legend) {
    const group = legend.cloneNode(true);
    group.setAttribute('transform', `translate(0 ${y + 20})`);
    root.appendChild(group);
    y += Number(group.getAttribute('data-height')) + 20;
  }
  const width = Number(root.getAttribute('width'));
  const height = Math.ceil(y + 24);
  root.setAttribute('height', String(height));
  root.setAttribute('viewBox', `0 0 ${width} ${height}`);
  root.setAttribute('aria-label', t('TYM time tracks {ids}', { ids: tracks.map(track => track.id).join(', ') }));
  return new XMLSerializer().serializeToString(root);
}

function renderSegmentOverview(parsed) {
  const overview = parsed?.root.querySelector('g[data-section="text-order"]');
  if (!overview) return '';
  const root = parsed.root.cloneNode(false);
  for (const child of parsed.root.children) {
    if (['rect', 'style', 'defs', 'metadata'].includes(child.localName)) root.appendChild(child.cloneNode(true));
  }
  root.appendChild(overview.cloneNode(true));
  const width = Number(root.getAttribute('width'));
  const height = Number(overview.getAttribute('data-height')) || 158;
  root.setAttribute('height', String(height));
  root.setAttribute('viewBox', `0 0 ${width} ${height}`);
  root.setAttribute('aria-label', t('Time segments (TS)'));
  return new XMLSerializer().serializeToString(root);
}

function formatJson(value) {
  return value ? JSON.stringify(value, null, 2) : '';
}

function score(value) {
  const number = Number(value);
  return Number.isFinite(number) && number > 0 ? `${Math.round(number * 100)}%` : t('n/a');
}

function analysisValue(analysis, snakeName, camelName, fallback) {
  if (!analysis) {
    return fallback;
  }

  return analysis[snakeName] ?? analysis[camelName] ?? fallback;
}

function countEntries(value) {
  return Object.entries(value || {})
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));
}

function timeMlSummary(timeMl) {
  if (!timeMl) {
    return '';
  }

  const lines = [];
  lines.push('EVENT');
  (timeMl.events || []).forEach(item => {
    lines.push(`${item.id || item.eid || ''}  ${item.text || ''}  ${item.class || item.event_class || ''}`);
  });
  lines.push('');
  lines.push('TIMEX3');
  (timeMl.timex3 || []).forEach(item => {
    lines.push(`${item.id || item.tid || ''}  ${item.text || ''}  ${item.type || ''}  ${item.value || ''}`);
  });
  lines.push('');
  lines.push('TLINK');
  (timeMl.tlinks || []).forEach(item => {
    const from = item.event_instance_id || item.from_id || item.eventInstanceId || '';
    const to = item.related_to_event_instance || item.related_to_time || item.to_id || item.relatedToEventInstance || item.relatedToTime || '';
    lines.push(`${item.id || item.lid || ''}  ${from} -> ${to}  ${item.rel_type || item.relType || item.rel || ''}`);
  });

  return lines.join('\n').trim();
}

function App() {
  const { locale, setLocale } = I18n.useLocale();
  const [theme, setTheme] = React.useState(document.documentElement.dataset.theme);
  const [selectedExample, setSelectedExample] = React.useState(examples[0].id);
  const [sourceText, setSourceText] = React.useState(examples[0].text);
  const [language, setLanguage] = React.useState(examples[0].language);
  const [highlightEntity, setHighlightEntity] = React.useState(examples[0].highlight);
  const [entityOptions, setEntityOptions] = React.useState([]);
  const [loadingEntities, setLoadingEntities] = React.useState(false);
  const [entityError, setEntityError] = React.useState('');
  const [entityRefresh, setEntityRefresh] = React.useState(0);
  const [resolution, setResolution] = React.useState(() => window.TymViewport.measure(window.innerWidth, window.innerHeight));
  const diagramWidth = resolution.diagramWidth;
  const resolutionKey = React.useRef(resolution.key);
  const [trackDensity, setTrackDensity] = React.useState(50);
  const [segmentationAlgorithm, setSegmentationAlgorithm] = React.useState('article_rules');
  const [segmentPage, setSegmentPage] = React.useState(0);
  const [selectedSegmentId, setSelectedSegmentId] = React.useState('');
  const [bookSource, setBookSource] = React.useState(null);
  const [online, setOnline] = React.useState(navigator.onLine);
  const [savedDiagrams, setSavedDiagrams] = React.useState([]);
  const [savedSelection, setSavedSelection] = React.useState('');
  const [offlineStatus, setOfflineStatus] = React.useState('');
  const [restoredDiagram, setRestoredDiagram] = React.useState(null);
  const savedIdentity = React.useRef(null);
  const lastLibraryRefresh = React.useRef(0);
  const libraryApi = React.useRef('');
  const [apiBaseUrl, setApiBaseUrl] = React.useState(apiDefault);
  const account = window.TymAccounts.useAccount(apiBaseUrl);
  const [showAccount, setShowAccount] = React.useState(() => new URLSearchParams(window.location.hash.slice(1)).has('resetCode'));
  const [accountDialogVersion, setAccountDialogVersion] = React.useState(0);
  const [baseResult, setBaseResult] = React.useState(null);
  const [cacheResolution, setCacheResolution] = React.useState('');
  const [cacheStatus, setCacheStatus] = React.useState('');
  const [viewerDensity, setViewerDensity] = React.useState(50);
  const generationVersion = React.useRef(0);
  const [activeTab, setActiveTab] = React.useState('diagram');
  const [fitToWidth, setFitToWidth] = React.useState(false);
  const [zoom, setZoom] = React.useState(1);
  const [trackQuery, setTrackQuery] = React.useState('');
  const [trackPage, setTrackPage] = React.useState(0);
  const [trackGroupCount, setTrackGroupCount] = React.useState(0);
  const [viewportWidth, setViewportWidth] = React.useState(800);
  const diagramArea = React.useRef(null);
  const [busy, setBusy] = React.useState(false);
  const [message, setMessage] = React.useState('');
  const [textScope, setTextScope] = React.useState('excerpt');
  const [sourceScope, setSourceScope] = React.useState('excerpt');
  const [loadingText, setLoadingText] = React.useState(false);
  const textRequest = React.useRef(null);
  const fullTextCache = React.useRef(null);
  const [chapterIndex, setChapterIndex] = React.useState(0);
  const [chapterDetection, setChapterDetection] = React.useState('auto');
  const [library, setLibrary] = React.useState([]);
  const [libraryError, setLibraryError] = React.useState('');
  const [loadingLibrary, setLoadingLibrary] = React.useState(false);
  const [libraryRefresh, setLibraryRefresh] = React.useState(0);
  const [selectedBookId, setSelectedBookId] = React.useState('');
  const [bookEntityRequest, setBookEntityRequest] = React.useState('');
  const selectedBook = library.find(book => book.id === selectedBookId);
  const getSections = React.useCallback(value => splitChapters(value, { standalone: chapterDetection }), [chapterDetection]);
  const sections = React.useMemo(() => selectedBook ? selectedBook.sections : getSections(sourceText), [selectedBook, sourceText, getSections]);
  const currentChapterIndex = Math.min(chapterIndex, Math.max(0, sections.length - 1));
  const selectedSection = textScope === 'chapters' ? sections[currentChapterIndex] : null;
  const text = selectedSection ? sourceText.slice(selectedSection.start, selectedSection.end) : sourceText;
  const chapterCount = sections.filter(section => section.kind === 'chapter').length;
  const selectedCharacters = selectedBook ? selectedSection?.character_count ?? selectedBook.character_count : text.length;
  const bookSelectionKey = selectedBook ? `${selectedBook.id}:${selectedSection?.id || 'full'}` : '';
  const selectedExampleItem = examples.find(example => example.id === selectedExample);
  const protectedSource = Boolean(restoredDiagram?.book || selectedBook || selectedExampleItem?.is_private);
  const picture = examplePicture(selectedBookId || selectedExample);

  React.useEffect(() => {
    const resetLink = () => {
      if (!new URLSearchParams(window.location.hash.slice(1)).has('resetCode')) return;
      setAccountDialogVersion(value => value + 1); setShowAccount(true);
    };
    window.addEventListener('hashchange', resetLink);
    return () => window.removeEventListener('hashchange', resetLink);
  }, []);

  React.useEffect(() => {
    const update = () => {
      const next = window.TymViewport.measure(window.innerWidth, window.innerHeight);
      if (next.key === resolutionKey.current) return;
      resolutionKey.current = next.key;
      generationVersion.current++;
      setResolution(next); setBaseResult(null); setCacheResolution('');
      setTrackPage(0); setMessage('');
    };
    window.addEventListener('resize', update);
    update();
    return () => window.removeEventListener('resize', update);
  }, []);

  React.useEffect(() => {
    const update = () => setOnline(navigator.onLine);
    window.addEventListener('online', update); window.addEventListener('offline', update);
    return () => { window.removeEventListener('online', update); window.removeEventListener('offline', update); };
  }, []);

  React.useEffect(() => {
    const request = new AbortController();
    const api = window.TymOffline.normalizeApi(apiBaseUrl);
    const force = lastLibraryRefresh.current !== libraryRefresh;
    const revision = offline.revision();
    lastLibraryRefresh.current = libraryRefresh;
    if (libraryApi.current !== api) { setLibrary([]); setSelectedBookId(''); libraryApi.current = api; }
    setLibraryError(''); setLoadingLibrary(true);
    (async () => {
      try {
        const cached = await offline.getCatalog(api);
        if (request.signal.aborted) return;
        if (cached) { setLibrary(cached.books); setLoadingLibrary(false); }
        if (!online) {
          if (!cached) throw I18n.error('No saved book library is available offline.');
          return;
        }
        if (cached && !force && Date.now() - cached.created < 300000) return;
        const response = await fetch(`${api}/v1/books`, { signal: request.signal, cache: 'no-store' });
        if (!response.ok) throw I18n.error('Book catalog unavailable (HTTP {status}).', { status: response.status });
        const data = await response.json();
        const books = window.TymOffline.catalog(data.books);
        if (!request.signal.aborted) {
          setLibrary(books);
          setSelectedBookId(current => books.some(book => book.id === current) ? current : '');
          await offline.putCatalog(api, books, revision);
        }
      } catch (error) { if (!request.signal.aborted) setLibraryError(I18n.describe(error)); }
      finally { if (!request.signal.aborted) setLoadingLibrary(false); }
    })();
    return () => request.abort();
  }, [apiBaseUrl, libraryRefresh, online]);

  React.useEffect(() => {
    let active = true;
    (async () => {
      if (account.config?.text_requires_login) await offline.removeBooks(apiBaseUrl);
      const entries = await offline.diagrams(apiBaseUrl, resolution.key);
      if (active) setSavedDiagrams(entries.filter(entry => !entry.book || account.config?.text_requires_login !== true));
    })();
    return () => { active = false; };
  }, [apiBaseUrl, resolution.key, account.config?.text_requires_login]);

  React.useEffect(() => () => textRequest.current?.abort(), []);
  React.useEffect(() => { window.TymTheme.set(theme); }, [theme]);
  React.useEffect(() => {
    const request = new AbortController();
    let active = true;
    if (!online) { setLoadingEntities(false); return () => request.abort(); }
    setEntityOptions([]);
    setEntityError('');
    if (!online || loadingText || (!selectedBook && !text.trim()) || !apiBaseUrl.trim()
      || (selectedBook && bookEntityRequest !== bookSelectionKey)) {
      setLoadingEntities(false);
      return () => request.abort();
    }
    setLoadingEntities(true);
    const timer = setTimeout(async () => {
      try {
        const endpoint = selectedBook ? `/v1/books/${encodeURIComponent(selectedBook.id)}/entities` : '/v1/entities';
        const response = await fetch(`${apiBaseUrl.trim().replace(/\/+$/, '')}${endpoint}`, {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(selectedBook ? { section_id: selectedSection?.id || null } : { text, language }), signal: request.signal, cache: 'no-store'
        });
        if (!response.ok) {
          let details = null;
          try { details = await response.json(); } catch {}
          throw response.status === 429 ? I18n.error('Entity lookup limit reached. Retry in a minute.')
            : details?.error ? I18n.apiError(details, response.status)
              : I18n.error('Entity lookup failed (HTTP {status}).', { status: response.status });
        }
        const data = await response.json();
        if (!Array.isArray(data.entities)) throw new Error('The API returned an invalid entity list.');
        if (!data.pos_filter || data.pos_filter.provider !== 'spaCy') throw new Error('The API did not return model-backed POS filtering.');
        const recognized = data.entities.filter(entity => entity && typeof entity.name === 'string'
          && entity.name.trim() && ['PERSON', 'LOCATION', 'ORGANIZATION', 'REFERENCE'].includes(entity.label)
          && Array.isArray(entity.parts_of_speech) && entity.parts_of_speech.length > 0
          && (entity.recognition_source === 'trained_ner'
            ? entity.parts_of_speech.some(pos => pos === 'PROPN' || pos === 'NOUN')
            : entity.parts_of_speech.every(pos => pos === (entity.label === 'REFERENCE' ? 'PRON' : 'PROPN'))));
        if (active) {
          setEntityOptions(recognized);
          setHighlightEntity(value => recognized.some(entity => entity.name === value) ? value : '');
        }
      } catch (error) {
        if (active && error.name !== 'AbortError') {
          setEntityError(error instanceof Error ? I18n.describe(error) : 'Entity lookup failed.');
          setHighlightEntity('');
        }
      } finally {
        if (active) setLoadingEntities(false);
      }
    }, 600);
    return () => { active = false; clearTimeout(timer); request.abort(); };
  }, [text, language, apiBaseUrl, loadingText, entityRefresh, selectedBook, bookEntityRequest, bookSelectionKey, online]);
  const selectedHighlight = entityOptions.some(entity => entity.name === highlightEntity) ? highlightEntity : '';

  React.useEffect(() => { generationVersion.current++; setBaseResult(null); setRestoredDiagram(null); setSavedSelection(''); savedIdentity.current = null; setSegmentPage(0); setSelectedSegmentId(''); },
    [text, language, diagramWidth, apiBaseUrl, selectedHighlight, bookSelectionKey, segmentationAlgorithm]);
  const currentResult = cacheResolution === resolution.key ? baseResult : null;
  const densityChoices = React.useMemo(() => window.TymDensityCache.choices(currentResult), [currentResult]);
  const result = React.useMemo(() => window.TymDensityCache.select(currentResult, viewerDensity), [currentResult, viewerDensity]);
  const densitySession = currentResult?.density_cache?.session_id;
  React.useEffect(() => {
    const identity = savedIdentity.current;
    if (!currentResult || !identity || identity.version !== generationVersion.current || identity.resolution !== resolution.key || restoredDiagram) return;
    if (identity.book && account.config?.text_requires_login !== false) return;
    let active = true;
    const timer = setTimeout(async () => {
      const outcome = await offline.putDiagram(identity.key, identity.api, identity.resolution, currentResult, identity, identity.revision);
      if (!active) return;
      setOfflineStatus(outcome.tooLarge ? 'Diagram exceeds the offline storage limit.'
        : outcome.saved ? outcome.persistent ? 'Saved offline' : 'Session cache only' : 'Offline storage unavailable');
      const entries = await offline.diagrams(identity.api, identity.resolution);
      if (active) setSavedDiagrams(entries);
    }, 150);
    return () => { active = false; clearTimeout(timer); };
  }, [currentResult, resolution.key, restoredDiagram, account.config?.text_requires_login]);
  React.useEffect(() => {
    setCacheStatus('');
    if (!densitySession || !online) return;
    const request = new AbortController();
    const version = generationVersion.current;
    const pending = currentResult.density_cache.pending_densities || [];
    (async () => {
      // Let the selected diagram paint before starting additional layouts.
      await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      try {
        for (const density of pending) {
          if (request.signal.aborted || generationVersion.current !== version) return;
          setCacheStatus(I18n.message('Caching density {density}...', { density }));
          const response = await fetch(`${apiBaseUrl.trim().replace(/\/+$/, '')}/v1/density-sessions/${encodeURIComponent(densitySession)}/${density}`,
            { signal: request.signal, cache: 'no-store' });
          if (!response.ok) throw new Error('Background cache paused. Current diagram remains available.');
          const data = await response.json();
          if (request.signal.aborted || generationVersion.current !== version) return;
          setBaseResult(current => current?.density_cache?.session_id === densitySession
            ? window.TymDensityCache.append(current, density, data.view) : current);
        }
        setCacheStatus('10 densities cached');
      } catch (error) { if (!request.signal.aborted) setCacheStatus(I18n.describe(error)); }
    })();
    return () => request.abort();
  }, [densitySession, resolution.key, apiBaseUrl, online]);
  const svg = result?.render?.svg || result?.renderings?.svg || '';
  const localizedSvg = React.useMemo(() => I18n.localizeSvg(svg, locale), [svg, locale]);
  const diagram = result && result.diagram ? result.diagram : {};
  const analysis = result && result.analysis ? result.analysis : null;
  const warnings = result && Array.isArray(result.warnings) ? result.warnings : [];
  const segmentation = result?.segmentation;
  const canReadBookSource = account.config?.text_requires_login === false || Boolean(account.user);
  const markedSource = restoredDiagram ? null : selectedBook
    ? canReadBookSource && bookSource?.key === bookSelectionKey ? bookSource.text : null : text;
  const markedPage = React.useMemo(() => window.TymSegmentation.sourcePage(markedSource, diagram.segments || [], segmentPage),
    [markedSource, diagram.segments, segmentPage]);
  const selectedSource = selectedBook ? selectedBook.source : selectedSection && selectedExampleItem
    ? `${selectedExampleItem.label}; ${selectedSection.label}; unlabeled local chapter`
    : sourceScope === 'excerpt' ? selectedExampleItem?.source : selectedExampleItem?.full_source;
  const selectedPreview = textScope === 'excerpt' ? selectedExampleItem?.preview_svg : null;
  const hasChapters = selectedBook ? sections.length > 0 : chapterCount > 0;
  const timeMl = diagram.time_ml || {};
  const parsedTrackSvg = React.useMemo(() => parseTrackSvg(localizedSvg), [localizedSvg]);
  const segmentOverviewSvg = React.useMemo(() => renderSegmentOverview(parsedTrackSvg), [parsedTrackSvg, locale]);
  const trackActors = React.useMemo(() => {
    const actors = new Map();
    for (const segment of diagram.segments || []) {
      if (!actors.has(segment.track_id)) actors.set(segment.track_id, new Set());
      for (const actor of segment.actors || []) actors.get(segment.track_id).add(actor);
    }
    return actors;
  }, [diagram.segments]);
  const filteredTracks = React.useMemo(() => (diagram.tracks || []).filter(track =>
    `${track.id} ${track.name} ${Array.from(trackActors.get(track.id) || []).join(' ')}`.toLocaleLowerCase()
      .includes(trackQuery.trim().toLocaleLowerCase())), [diagram.tracks, trackActors, trackQuery]);
  const pageSize = 12;
  const totalTracks = count(diagram.tracks);
  const effectiveGroupCount = Math.min(trackGroupCount, totalTracks);
  const trackGroups = React.useMemo(() => partitionTracks(effectiveGroupCount ? diagram.tracks : filteredTracks,
    effectiveGroupCount, pageSize), [diagram.tracks, filteredTracks, effectiveGroupCount]);
  const displayGroups = React.useMemo(() => {
    if (!effectiveGroupCount) return trackGroups;
    const matchingIds = new Set(filteredTracks.map(track => track.id));
    return trackGroups.map(group => ({ ...group, tracks: group.tracks.filter(track => matchingIds.has(track.id)) }))
      .filter(group => group.tracks.length > 0);
  }, [trackGroups, filteredTracks, effectiveGroupCount]);
  const trackPageCount = Math.max(1, displayGroups.length);
  const currentTrackPage = Math.min(trackPage, trackPageCount - 1);
  const currentGroup = displayGroups[currentTrackPage];
  const visibleTracks = React.useMemo(() => currentGroup?.tracks || [], [currentGroup]);
  const displaySvg = React.useMemo(() => parsedTrackSvg ? renderTrackPage(parsedTrackSvg, visibleTracks) : localizedSvg,
    [parsedTrackSvg, visibleTracks, localizedSvg, locale]);
  const svgSize = parseSvgSize(displaySvg);
  const displayZoom = fitToWidth ? viewportWidth / svgSize.width : zoom;

  React.useEffect(() => {
    const area = diagramArea.current;
    if (!area) return;
    const update = () => {
      const style = getComputedStyle(area);
      setViewportWidth(Math.max(1, area.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight)));
    };
    const observer = new ResizeObserver(update);
    observer.observe(area);
    update();
    return () => observer.disconnect();
  }, [svg, activeTab]);
  const jsonText = React.useMemo(() => protectedSource ? '' : formatJson(result), [result, protectedSource]);
  const xmlText = result && result.xml ? result.xml : '';
  const timeMlText = React.useMemo(() => timeMlSummary(timeMl), [timeMl]);
  const statusClass = busy || loadingText ? 'busy' : message ? 'error' : result ? 'ready' : '';
  const statusText = !online ? 'Offline' : loadingText ? 'Loading text' : busy ? 'Generating' : message ? 'Needs attention' : result ? 'Ready' : 'Idle';

  function selectDensity(density) {
    const choice = densityChoices.find(option => option.density === density);
    if (!choice) return;
    setViewerDensity(density);
    setTrackDensity(density);
    setTrackPage(0);
    setTrackGroupCount(value => Math.min(value, choice.track_count));
    diagramArea.current?.scrollTo(0, 0);
  }

  function clearResult() {
    generationVersion.current++;
    setMessage('');
    setBaseResult(null);
  }

  function selectChapter(index) {
    setChapterIndex(index);
    setBookEntityRequest('');
    clearResult();
  }

  function selectScope(scope) {
    if (scope === textScope) return;
    if (scope !== 'chapters' && selectedExampleItem?.full_text_url && scope !== sourceScope) {
      loadExample(selectedExampleItem, scope);
      return;
    }
    setTextScope(scope);
    setBookEntityRequest('');
    clearResult();
  }

  function editText(value) {
    const updated = selectedSection
      ? sourceText.slice(0, selectedSection.start) + value + sourceText.slice(selectedSection.end)
      : value;
    setSourceText(updated);
    if (selectedSection) {
      const updatedSections = getSections(updated);
      const index = updatedSections.findIndex(section => section.start <= selectedSection.start && section.end > selectedSection.start);
      setChapterIndex(Math.max(0, index));
      if (!updatedSections.length) setTextScope('full');
    } else {
      setChapterIndex(Math.max(0, getSections(updated).findIndex(section => section.kind === 'chapter')));
    }
    setSelectedExample('');
    clearResult();
  }

  async function loadExample(example, scope = example.full_text_url ? 'auto' : 'excerpt') {
    setSelectedBookId(''); setBookEntityRequest(''); setActiveTab('diagram');
    textRequest.current?.abort();
    const request = new AbortController();
    textRequest.current = request;
    setSelectedExample(example.id);
    setTextScope(scope === 'auto' ? 'full' : scope);
    setSourceScope(scope === 'excerpt' ? 'excerpt' : 'full');
    setSourceText('');
    setChapterIndex(0);
    setLanguage(example.language);
    setHighlightEntity(example.highlight);
    clearResult();

    if (scope === 'excerpt') {
      setLoadingText(false);
      setSourceText(example.text);
      return;
    }

    setLoadingText(true);
    try {
      let completeText = fullTextCache.current?.id === example.id ? fullTextCache.current.text : null;
      if (completeText === null) {
        const response = await fetch(example.full_text_url, { signal: request.signal, cache: 'no-store' });
        if (!response.ok) throw I18n.error('Full text could not be loaded (HTTP {status}).', { status: response.status });
        completeText = await response.text();
      }
      if (completeText.length !== example.full_text_characters) {
        throw new Error('Full text length differs from the imported source. Prepare the local examples again.');
      }
      if (textRequest.current === request) {
        const parts = getSections(completeText);
        fullTextCache.current = { id: example.id, text: completeText, hasChapters: parts.length > 0 };
        setSourceText(completeText);
        const useChapters = parts.length > 0 && (scope === 'chapters' || (scope === 'auto' && completeText.length > 20000));
        setTextScope(useChapters ? 'chapters' : 'full');
        setChapterIndex(Math.max(0, parts.findIndex(section => section.kind === 'chapter')));
      }
    } catch (error) {
      if (textRequest.current === request && error.name !== 'AbortError') setMessage(I18n.describe(error));
    } finally {
      if (textRequest.current === request) setLoadingText(false);
    }
  }

  function loadBook(book) {
    textRequest.current?.abort();
    textRequest.current = null;
    setLoadingText(false); setSelectedExample(''); setSourceText('');
    setSelectedBookId(book.id); setLanguage(book.language); setHighlightEntity('');
    setBookEntityRequest(''); setTextScope(book.sections.length ? 'chapters' : 'full');
    setSourceScope('full'); setChapterIndex(Math.max(0, book.sections.findIndex(section => section.kind === 'chapter')));
    setActiveTab('diagram'); clearResult();
  }

  async function generate(force = false) {
    const version = generationVersion.current;
    const requestedResolution = resolution.key;
    const trimmedApi = apiBaseUrl.trim().replace(/\/+$/, '');
    const trimmedText = text.trim();

    if (!trimmedApi || (!selectedBook && !trimmedText)) {
      setMessage('Provide an API URL and input text.');
      return;
    }

    const payload = {
      text,
      options: {
        layout: 'both',
        width: diagramWidth,
        track_density: trackDensity,
        segmentation_algorithm: segmentationAlgorithm,
        cache_track_densities: true,
        progressive_density_cache: true,
        highlight_entities: selectedHighlight ? [selectedHighlight] : [],
        include_debug: true,
        language
      }
    };
    if (selectedBook) {
      delete payload.text;
      payload.section_id = selectedSection?.id || null;
    }

    setBusy(true);
    setOfflineStatus('');
    setMessage('');

    try {
      const endpoint = selectedBook ? `/v1/books/${encodeURIComponent(selectedBook.id)}/diagrams` : '/v1/diagrams';
      const key = await window.TymOffline.identity(trimmedApi, payload, requestedResolution, selectedBook);
      if (generationVersion.current !== version || window.TymViewport.measure(window.innerWidth, window.innerHeight).key !== requestedResolution) return;
      const identity = { key, version, api: trimmedApi, resolution: requestedResolution, book: Boolean(selectedBook || selectedExampleItem?.is_private),
        label: selectedBook ? `${selectedBook.label}${selectedSection ? ' / ' + selectedSection.label : ''}` : t(selectedExampleItem?.label || 'Custom text'), revision: offline.revision() };
      const canReuse = force !== true && !(selectedBook && account.config?.text_requires_login === true);
      const previous = savedIdentity.current;
      // A pending disk write can contain fewer completed views than the live bundle.
      const live = canReuse && !restoredDiagram && previous?.key === key && previous.version === version
        && previous.revision === identity.revision
        && window.TymDensityCache.choices(currentResult).some(choice => choice.density === trackDensity) ? currentResult : null;
      savedIdentity.current = identity;
      const cached = canReuse && !live ? await offline.getDiagram(key) : null;
      let data = live || (cached && window.TymDensityCache.choices(cached.response).some(choice => choice.density === trackDensity) ? cached.response : null);
      if (live && (!identity.book || account.config?.text_requires_login === false))
        await offline.putDiagram(key, trimmedApi, requestedResolution, live, identity, identity.revision);
      if (!data && !online) throw I18n.error('No saved diagram matches these settings. Connect to generate it.');
      if (!data) {
        setBaseResult(null); setRestoredDiagram(null); setSavedSelection('');
        const response = await fetch(`${trimmedApi}${endpoint}`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        });

        if (!response.ok) {
          let apiError = null;

          try {
            apiError = await response.json();
          } catch {
            // The status code remains useful when a proxy returns a non-JSON error.
          }

          if (response.status === 429) {
            const retrySeconds = Number(apiError && apiError.retry_after_seconds)
              || Number(response.headers.get('Retry-After'));
            const retryHours = Number.isFinite(retrySeconds)
              ? Math.max(1, Math.ceil(retrySeconds / 3600))
              : null;
            throw retryHours ? I18n.error('Daily limit reached. Try again in about {count} hours.', { count: retryHours })
              : I18n.error('Daily limit reached. Try again later.');
          }

          throw apiError?.error ? I18n.apiError(apiError, response.status)
            : I18n.error('API returned HTTP {status}', { status: response.status });
        }

        data = await response.json();
      }
      if (generationVersion.current !== version
        || window.TymViewport.measure(window.innerWidth, window.innerHeight).key !== requestedResolution) return;
      if (segmentationAlgorithm !== 'article_rules' && data.segmentation?.algorithm !== segmentationAlgorithm)
        throw I18n.error('The API did not apply the selected segmentation algorithm.');
      if (data.segmentation?.unit !== 'temporal_development')
        throw I18n.error('The API does not provide temporal-development segmentation. Update the API.');
      const returnedSvg = (data.render && data.render.svg) || (data.renderings && data.renderings.svg) || '';

      if (!live) setBaseResult(data);
      setRestoredDiagram(null); setSavedSelection('');
      setOfflineStatus(live || (cached && data === cached.response) ? 'Loaded from offline cache' : '');
      setCacheResolution(requestedResolution);
      setViewerDensity(trackDensity);
      setActiveTab('diagram');
      setTrackQuery('');
      setTrackPage(0);
      setSegmentPage(0); setSelectedSegmentId('');
      setTrackGroupCount(value => Math.min(value, count(data.diagram?.tracks)));
      setFitToWidth(false);
      setZoom(1);

      if (!returnedSvg) {
        setMessage('The API returned JSON but no SVG rendering.');
      }
    } catch (error) {
      if (generationVersion.current !== version) return;
      setBaseResult(null);
      setMessage(error instanceof Error ? I18n.describe(error) : 'Diagram generation failed.');
    } finally {
      setBusy(false);
    }
  }

  async function restoreSaved(key) {
    const version = ++generationVersion.current;
    const saved = await offline.getDiagram(key);
    if (generationVersion.current !== version || !saved || saved.resolution !== resolution.key
      || saved.api !== window.TymOffline.normalizeApi(apiBaseUrl) || (saved.book && account.config?.text_requires_login === true)) return;
    savedIdentity.current = null;
    setRestoredDiagram({ book: saved.book }); setSavedSelection(key);
    setBaseResult(saved.response); setCacheResolution(resolution.key);
    setViewerDensity(saved.response.track_generation?.density ?? 50);
    setActiveTab('diagram'); setTrackQuery(''); setTrackPage(0); setSegmentPage(0);
    setSelectedSegmentId(''); setFitToWidth(false); setZoom(1); setMessage('');
    setOfflineStatus('Loaded from offline cache');
  }

  async function clearOffline() {
    await offline.clear(); savedIdentity.current = null; setSavedDiagrams([]); setSavedSelection('');
    setOfflineStatus('Offline cache cleared');
  }

  function downloadSvg() {
    if (!svg) {
      return;
    }

    const blob = new Blob([localizedSvg], { type: 'image/svg+xml' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'tym-diagram.svg';
    link.click();
    URL.revokeObjectURL(url);
  }

  function downloadText(filename, content, type) {
    if (!content) {
      return;
    }

    const blob = new Blob([content], { type });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.click();
    URL.revokeObjectURL(url);
  }

  function resultTab(id, label) {
    return e('button', {
      className: `tab ${activeTab === id ? 'active' : ''}`,
      onClick: () => setActiveTab(id),
      type: 'button'
    }, t(label));
  }

  function analysisCard(label, value) {
    return e('div', { className: 'analysis-card', key: label },
      e('span', null, t(label)),
      e('b', null, value)
    );
  }

  function countBlock(title, value) {
    const entries = countEntries(value);
    return e('section', { className: 'analysis-block', key: title },
      e('h3', null, t(title)),
      entries.length
        ? e('div', { className: 'count-grid' },
            entries.map(([label, countValue]) => e('span', { className: 'count-chip', key: label },
              e('b', null, countValue),
              t(label)
            ))
          )
        : e('p', { className: 'muted-line' }, t('No values.'))
    );
  }

  function sourceBlock() {
    const sources = analysisValue(analysis, 'extraction_sources', 'extractionSources', []);
    return e('section', { className: 'analysis-block' },
      e('h3', null, t('Extraction sources')),
      sources.length
        ? e('div', { className: 'source-list' }, sources.map(source => e('code', { key: source }, source)))
        : e('p', { className: 'muted-line' }, t('No sources.'))
    );
  }

  function issueBlock() {
    const issues = analysisValue(analysis, 'issues', 'issues', []);
    function issueText(issue) {
      const counted = {
        EVENTS_WITHOUT_ACTORS: I18n.message('Events without an explicit or carried actor: {count}.',
          { count: (diagram.events || []).filter(event => !event.actors?.length).length }),
        SEGMENTS_WITHOUT_TIMEX: I18n.message('Segments without an explicit temporal anchor: {count}.',
          { count: (diagram.segments || []).filter(segment => !segment.temporal_anchor).length })
      };
      return t(counted[issue.code] || issue.message || '');
    }
    const rows = issues.length
      ? issues.map(issue => e('li', { className: `issue ${issue.severity || ''}`, key: issue.code || issue.message },
          e('b', null, issue.code || 'ISSUE'),
          e('span', null, issueText(issue)),
          issue.evidence ? e('small', null, t(issue.evidence)) : null
        ))
      : [e('li', { className: 'issue info', key: 'ok' },
          e('b', null, 'NO_ANALYSIS_ISSUES'),
          e('span', null, t('No analysis issues were reported.')),
          null
        )];

    return e('section', { className: 'analysis-block' },
      e('h3', null, t('Analysis issues')),
      e('ul', { className: 'issue-list' }, rows),
      warnings.length
        ? e('div', { className: 'warnings' },
            e('h3', null, t('Warnings')),
            warnings.map(item => {
              const issue = issues.find(value => item === `${value.code}: ${value.message}`);
              return e('p', { key: item }, issue ? `${issue.code}: ${issueText(issue)}` : t(item));
            })
          )
        : null
    );
  }

  function dataTable(title, headers, rows, emptyText) {
    return e('section', { className: 'analysis-block wide', key: title },
      e('h3', null, t(title)),
      rows.length
        ? e('div', { className: 'table-scroll' },
            e('table', { className: 'data-table' },
              e('thead', null,
                e('tr', null, headers.map(header => e('th', { key: header }, t(header))))
              ),
              e('tbody', null,
                rows.map((row, rowIndex) => e('tr', { key: `${title}-${rowIndex}` },
                  row.map((cell, cellIndex) => e('td', { key: `${title}-${rowIndex}-${cellIndex}` }, cell || '-'))
                ))
              )
            )
          )
        : e('p', { className: 'muted-line' }, t(emptyText))
    );
  }

  function renderAnalysis() {
    if (!analysis) {
      return e('div', { className: 'empty' },
        e('div', null,
          e('strong', null, t('No analysis yet')),
          e('span', null, t('Generate a diagram to inspect extractor statistics.'))
        )
      );
    }

    const eventRows = (diagram.events || []).map(item => [
      item.id,
      t(item.temporal_category),
      t(item.relation_to_previous),
      item.relation_cue || '-',
      score(item.confidence),
      item.text
    ]);
    const segmentRows = (diagram.segments || []).map(item => [
      item.id,
      item.type,
      item.track_id,
      (item.actors || []).join(', '),
      item.temporal_anchor || '-',
      (item.event_ids || []).join(', '),
      score(item.confidence)
    ]);

    return e('div', { className: 'analysis-view' },
      e('div', { className: 'analysis-grid' },
        analysisCard('Characters', analysisValue(analysis, 'character_count', 'characterCount', 0)),
        analysisCard('Avg event confidence', score(analysisValue(analysis, 'average_event_confidence', 'averageEventConfidence', 0))),
        analysisCard('Avg TS confidence', score(analysisValue(analysis, 'average_segment_confidence', 'averageSegmentConfidence', 0))),
        analysisCard('TimeML events', analysisValue(analysis, 'time_ml_event_count', 'timeMlEventCount', 0)),
        analysisCard('TIMEX3', analysisValue(analysis, 'time_ml_timex_count', 'timeMlTimexCount', 0)),
        analysisCard('TLINKs', analysisValue(analysis, 'time_ml_tlink_count', 'timeMlTLinkCount', 0))
      ),
      e('div', { className: 'analysis-columns' },
        countBlock('Segment types', analysisValue(analysis, 'segment_types', 'segmentTypes', {})),
        countBlock('Temporal categories', analysisValue(analysis, 'temporal_categories', 'temporalCategories', {})),
        countBlock('Relation types', analysisValue(analysis, 'relation_types', 'relationTypes', {})),
        countBlock('Entity labels', analysisValue(analysis, 'entity_mention_labels', 'entityMentionLabels', {})),
        sourceBlock(),
        issueBlock()
      ),
      dataTable('Events', ['Event', 'Temporal', 'Rel prev', 'Cue', 'Confidence', 'Text'], eventRows, 'No events detected.'),
      dataTable('Segments', ['TS', 'Type', 'Track', 'Actors', 'Anchor', 'Events', 'Confidence'], segmentRows, 'No segments detected.')
    );
  }

  function renderSegmentation() {
    const segments = diagram.segments || [];
    if (!result || !segments.length) return e('div', { className: 'empty' }, t('No segments detected.'));
    if (!markedPage) return e('div', { className: 'empty' }, t('Source text is unavailable or its offsets do not match.'));
    const selected = markedPage.segments.find(segment => segment.id === selectedSegmentId) || markedPage.segments[0];
    const boundary = segmentation?.decisions?.find(decision => decision.segment_id === selected?.id && decision.is_boundary);
    const scoreKind = segmentation?.score_kind;
    const modelScore = boundary?.score == null ? t('n/a') : scoreKind === 'margin'
      ? Number(boundary.score).toFixed(3) : `${(Number(boundary.score) * 100).toFixed(1)}%`;
    const algorithm = window.TymSegmentation.algorithms.find(item => item.id === (segmentation?.algorithm || 'article_rules'));
    function showTrack() {
      setTrackQuery('');
      const groups = partitionTracks(diagram.tracks || [], effectiveGroupCount, pageSize);
      setTrackPage(Math.max(0, groups.findIndex(group => group.tracks.some(track => track.id === selected.track_id))));
      setActiveTab('diagram');
    }
    return e('section', { className: 'segmentation-view', 'aria-label': t('Marked segmentation') },
      e('div', { className: 'segmentation-heading' },
        e('div', null, e('h3', null, t(algorithm?.label || 'Temporal continuity rules')),
          e('span', { className: 'muted-line' }, segmentation?.training_examples
            ? t('Seed examples: {count} | Not corpus validated', { count: segmentation.training_examples })
            : t('Rule-based boundaries'))),
        e('div', { className: 'track-paging' },
          e('span', { className: 'track-range', 'aria-live': 'polite' }, t('Segments {start}-{end} of {total}',
            { start: markedPage.startIndex + 1, end: markedPage.startIndex + markedPage.segments.length, total: segments.length })),
          e('button', { className: 'tool', type: 'button', title: t('Previous segments'), 'aria-label': t('Previous segments'),
            disabled: markedPage.index === 0, onClick: () => setSegmentPage(markedPage.index - 1) }, chapterArrow(false)),
          e('button', { className: 'tool', type: 'button', title: t('Next segments'), 'aria-label': t('Next segments'),
            disabled: markedPage.index === markedPage.pages - 1, onClick: () => setSegmentPage(markedPage.index + 1) }, chapterArrow(true)))),
      e('div', { className: 'segment-type-legend', 'aria-label': t('Segment types') },
        ['NAR', 'REM', 'SUP', 'GEN', 'FIC'].map(type => e('span', { key: type, 'data-type': type }, type))),
      selected ? e('section', { className: 'segment-inspector', 'aria-label': t('Selected segment') },
        e('div', { className: 'segmentation-heading' }, e('strong', null, `${selected.id} / ${selected.type}`),
          e('button', { className: 'secondary segment-track-link', type: 'button', onClick: showTrack, title: t('Show time track') },
            selected.track_id, chapterArrow(true))),
        e('dl', { className: 'segment-properties' },
          e('div', null, e('dt', null, 'UTF-16'), e('dd', null, `[${selected.span_start}, ${selected.span_end})`)),
          e('div', null, e('dt', null, t('Events')), e('dd', null, (selected.event_ids || []).join(', ') || '-')),
          e('div', null, e('dt', null, t('Temporal context')), e('dd', { 'data-testid': 'temporal-frame' },
            (boundary?.frame || 'narrative').split('/').map(value => t(value)).join(' / '))),
          e('div', null, e('dt', null, t('Time reference')), e('dd', null, boundary?.time_reference || t('Unanchored'))),
          e('div', null, e('dt', null, t('Boundary basis')), e('dd', null, locale === 'en'
            ? (boundary?.basis || 'temporal_continuity').replaceAll('_', ' ') : t(boundary?.basis || 'temporal_continuity'))),
          e('div', null, e('dt', null, t(scoreKind === 'margin' ? 'Decision margin' : scoreKind === 'uncalibrated_probability' ? 'Model probability (uncalibrated)' : 'Boundary score')),
            e('dd', { 'data-testid': 'boundary-score' }, modelScore)),
          e('div', null, e('dt', null, t('Boundary evidence')), e('dd', null, (boundary?.evidence || [])
            .map(item => locale === 'en' ? item.replaceAll('_', ' ') : t(item)).join(', ') || '-')))) : null,
      e('div', { className: 'segmented-source', lang: language, tabIndex: 0, 'aria-label': t('Marked source text') },
        markedPage.parts.map((part, index) => part.kind === 'gap' ? e('span', { key: `gap-${index}`, className: 'source-gap' }, part.text)
          : e(React.Fragment, { key: part.segment.id },
            e('button', { type: 'button', className: 'segment-marker', 'aria-pressed': selected?.id === part.segment.id,
              title: t('Inspect {id}', { id: part.segment.id }), onClick: () => setSelectedSegmentId(part.segment.id) }, part.segment.id),
            e('mark', { className: `source-segment${selected?.id === part.segment.id ? ' selected' : ''}`,
              'data-segment-id': part.segment.id, 'data-type': part.segment.type }, part.text))))
    );
  }

  function renderResultBody() {
    if (activeTab === 'segmentation') return renderSegmentation();
    if (activeTab === 'json') {
      return e('pre', { className: 'code-view' }, jsonText || t('No JSON result yet.'));
    }

    if (activeTab === 'xml') {
      return e('pre', { className: 'code-view' }, xmlText || t('No XML result yet.'));
    }

    if (activeTab === 'timeml') {
      return e('pre', { className: 'code-view' }, timeMlText || t('No TimeML result yet.'));
    }

    if (activeTab === 'analysis') {
      return renderAnalysis();
    }

    return e('div', { className: 'diagram-view' },
      segmentOverviewSvg ? e('section', { className: 'segment-overview', 'aria-labelledby': 'segment-overview-title' },
        e('div', { className: 'diagram-section-header' },
          e('h3', { id: 'segment-overview-title' }, t('Time segments (TS)')),
          e('button', { type: 'button', className: 'secondary', onClick: () => setActiveTab('segmentation') }, t('Inspect segments'))),
        e('div', { className: 'segment-overview-area', role: 'region', tabIndex: 0, 'aria-label': t('Time segments (TS)') },
          e('div', { className: 'diagram-frame segment-overview-frame', lang: language,
            dangerouslySetInnerHTML: { __html: segmentOverviewSvg } }))) : null,
      e('section', { className: 'time-tracks-view', 'aria-labelledby': 'time-tracks-title' },
      e('div', { className: 'diagram-section-header' },
        e('h3', { id: 'time-tracks-title' }, t('Time tracks (TT)'))),
      parsedTrackSvg ? e('div', { className: 'track-toolbar' },
        e('input', {
          type: 'search', className: 'api-input track-search', 'aria-label': t('Filter time tracks'),
          placeholder: t('Track ID or character'), value: trackQuery,
          onChange: event => { setTrackQuery(event.target.value); setTrackPage(0); }
        }),
        e('div', { className: 'track-group-controls' },
          e('div', { className: 'field' },
            e('label', { htmlFor: 'track-groups' }, t('Display groups')),
            e('select', { id: 'track-groups', className: 'language-select', value: effectiveGroupCount,
              onChange: event => { setTrackGroupCount(Number(event.target.value)); setTrackPage(0); diagramArea.current?.scrollTo(0, 0); } },
              Array.from({ length: 11 }, (_, groups) => {
                const low = groups ? Math.floor(totalTracks / groups) : 0;
                const high = groups ? Math.ceil(totalTracks / groups) : 0;
                const size = low === high ? String(low) : `${low}-${high}`;
                return e('option', { key: groups, value: groups, disabled: groups > totalTracks },
                  groups === 0 ? t('0 - Paged (12 tracks)') : t('{groups} - {size} tracks/group', { groups, size }));
              })
            )
          ),
          effectiveGroupCount ? e('div', { className: 'field' },
            e('label', { htmlFor: 'track-group' }, t('Group')),
            e('select', { id: 'track-group', className: 'language-select', disabled: !currentGroup,
              value: currentGroup?.index ?? '', onChange: event => {
                setTrackPage(displayGroups.findIndex(group => group.index === Number(event.target.value)));
                diagramArea.current?.scrollTo(0, 0);
              } }, currentGroup
                ? displayGroups.map(group => e('option', { key: group.index, value: group.index },
                  `${group.index + 1}: ${diagram.tracks[group.start].id}-${diagram.tracks[group.end - 1].id} (${group.tracks.length})`))
                : e('option', { value: '' }, t('No matching groups')))
          ) : null
        ),
        e('div', { className: 'track-paging' },
          e('span', { className: 'track-range', 'aria-live': 'polite' }, currentGroup
            ? effectiveGroupCount
              ? `${t('Group {index} of {total}', { index: currentGroup.index + 1, total: trackGroups.length })} | ${t('Tracks {start}-{end} of {total}', { start: currentGroup.start + 1, end: currentGroup.end, total: totalTracks })}${trackQuery.trim() ? ` | ${t('{count} matching', { count: currentGroup.tracks.length })}` : ''}`
              : t('Tracks {start}-{end} of {total}', { start: currentGroup.start + 1, end: currentGroup.end, total: filteredTracks.length })
            : t('No matching tracks')),
          e('button', { type: 'button', className: 'tool', title: t('Previous tracks'), 'aria-label': t('Previous tracks'),
            disabled: currentTrackPage === 0, onClick: () => { setTrackPage(currentTrackPage - 1); diagramArea.current?.scrollTo(0, 0); } }, chapterArrow(false)),
          e('button', { type: 'button', className: 'tool', title: t('Next tracks'), 'aria-label': t('Next tracks'),
            disabled: currentTrackPage >= trackPageCount - 1, onClick: () => { setTrackPage(currentTrackPage + 1); diagramArea.current?.scrollTo(0, 0); } }, chapterArrow(true))
        )
      ) : null,
      e('div', { className: 'diagram-area', ref: diagramArea, role: 'region', tabIndex: 0, 'aria-label': t('Time tracks (TT)') },
      svg && (!parsedTrackSvg || visibleTracks.length > 0)
        ? e('div', {
            className: `diagram-frame ${fitToWidth ? 'fit' : ''}`,
            style: fitToWidth ? null : {
              width: `${svgSize.width * zoom}px`,
              height: `${svgSize.height * zoom}px`
            }
          },
            e('div', {
              className: 'diagram-scale',
              style: fitToWidth ? null : { transform: `scale(${zoom})` },
              lang: language, dangerouslySetInnerHTML: { __html: displaySvg }
            })
          )
        : e('div', { className: 'empty' },
            e('div', null,
              e('strong', null, t(svg ? 'No matching tracks' : 'No diagram yet'))
            )
          )
    )));
  }

  return e('main', { className: 'shell' },
    e('header', { className: 'topbar' },
      e('div', { className: 'brand' },
        e('strong', null, e('span', { className: 'brand-accent' }, 'TYM'), t(' Diagram UI')),
        e('span', null, t('Natural-language text to temporal-segment diagram'))
      ),
      e('div', { className: 'header-tools' },
      e('div', { className: 'locale-control' },
        e('div', { id: 'interface-language', className: 'locale-switch', role: 'group', 'aria-label': t('Interface language') },
          Object.entries(I18n.names).map(([value, name]) => e('button', {
            key: value, type: 'button', className: 'locale-button', lang: value, 'data-locale': value,
            'aria-pressed': locale === value, title: t(value === 'en' ? 'English' : 'Romanian'),
            'aria-label': t(value === 'en' ? 'English' : 'Romanian'),
            onClick: () => setLocale(value)
          }, e('img', { src: `/assets/flags/${value === 'en' ? 'gb' : 'ro'}.svg`, alt: '', width: 24, height: 18 }),
          e('span', { className: 'locale-name' }, name))))),
      e('div', { className: 'account-tools' },
        account.user ? e('span', { className: 'account-email', title: account.user.email }, account.user.email) : null,
        e('button', { className: 'secondary', type: 'button', onClick: () => account.user ? account.client.logout() : setShowAccount(true) },
          t(account.user ? 'Sign Out' : 'Sign In'))),
      e('div', { className: 'status' },
        e('span', { className: `status-dot ${statusClass}` }),
        e('span', null, t(statusText))
      ),
      e('button', { className: 'tool theme-toggle', type: 'button', title: t(theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'),
        'aria-label': t(theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'), 'aria-pressed': theme === 'light',
        onClick: () => setTheme(theme === 'dark' ? 'light' : 'dark') }, themeIcon(theme === 'dark')))
    ),
    showAccount ? e(window.TymAccounts.AccountDialog, { key: `${apiBaseUrl}:${accountDialogVersion}`, account, onClose: () => setShowAccount(false) }) : null,
    account.error ? e('p', { className: 'account-banner', role: 'status' }, t(account.error)) : null,
    e('section', { className: 'workspace' },
      e('section', { className: 'panel input-panel' },
        e('div', { className: 'panel-header' },
          e('h1', { className: 'panel-title' }, t('Input')),
          e('div', { className: 'tabs', role: 'tablist', 'aria-label': t('Examples') },
            examples.filter(example => !example.is_private).map(example => e('button', {
              key: example.id,
              className: `tab ${selectedExample === example.id ? 'active' : ''}`,
              title: example.source || t(example.label),
              disabled: busy,
              onClick: () => loadExample(example),
              type: 'button'
            }, t(example.label)))
          )
        ),
        e('div', { className: 'fields' },
          e('div', { className: 'field' },
            e('label', { htmlFor: 'hosted-book' }, t('Book library')),
            e('div', { className: 'entity-controls' },
              e('select', { id: 'hosted-book', className: 'language-select', value: selectedBookId,
                disabled: busy || loadingLibrary || !library.length,
                onChange: event => { const book = library.find(item => item.id === event.target.value); if (book) loadBook(book); } },
                e('option', { value: '', disabled: true }, t(loadingLibrary ? 'Loading library...' : 'Select a book')),
                library.map(book => e('option', { key: book.id, value: book.id }, book.label))),
              e('button', { className: 'tool entity-refresh', type: 'button', title: t('Refresh book library'), 'aria-label': t('Refresh book library'),
                disabled: busy || loadingLibrary, onClick: () => setLibraryRefresh(value => value + 1) },
                e('svg', { width: 18, height: 18, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2,
                  strokeLinecap: 'round', strokeLinejoin: 'round', 'aria-hidden': true },
                  e('path', { d: 'M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1.02 6.74 2.74L21 8M21 3v5h-5' })))),
            e('small', { className: `entity-status${libraryError ? ' error' : ''}`, role: 'status' },
              libraryError ? t(libraryError) : loadingLibrary ? t('Loading...') : t('{count} complete books', { count: library.length }))
          ),
          bookExamples.length && !library.length ? e('div', { className: 'field' },
            e('label', { htmlFor: 'book-example' }, t('Book example')),
            e('select', {
              id: 'book-example',
              className: 'language-select',
              disabled: busy,
              value: bookExamples.some(example => example.id === selectedExample) ? selectedExample : '',
              onChange: event => {
                const example = bookExamples.find(item => item.id === event.target.value);
                if (example) loadExample(example);
              }
            },
              e('option', { value: '', disabled: true }, t('Select a book')),
              bookExamples.map(example => e('option', { key: example.id, value: example.id }, example.label))
            )
          ) : null,
          picture ? e('figure', { className: 'example-visual' },
            e('img', { src: `/assets/examples/${picture.file}`, alt: t(picture.alt), width: 112, height: 112, decoding: 'async' }),
            e('figcaption', null, e('strong', null, t(picture.caption)),
              e('a', { href: picture.source, target: '_blank', rel: 'noopener noreferrer' }, picture.credit),
              picture.licenseUrl ? e('a', { href: picture.licenseUrl, target: '_blank', rel: 'noopener noreferrer' }, t(picture.license))
                : e('span', null, t(picture.license)))) : null,
          selectedBook || selectedExampleItem?.full_text_url || chapterCount > 0 ? e('div', { className: 'source-controls' },
            e('div', { className: 'tabs', role: 'radiogroup', 'aria-label': t('Text scope') },
              [['full', 'Full text'], ...(hasChapters ? [['chapters', 'Chapters']] : []),
                ...(selectedExampleItem?.full_text_url ? [['excerpt', 'Excerpt']] : [])].map(([scope, label]) => e('button', {
                key: scope,
                type: 'button',
                role: 'radio',
                'aria-checked': textScope === scope,
                className: `tab ${textScope === scope ? 'active' : ''}`,
                disabled: busy,
                title: t(label),
                onClick: () => selectScope(scope)
              }, t(label)))
            ),
            e('span', { className: 'text-count', 'aria-live': 'polite' }, loadingText
              ? t('Loading...')
              : selectedSection
                ? t('{selected} / {total} characters', { selected: I18n.number(selectedCharacters), total: I18n.number(selectedBook?.character_count ?? sourceText.length) })
                : t('{count} characters', { count: selectedCharacters }))
          ) : null,
          selectedSection ? e('div', { className: 'field chapter-field' },
            e('label', { htmlFor: 'chapter' }, selectedBook ? t('Chapter / Section ({count})', { count: sections.length }) : t('Chapter ({count} detected)', { count: chapterCount })),
            e('div', { className: 'chapter-controls' },
              e('select', { id: 'chapter', className: 'language-select', value: currentChapterIndex, disabled: busy || loadingText,
                onChange: event => selectChapter(Number(event.target.value)) },
                sections.map((section, index) => e('option', { key: index, value: index, title: section.label },
                  window.TymChapters.sectionLabel(section, index, locale, { useHeadingLanguage: false })))),
              e('button', { className: 'tool chapter-step', type: 'button', title: t('Previous chapter'), 'aria-label': t('Previous chapter'),
                disabled: busy || loadingText || currentChapterIndex === 0, onClick: () => selectChapter(currentChapterIndex - 1) }, chapterArrow(false)),
              e('button', { className: 'tool chapter-step', type: 'button', title: t('Next chapter'), 'aria-label': t('Next chapter'),
                disabled: busy || loadingText || currentChapterIndex === sections.length - 1, onClick: () => selectChapter(currentChapterIndex + 1) }, chapterArrow(true))
            ),
            e('small', { className: 'chapter-range', 'aria-live': 'polite' },
              t('Section {index} of {total} | UTF-16 [{start}, {end})', { index: currentChapterIndex + 1, total: sections.length, start: selectedSection.start, end: selectedSection.end }))
          ) : null,
          selectedBook ? e(window.TymAccounts.SourceReader, { account, book: selectedBook, section: selectedSection, language, onSource: setBookSource,
            onSignIn: () => setShowAccount(true) }) : null,
          !selectedBook ? e('div', { className: 'field' },
            e('label', { htmlFor: 'chapter-detection' }, t('Chapter detection')),
            e('select', { id: 'chapter-detection', className: 'language-select', value: chapterDetection,
              disabled: busy || loadingText, onChange: event => {
                setChapterDetection(event.target.value); setChapterIndex(0); clearResult();
              } },
              e('option', { value: 'auto' }, t('Auto')),
              e('option', { value: 'off' }, t('Prefixed headings only')),
              e('option', { value: 'always' }, t('Include short numbered sections')))
          ) : null,
          e('div', { className: 'field' },
            e('label', { htmlFor: 'api-url' }, t('API URL')),
            e('input', {
              id: 'api-url',
              className: 'api-input',
              value: apiBaseUrl,
              onChange: event => setApiBaseUrl(event.target.value),
              spellCheck: false
            })
          ),
          e('div', { className: 'field' },
            e('label', { htmlFor: 'language' }, t('Text language')),
            e('select', {
              id: 'language',
              className: 'language-select',
              value: language,
              disabled: Boolean(selectedBook),
              onChange: event => setLanguage(event.target.value)
            },
              e('option', { value: 'en' }, t('English')),
              e('option', { value: 'ro' }, t('Romanian'))
            )
          ),
          e('div', { className: 'field' },
            e('label', { htmlFor: 'segmentation-algorithm' }, t('Temporal segmentation algorithm')),
            e('select', { id: 'segmentation-algorithm', className: 'language-select', value: segmentationAlgorithm, disabled: busy,
              onChange: event => setSegmentationAlgorithm(event.target.value) },
              window.TymSegmentation.algorithms.map(algorithm => e('option', { key: algorithm.id, value: algorithm.id }, t(algorithm.label))))
          ),
          e('div', { className: 'field-grid' },
            e('div', { className: 'field' },
              e('label', { htmlFor: 'highlight' }, t('Highlight entity')),
              e('div', { className: 'entity-controls' },
              e('select', {
                id: 'highlight',
                className: 'language-select',
                value: selectedHighlight,
                disabled: busy || loadingText || loadingEntities || !entityOptions.length,
                'aria-describedby': 'entity-status',
                onChange: event => setHighlightEntity(event.target.value)
              },
                e('option', { value: '' }, t('No highlight')),
                ['PERSON', 'LOCATION', 'ORGANIZATION', 'REFERENCE'].map(label => {
                  const items = entityOptions.filter(entity => entity.label === label);
                  return items.length ? e('optgroup', { label: t(label === 'PERSON' ? 'People' : label === 'LOCATION' ? 'Places'
                    : label === 'ORGANIZATION' ? 'Organizations' : 'Unresolved references'), key: label },
                    items.map(entity => e('option', { key: `${label}:${entity.name}`, value: entity.name },
                      `${entity.name} (${entity.mention_count})${entity.reference_forms?.length ? ` - ${entity.reference_forms.join(', ')}` : ''}`))) : null;
                })
              ),
              e('button', { className: 'tool entity-refresh', type: 'button', title: t('Refresh entities'), 'aria-label': t('Refresh entities'),
                disabled: busy || loadingText || loadingEntities || (!selectedBook && !text.trim()),
                onClick: () => { setBookEntityRequest(bookSelectionKey); setEntityRefresh(value => value + 1); } },
                e('svg', { width: 18, height: 18, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2,
                  strokeLinecap: 'round', strokeLinejoin: 'round', 'aria-hidden': true },
                  e('path', { d: 'M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1.02 6.74 2.74L21 8' }),
                  e('path', { d: 'M21 3v5h-5' })))
              ),
              e('small', { id: 'entity-status', className: `entity-status${entityError ? ' error' : ''}`, role: 'status' },
                loadingEntities ? t('Recognizing entities...') : entityError ? t(entityError)
                  : selectedBook && bookEntityRequest !== bookSelectionKey ? t('Not requested for this section') : t('{count} entities detected', { count: entityOptions.length }))
            ),
            e('div', { className: 'field' },
              e('label', { htmlFor: 'width' }, t('Diagram width')),
              e('output', { id: 'width', className: 'resolution-output' }, `${diagramWidth}px`),
              e('small', { className: 'entity-status', 'data-testid': 'window-resolution' }, t('Window {width} x {height}', { width: resolution.width, height: resolution.height }))
            )
          ),
          e('div', { className: 'field' },
            e('label', { htmlFor: 'track-density' }, t('TT density {density}', { density: trackDensity })),
            e('input', { id: 'track-density', className: 'range-input', type: 'range', min: '10', max: '100', step: '10',
              value: trackDensity, disabled: busy,
              onChange: event => {
                const density = Number(event.target.value);
                setTrackDensity(density);
                if (densityChoices.some(choice => choice.density === density)) selectDensity(density);
              } }),
            e('div', { className: 'density-scale', 'aria-hidden': true },
              e('span', null, t('10 - Overview')), e('span', null, t('100 - Detailed')))
          ),
          !protectedSource ? e('div', { className: 'field' },
            e('label', { htmlFor: 'text' }, t('Text')),
            e('textarea', {
              id: 'text', lang: language,
              className: 'text-input',
              disabled: loadingText || busy,
              value: text,
              onChange: event => editText(event.target.value)
            }),
            selectedSource ? e('small', { className: 'example-source' }, selectedSource) : null,
            selectedPreview && !protectedSource ? e('details', { className: 'example-preview' },
              e('summary', null, t('Excerpt diagram preview')),
              e('img', {
                src: `data:image/svg+xml;charset=utf-8,${encodeURIComponent(selectedPreview)}`,
                alt: t('Generated TYM diagram for {title}', { title: t(selectedExampleItem.label) }),
                loading: 'lazy'
              })
            ) : null
          ) : null,
          e('div', { className: 'actions' },
            e('button', { className: 'primary', type: 'button', onClick: () => generate(), disabled: busy || loadingText || loadingEntities }, t(busy ? 'Generating...' : 'Generate diagram')),
            e('div', { className: 'download-actions' },
              e('button', { className: 'tool entity-refresh', type: 'button', title: t('Regenerate diagram'), 'aria-label': t('Regenerate diagram'),
                disabled: busy || loadingText || loadingEntities || !online, onClick: () => generate(true) },
                e('svg', { width: 18, height: 18, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2,
                  strokeLinecap: 'round', strokeLinejoin: 'round', 'aria-hidden': true },
                  e('path', { d: 'M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1.02 6.74 2.74L21 8M21 3v5h-5' }))),
              e('button', { className: 'secondary', type: 'button', onClick: downloadSvg, disabled: !svg }, 'SVG'),
              !protectedSource ? e('button', { className: 'secondary', type: 'button', onClick: () => downloadText('tym-diagram.json', jsonText, 'application/json'), disabled: !result }, 'JSON') : null,
              !protectedSource ? e('button', { className: 'secondary', type: 'button', onClick: () => downloadText('tym-diagram.xml', xmlText, 'application/xml'), disabled: !xmlText }, 'XML') : null
            )
          ),
          e('div', { className: 'message', role: 'status' }, t(message))
        )
      ),
      e('section', { className: 'panel result-panel' },
        e('div', { className: 'panel-header' },
          e('h2', { className: 'panel-title' }, t('Diagram')),
          result ? e('span', { className: 'status' }, result.model_version || '') : null
        ),
        e('div', { className: 'result-toolbar' },
          e('div', { className: 'diagram-cache-selectors' },
          e('div', { className: 'field density-cache-control offline-controls' },
            e('label', { htmlFor: 'saved-diagram' }, t('Saved diagrams')),
            e('div', { className: 'entity-controls' },
              e('select', { id: 'saved-diagram', className: 'language-select', value: savedSelection, disabled: busy || !savedDiagrams.length,
                title: savedDiagrams.find(entry => entry.key === savedSelection)?.label,
                onChange: event => restoreSaved(event.target.value) },
                e('option', { value: '', disabled: true }, t('Select saved diagram')),
                savedDiagrams.map(entry => e('option', { key: entry.key, value: entry.key },
                  `${entry.label} | ${t(window.TymSegmentation.algorithms.find(item => item.id === entry.algorithm)?.label || 'Temporal continuity rules')}`))),
              e('button', { className: 'tool entity-refresh', type: 'button', title: t('Clear offline cache'), 'aria-label': t('Clear offline cache'),
                disabled: busy, onClick: clearOffline },
                e('svg', { width: 18, height: 18, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2,
                  strokeLinecap: 'round', strokeLinejoin: 'round', 'aria-hidden': true },
                  e('path', { d: 'M3 6h18M9 6V4h6v2M5 6l1 14h12l1-14M10 10v6M14 10v6' })))),
            e('small', { className: 'entity-status', role: 'status' }, t(offlineStatus))),
          e('div', { className: 'field density-cache-control' },
            e('label', { htmlFor: 'cached-density' }, t('Cached TT density')),
            e('select', { id: 'cached-density', className: 'language-select', value: result ? viewerDensity : '',
              disabled: busy || densityChoices.length < 2, onChange: event => selectDensity(Number(event.target.value)) },
              densityChoices.length ? Array.from({ length: 10 }, (_, index) => (index + 1) * 10).map(density => {
                const choice = densityChoices.find(option => option.density === density);
                return e('option', { key: density, value: density, disabled: !choice }, choice
                  ? `${density} - ${t('{count} tracks', { count: choice.track_count })}` : t('{density} - Pending', { density }));
              })
                : e('option', { value: '' }, t('No cached diagrams')))
            , e('small', { className: 'entity-status', role: 'status' }, t(cacheStatus))
          )),
          e('div', { className: 'tabs', role: 'tablist', 'aria-label': t('Result views') },
            resultTab('diagram', 'SVG'),
            resultTab('segmentation', 'Segmentation'),
            resultTab('analysis', 'Analysis'),
            resultTab('timeml', 'TimeML'),
            !protectedSource ? resultTab('json', 'JSON') : null,
            !protectedSource ? resultTab('xml', 'XML') : null
          ),
          e('div', { className: 'zoom-tools' },
            e('button', { className: `tool ${fitToWidth ? 'active' : ''}`, type: 'button', title: t('Fit diagram to viewport width'), 'aria-pressed': fitToWidth, onClick: () => setFitToWidth(!fitToWidth), disabled: !svg }, t('Fit')),
            e('button', { className: 'tool', type: 'button', title: t('Zoom out'), 'aria-label': t('Zoom out'), onClick: () => { setFitToWidth(false); setZoom(Math.max(0.5, Number((displayZoom - 0.25).toFixed(2)))); }, disabled: !svg }, '-'),
            e('button', { className: 'tool zoom-value', type: 'button', title: t('Actual size (100%)'), 'aria-label': t('Actual size (100%)'), onClick: () => { setFitToWidth(false); setZoom(1); }, disabled: !svg }, `${Math.round(displayZoom * 100)}%`),
            e('button', { className: 'tool', type: 'button', title: t('Zoom in'), 'aria-label': t('Zoom in'), onClick: () => { setFitToWidth(false); setZoom(Math.min(3, Number((displayZoom + 0.25).toFixed(2)))); }, disabled: !svg }, '+')
          )
        ),
        e('div', { className: 'metrics' },
          metric('Actors', count(diagram.actors)),
          metric('Events', count(diagram.events)),
          metric('Segments', count(diagram.segments)),
          metric('Tracks', count(diagram.tracks)),
          metric('Relations', count(diagram.relations)),
          metric('TLINKs', count(timeMl.tlinks))
        ),
        renderResultBody()
      )
    )
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(e(App));
