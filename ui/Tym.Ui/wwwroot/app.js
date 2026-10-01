const e = React.createElement;

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

function count(value) {
  return Array.isArray(value) ? value.length : 0;
}

function metric(label, value) {
  return e('div', { className: 'metric', key: label },
    e('b', null, value),
    e('span', null, label)
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

function formatJson(value) {
  return value ? JSON.stringify(value, null, 2) : '';
}

function score(value) {
  const number = Number(value);
  return Number.isFinite(number) && number > 0 ? `${Math.round(number * 100)}%` : 'n/a';
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
  const [selectedExample, setSelectedExample] = React.useState(examples[0].id);
  const [text, setText] = React.useState(examples[0].text);
  const [language, setLanguage] = React.useState(examples[0].language);
  const [highlightEntity, setHighlightEntity] = React.useState(examples[0].highlight);
  const [diagramWidth, setDiagramWidth] = React.useState(1200);
  const [apiBaseUrl, setApiBaseUrl] = React.useState(apiDefault);
  const [result, setResult] = React.useState(null);
  const [svg, setSvg] = React.useState('');
  const [activeTab, setActiveTab] = React.useState('diagram');
  const [fitToWidth, setFitToWidth] = React.useState(true);
  const [zoom, setZoom] = React.useState(0.75);
  const [busy, setBusy] = React.useState(false);
  const [message, setMessage] = React.useState('');
  const [theme, setTheme] = React.useState(() => {
    try {
      return localStorage.getItem('tym-theme') || 'light';
    } catch {
      return 'light';
    }
  });
  const [selectedItem, setSelectedItem] = React.useState(null);
  const [resultInputText, setResultInputText] = React.useState('');
  const [eventCategoryFilter, setEventCategoryFilter] = React.useState('all');
  const [actorFilter, setActorFilter] = React.useState('all');
  const [minimumConfidence, setMinimumConfidence] = React.useState(0);
  const sourceTextArea = React.useRef(null);

  const diagram = result && result.diagram ? result.diagram : {};
  const analysis = result && result.analysis ? result.analysis : null;
  const warnings = result && Array.isArray(result.warnings) ? result.warnings : [];
  const timeMl = diagram.time_ml || {};
  const svgSize = parseSvgSize(svg);
  const jsonText = formatJson(result);
  const xmlText = result && result.xml ? result.xml : '';
  const timeMlText = timeMlSummary(timeMl);
  const statusClass = busy ? 'busy' : message ? 'error' : result ? 'ready' : '';
  const statusText = busy ? 'Generating' : message ? 'Needs attention' : result ? 'Ready' : 'Idle';

  React.useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try {
      localStorage.setItem('tym-theme', theme);
    } catch {
      // Keep the chosen theme for this session when storage is disabled.
    }
  }, [theme]);

  function loadExample(example) {
    setSelectedExample(example.id);
    setText(example.text);
    setLanguage(example.language);
    setHighlightEntity(example.highlight);
    setMessage('');
    setSelectedItem(null);
    setResult(null);
    setSvg('');
  }

  async function generate() {
    const trimmedApi = apiBaseUrl.trim().replace(/\/+$/, '');
    const trimmedText = text.trim();

    if (!trimmedApi || !trimmedText) {
      setMessage('Provide an API URL and input text.');
      return;
    }

    const payload = {
      text: trimmedText,
      options: {
        layout: 'both',
        width: diagramWidth,
        highlight_entities: highlightEntity.trim() ? [highlightEntity.trim()] : [],
        include_debug: true,
        language
      }
    };

    setBusy(true);
    setMessage('');

    try {
      const response = await fetch(`${trimmedApi}/v1/diagrams`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });

      if (!response.ok) {
        throw new Error(`API returned HTTP ${response.status}`);
      }

      const data = await response.json();
      const returnedSvg = (data.render && data.render.svg) || (data.renderings && data.renderings.svg) || '';

      setResult(data);
      setSvg(returnedSvg);
      setResultInputText(trimmedText);
      setSelectedItem(null);
      setActiveTab('diagram');

      if (!returnedSvg) {
        setMessage('The API returned JSON but no SVG rendering.');
      }
    } catch (error) {
      setResult(null);
      setSvg('');
      setMessage(error instanceof Error ? error.message : 'Diagram generation failed.');
    } finally {
      setBusy(false);
    }
  }

  function downloadSvg() {
    if (!svg) {
      return;
    }

    const blob = new Blob([svg], { type: 'image/svg+xml' });
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
    }, label);
  }

  function analysisCard(label, value) {
    return e('div', { className: 'analysis-card', key: label },
      e('span', null, label),
      e('b', null, value)
    );
  }

  function selectSourceSpan(start, end) {
    const currentText = text.trim();
    if (!sourceTextArea.current || !resultInputText || currentText !== resultInputText) {
      setMessage('The input has changed since this analysis. Generate a fresh result to jump to its source span.');
      return;
    }

    const baseOffset = text.indexOf(resultInputText);
    const spanStart = Math.max(0, baseOffset + Number(start || 0));
    const spanEnd = Math.max(spanStart, baseOffset + Number(end || start || 0));
    sourceTextArea.current.focus();
    sourceTextArea.current.setSelectionRange(spanStart, spanEnd);
    sourceTextArea.current.scrollIntoView({ behavior: 'smooth', block: 'center' });
    setMessage('');
  }

  function selectEvent(eventItem) {
    setSelectedItem({ kind: 'event', id: eventItem.id });
    selectSourceSpan(eventItem.span_start, eventItem.span_end);
  }

  function selectSegment(segmentItem) {
    setSelectedItem({ kind: 'segment', id: segmentItem.id });
    selectSourceSpan(segmentItem.span_start, segmentItem.span_end);
  }

  function selectionDetails() {
    if (!selectedItem) {
      return e('section', { className: 'selection-detail muted-detail' },
        e('strong', null, 'Inspect a source span'),
        e('span', null, 'Select an event or time segment below to highlight its text and inspect its fields.')
      );
    }

    const selected = selectedItem.kind === 'event'
      ? (diagram.events || []).find(item => item.id === selectedItem.id)
      : (diagram.segments || []).find(item => item.id === selectedItem.id);
    if (!selected) {
      return null;
    }

    const isEvent = selectedItem.kind === 'event';
    const provenance = isEvent ? (selected.provenance || {}) : {};
    const fields = isEvent
      ? [
          ['Temporal category', selected.temporal_category],
          ['Actors', (selected.actors || []).join(', ') || 'Unassigned'],
          ['Temporal anchor', selected.temporal_anchor || 'Not identified'],
          ['Location', selected.location || 'Not identified'],
          ['Relation', `${selected.relation_to_previous || 'Unspecified'}${selected.relation_cue ? ` · ${selected.relation_cue}` : ''}`],
          ['Confidence', score(selected.confidence)],
          ...Object.entries(provenance).map(([name, item]) => [
            `${name.replaceAll('_', ' ')} evidence`,
            item && item.evidence ? `${item.source || 'unknown'} · ${item.evidence}` : (item?.source || 'unknown')
          ])
        ]
      : [
          ['Type', selected.type],
          ['Track', selected.track_id],
          ['Perspective', selected.perspective],
          ['Actors', (selected.actors || []).join(', ') || 'Unassigned'],
          ['Temporal category', selected.temporal_category],
          ['Temporal anchor', selected.temporal_anchor || 'Not identified'],
          ['Events', (selected.event_ids || []).join(', ') || 'None'],
          ['Classifier', selected.classifier || 'unknown'],
          ['Confidence', score(selected.confidence)]
        ];

    return e('section', { className: 'selection-detail' },
      e('div', { className: 'selection-heading' },
        e('div', null,
          e('strong', null, `${selected.id} · ${isEvent ? 'Event' : 'Time segment'}`),
          e('p', null, selected.text || '')
        ),
        e('button', {
          className: 'tool',
          type: 'button',
          onClick: () => selectSourceSpan(selected.span_start, selected.span_end)
        }, 'Jump to text')
      ),
      e('dl', { className: 'detail-grid' }, fields.map(([label, value]) =>
        e(React.Fragment, { key: label }, e('dt', null, label), e('dd', null, value || 'Not identified'))
      ))
    );
  }

  function countBlock(title, value) {
    const entries = countEntries(value);
    return e('section', { className: 'analysis-block', key: title },
      e('h3', null, title),
      entries.length
        ? e('div', { className: 'count-grid' },
            entries.map(([label, countValue]) => e('span', { className: 'count-chip', key: label },
              e('b', null, countValue),
              label
            ))
          )
        : e('p', { className: 'muted-line' }, 'No values.')
    );
  }

  function sourceBlock() {
    const sources = analysisValue(analysis, 'extraction_sources', 'extractionSources', []);
    return e('section', { className: 'analysis-block' },
      e('h3', null, 'Extraction sources'),
      sources.length
        ? e('div', { className: 'source-list' }, sources.map(source => e('code', { key: source }, source)))
        : e('p', { className: 'muted-line' }, 'No sources.')
    );
  }

  function issueBlock() {
    const issues = analysisValue(analysis, 'issues', 'issues', []);
    const rows = issues.length
      ? issues.map(issue => e('li', { className: `issue ${issue.severity || ''}`, key: issue.code || issue.message },
          e('b', null, issue.code || 'ISSUE'),
          e('span', null, issue.message || ''),
          issue.evidence ? e('small', null, issue.evidence) : null
        ))
      : [e('li', { className: 'issue info', key: 'ok' },
          e('b', null, 'NO_ANALYSIS_ISSUES'),
          e('span', null, 'No analysis issues were reported.'),
          null
        )];

    return e('section', { className: 'analysis-block' },
      e('h3', null, 'Analysis issues'),
      e('ul', { className: 'issue-list' }, rows),
      warnings.length
        ? e('div', { className: 'warnings' },
            e('h3', null, 'Warnings'),
            warnings.map(item => e('p', { key: item }, item))
          )
        : null
    );
  }

  function dataTable(title, headers, rows, emptyText, onSelect) {
    return e('section', { className: 'analysis-block wide', key: title },
      e('h3', null, title),
      rows.length
        ? e('div', { className: 'table-scroll' },
            e('table', { className: 'data-table' },
              e('thead', null,
                e('tr', null, headers.map(header => e('th', { key: header }, header)))
              ),
              e('tbody', null,
                rows.map((row, rowIndex) => e('tr', { key: row.key || `${title}-${rowIndex}`,
                    className: onSelect && selectedItem?.id === row.key ? 'selected-row' : '' },
                  row.cells.map((cell, cellIndex) => e('td', { key: `${title}-${rowIndex}-${cellIndex}` },
                    cellIndex === 0 && onSelect
                      ? e('button', {
                          className: 'row-select',
                          type: 'button',
                          'aria-pressed': selectedItem?.id === row.key,
                          onClick: () => onSelect(row.item)
                        }, cell || '-')
                      : (cell || '-')))
                ))
              )
            )
          )
        : e('p', { className: 'muted-line' }, emptyText)
    );
  }

  function renderAnalysis() {
    if (!analysis) {
      return e('div', { className: 'empty' },
        e('div', null,
          e('strong', null, 'No analysis yet'),
          e('span', null, 'Generate a diagram to inspect extractor statistics.')
        )
      );
    }

    const allEvents = diagram.events || [];
    const actorNames = Array.from(new Set(allEvents.flatMap(item => item.actors || []))).sort((a, b) => a.localeCompare(b));
    const eventRows = allEvents.filter(item =>
      (eventCategoryFilter === 'all' || item.temporal_category === eventCategoryFilter)
      && (actorFilter === 'all' || (item.actors || []).includes(actorFilter))
      && Number(item.confidence || 0) >= minimumConfidence
    ).map(item => {
      const provenance = item.provenance || {};
      return {
        key: item.id,
        item,
        cells: [
          item.id,
          item.temporal_category,
          item.relation_to_previous,
          item.relation_cue || '-',
          provenance.actors?.source || '-',
          provenance.temporal_anchor?.source || '-',
          provenance.temporal_category?.source || item.classifier || '-',
          provenance.relation?.source || '-',
          score(item.confidence),
          item.text
        ]
      };
    });
    const segmentRows = (diagram.segments || []).map(item => ({
      key: item.id,
      item,
      cells: [
        item.id,
        item.type,
        item.track_id,
        item.perspective || '-',
        (item.actors || []).join(', '),
        item.temporal_anchor || '-',
        (item.event_ids || []).join(', '),
        score(item.confidence)
      ]
    }));

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
        countBlock('Provenance methods', analysisValue(analysis, 'provenance_sources', 'provenanceSources', {})),
        sourceBlock(),
        issueBlock()
      ),
      e('section', { className: 'analysis-block wide event-explorer' },
        e('div', { className: 'explorer-heading' },
          e('div', null,
            e('h3', null, 'Event explorer'),
            e('p', { className: 'muted-line' }, `${eventRows.length} of ${allEvents.length} events shown · select an ID to highlight its source span.`)
          ),
          e('div', { className: 'filters' },
            e('label', null, 'Temporal category',
              e('select', { value: eventCategoryFilter, onChange: event => setEventCategoryFilter(event.target.value) },
                e('option', { value: 'all' }, 'All'),
                ...Array.from(new Set(allEvents.map(item => item.temporal_category).filter(Boolean))).sort().map(value => e('option', { key: value, value }, value))
              )
            ),
            e('label', null, 'Actor',
              e('select', { value: actorFilter, onChange: event => setActorFilter(event.target.value) },
                e('option', { value: 'all' }, 'All actors'),
                ...actorNames.map(value => e('option', { key: value, value }, value))
              )
            ),
            e('label', { className: 'confidence-filter' }, `Minimum confidence ${Math.round(minimumConfidence * 100)}%`,
              e('input', { type: 'range', min: '0', max: '0.9', step: '0.05', value: minimumConfidence,
                onChange: event => setMinimumConfidence(Number(event.target.value)) })
            )
          )
        ),
        dataTable('Events', ['Event', 'Temporal', 'Rel prev', 'Cue', 'Actor source', 'Anchor source', 'Category source', 'Relation source', 'Confidence', 'Text'], eventRows, 'No events match these filters.', selectEvent)
      ),
      selectionDetails(),
      dataTable('Time segments', ['TS', 'Type', 'Track', 'Perspective', 'Actors', 'Anchor', 'Events', 'Confidence'], segmentRows, 'No segments detected.', selectSegment)
    );
  }

  function renderResultBody() {
    if (activeTab === 'json') {
      return e('pre', { className: 'code-view' }, jsonText || 'No JSON result yet.');
    }

    if (activeTab === 'xml') {
      return e('pre', { className: 'code-view' }, xmlText || 'No XML result yet.');
    }

    if (activeTab === 'timeml') {
      return e('pre', { className: 'code-view' }, timeMlText || 'No TimeML result yet.');
    }

    if (activeTab === 'analysis') {
      return renderAnalysis();
    }

    return e('div', { className: 'diagram-area' },
      svg
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
              dangerouslySetInnerHTML: { __html: svg }
            })
          )
        : e('div', { className: 'empty' },
            e('div', null,
              e('strong', null, 'No diagram yet'),
              e('span', null, 'Select an example and generate a diagram.')
            )
          )
    );
  }

  return e('main', { className: 'shell' },
    e('header', { className: 'topbar' },
      e('div', { className: 'brand' },
        e('strong', null, 'TYM Workbench'),
        e('span', null, 'Narrative time analysis · inspect every source span')
      ),
      e('div', { className: 'topbar-actions' },
        e('button', {
          className: 'theme-toggle',
          type: 'button',
          'aria-pressed': theme === 'dark',
          onClick: () => setTheme(value => value === 'dark' ? 'light' : 'dark')
        }, theme === 'dark' ? 'Use light theme' : 'Use dark theme'),
        e('div', { className: 'status' },
          e('span', { className: `status-dot ${statusClass}` }),
          e('span', null, statusText)
        )
      )
    ),
    e('section', { className: 'workspace' },
      e('section', { className: 'panel input-panel' },
        e('div', { className: 'panel-header' },
          e('h1', { className: 'panel-title' }, 'Input'),
          e('div', { className: 'tabs', role: 'tablist', 'aria-label': 'Examples' },
            examples.map(example => e('button', {
              key: example.id,
              className: `tab ${selectedExample === example.id ? 'active' : ''}`,
              onClick: () => loadExample(example),
              type: 'button'
            }, example.label))
          )
        ),
        e('div', { className: 'fields' },
          e('div', { className: 'field' },
            e('label', { htmlFor: 'api-url' }, 'API URL'),
            e('input', {
              id: 'api-url',
              className: 'api-input',
              value: apiBaseUrl,
              onChange: event => setApiBaseUrl(event.target.value),
              spellCheck: false
            })
          ),
          e('div', { className: 'field' },
            e('label', { htmlFor: 'language' }, 'Language'),
            e('select', {
              id: 'language',
              className: 'language-select',
              value: language,
              onChange: event => setLanguage(event.target.value)
            },
              e('option', { value: 'en' }, 'English'),
              e('option', { value: 'ro' }, 'Romanian')
            )
          ),
          e('div', { className: 'field-grid' },
            e('div', { className: 'field' },
              e('label', { htmlFor: 'highlight' }, 'Highlight entity'),
              e('input', {
                id: 'highlight',
                className: 'api-input',
                value: highlightEntity,
                onChange: event => setHighlightEntity(event.target.value),
                spellCheck: false
              })
            ),
            e('div', { className: 'field' },
              e('label', { htmlFor: 'width' }, `Diagram width ${diagramWidth}px`),
              e('input', {
                id: 'width',
                className: 'range-input',
                type: 'range',
                min: '900',
                max: '1800',
                step: '100',
                value: diagramWidth,
                onChange: event => setDiagramWidth(Number(event.target.value))
              })
            )
          ),
          e('div', { className: 'field' },
            e('label', { htmlFor: 'text' }, 'Text'),
            e('textarea', {
              id: 'text',
              className: 'text-input',
              ref: sourceTextArea,
              value: text,
              onChange: event => setText(event.target.value)
            })
          ),
          e('div', { className: 'actions' },
            e('button', { className: 'primary', type: 'button', onClick: generate, disabled: busy }, busy ? 'Generating...' : 'Generate diagram'),
            e('div', { className: 'download-actions' },
              e('button', { className: 'secondary', type: 'button', onClick: downloadSvg, disabled: !svg }, 'SVG'),
              e('button', { className: 'secondary', type: 'button', onClick: () => downloadText('tym-diagram.json', jsonText, 'application/json'), disabled: !result }, 'JSON'),
              e('button', { className: 'secondary', type: 'button', onClick: () => downloadText('tym-diagram.xml', xmlText, 'application/xml'), disabled: !xmlText }, 'XML')
            )
          ),
          e('div', { className: 'message', role: 'status' }, message)
        )
      ),
      e('section', { className: 'panel result-panel' },
        e('div', { className: 'panel-header' },
          e('h2', { className: 'panel-title' }, 'Diagram'),
          result ? e('span', { className: 'status' }, result.model_version || '') : null
        ),
        e('div', { className: 'result-toolbar' },
          e('div', { className: 'tabs', role: 'tablist', 'aria-label': 'Result views' },
            resultTab('diagram', 'SVG'),
            resultTab('analysis', 'Analysis'),
            resultTab('timeml', 'TimeML'),
            resultTab('json', 'JSON'),
            resultTab('xml', 'XML')
          ),
          e('div', { className: 'zoom-tools' },
            e('button', { className: `tool ${fitToWidth ? 'active' : ''}`, type: 'button', onClick: () => setFitToWidth(!fitToWidth), disabled: !svg }, 'Fit'),
            e('button', { className: 'tool', type: 'button', onClick: () => setZoom(value => Math.max(0.45, Number((value - 0.1).toFixed(2)))), disabled: !svg || fitToWidth }, '-'),
            e('span', { className: 'zoom-value' }, `${Math.round(zoom * 100)}%`),
            e('button', { className: 'tool', type: 'button', onClick: () => setZoom(value => Math.min(1.5, Number((value + 0.1).toFixed(2)))), disabled: !svg || fitToWidth }, '+')
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
