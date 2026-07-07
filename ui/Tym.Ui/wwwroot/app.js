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

  const diagram = result && result.diagram ? result.diagram : {};
  const timeMl = diagram.time_ml || {};
  const svgSize = parseSvgSize(svg);
  const jsonText = formatJson(result);
  const xmlText = result && result.xml ? result.xml : '';
  const timeMlText = timeMlSummary(timeMl);
  const statusClass = busy ? 'busy' : message ? 'error' : result ? 'ready' : '';
  const statusText = busy ? 'Generating' : message ? 'Needs attention' : result ? 'Ready' : 'Idle';

  function loadExample(example) {
    setSelectedExample(example.id);
    setText(example.text);
    setLanguage(example.language);
    setHighlightEntity(example.highlight);
    setMessage('');
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
        e('strong', null, 'TYM Diagram UI'),
        e('span', null, 'Natural-language text to temporal-segment diagram')
      ),
      e('div', { className: 'status' },
        e('span', { className: `status-dot ${statusClass}` }),
        e('span', null, statusText)
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
