(async function () {
  const api = window.TYM_CONFIG.apiBaseUrl.replace(/\/+$/, '');
  const modelSelect = document.getElementById('model');
  const input = document.getElementById('text');
  const predict = document.getElementById('predict');
  const exampleButton = document.getElementById('load-example');
  const status = document.getElementById('status');
  let models = [];
  let examples = {};

  function updateSelection() {
    const model = models.find(item => item.model_id === modelSelect.value);
    if (!model) return;
    input.value = examples[model.model_id] || '';
    document.getElementById('model-details').textContent = `${model.training_rows == null ? 'Unknown' : model.training_rows.toLocaleString()} training rows. Labels: ${model.labels.join(', ')}.`;
    document.getElementById('input-help').textContent = model.model_id.startsWith('timebank_')
      ? 'The example shows the required context format. Mark the event, time expression, or each relation endpoint with [TARGET] and [/TARGET].'
      : model.model_id.startsWith('temporal_relation_')
        ? 'Supply an earlier segment, a relation cue, and a later segment as shown in the example.'
        : 'Supply the prose of a candidate narrative segment.';
    document.getElementById('result').hidden = true;
    status.textContent = '';
  }

  try {
    const [catalogResponse, exampleResponse] = await Promise.all([
      fetch(`${api}/v1/models`), fetch('/prediction-examples.json')
    ]);
    if (!catalogResponse.ok || !exampleResponse.ok) throw new Error('Could not load trained models.');
    const catalog = await catalogResponse.json();
    examples = await exampleResponse.json();
    models = catalog.models.filter(model => model.available);
    modelSelect.replaceChildren(...models.map(model => {
      const option = document.createElement('option');
      option.value = model.model_id;
      option.textContent = `${model.task.replaceAll('_', ' ')} (${model.language.toUpperCase()})`;
      return option;
    }));
    if (!models.length) throw new Error('No corpus models are configured on this server.');
    modelSelect.disabled = predict.disabled = exampleButton.disabled = false;
    updateSelection();
  } catch (error) {
    status.textContent = error.message;
  }
  modelSelect.addEventListener('change', updateSelection);
  exampleButton.addEventListener('click', updateSelection);
  predict.addEventListener('click', async () => {
    if (!input.value.trim()) { status.textContent = 'Enter some text first.'; return; }
    predict.disabled = modelSelect.disabled = exampleButton.disabled = true;
    document.getElementById('result').hidden = true;
    status.textContent = 'Predicting...';
    try {
      const response = await fetch(`${api}/v1/predictions`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ model_id: modelSelect.value, text: input.value })
      });
      const result = await response.json();
      if (!response.ok) throw new Error(result.error || 'Prediction failed.');
      document.getElementById('prediction-label').textContent = result.predicted_label;
      document.getElementById('prediction-details').textContent = result.research_limit;
      document.getElementById('result').hidden = false;
      status.textContent = '';
    } catch (error) {
      status.textContent = error.message;
    } finally {
      predict.disabled = modelSelect.disabled = exampleButton.disabled = false;
    }
  });
})();
