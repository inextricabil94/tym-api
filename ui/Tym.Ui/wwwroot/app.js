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

function App() {
  const [selectedExample, setSelectedExample] = React.useState(examples[0].id);
  const [text, setText] = React.useState(examples[0].text);
  const [language, setLanguage] = React.useState(examples[0].language);
  const [apiBaseUrl, setApiBaseUrl] = React.useState(apiDefault);
  const [result, setResult] = React.useState(null);
  const [svg, setSvg] = React.useState('');
  const [busy, setBusy] = React.useState(false);
  const [message, setMessage] = React.useState('');

  const diagram = result && result.diagram ? result.diagram : {};
  const timeMl = diagram.time_ml || {};
  const statusClass = busy ? 'busy' : message ? 'error' : result ? 'ready' : '';
  const statusText = busy ? 'Generating' : message ? 'Needs attention' : result ? 'Ready' : 'Idle';

  function loadExample(example) {
    setSelectedExample(example.id);
    setText(example.text);
    setLanguage(example.language);
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
        width: 1200,
        highlight_entities: ['Adam'],
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
            e('button', { className: 'secondary', type: 'button', onClick: downloadSvg, disabled: !svg }, 'Download SVG')
          ),
          e('div', { className: 'message', role: 'status' }, message)
        )
      ),
      e('section', { className: 'panel result-panel' },
        e('div', { className: 'panel-header' },
          e('h2', { className: 'panel-title' }, 'Diagram'),
          result ? e('span', { className: 'status' }, result.model_version || '') : null
        ),
        e('div', { className: 'metrics' },
          metric('Actors', count(diagram.actors)),
          metric('Events', count(diagram.events)),
          metric('Segments', count(diagram.segments)),
          metric('Tracks', count(diagram.tracks)),
          metric('Relations', count(diagram.relations)),
          metric('TLINKs', count(timeMl.tlinks))
        ),
        e('div', { className: 'diagram-area' },
          svg
            ? e('div', { className: 'diagram-frame', dangerouslySetInnerHTML: { __html: svg } })
            : e('div', { className: 'empty' },
                e('div', null,
                  e('strong', null, 'No diagram yet'),
                  e('span', null, 'Select an example and generate a diagram.')
                )
              )
        )
      )
    )
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(e(App));
