/** Loads aggregate research evidence independently of the prediction/document workbenches.
 * All report labels, scopes, metric keys and notes are rendered as text, never HTML. */
(() => {
  if (!document.getElementById('algorithm-comparison')) return;

  const familyOrder = ['linear_regression', 'logistic_regression', 'decision_tree', 'random_forest',
    'gradient_boosting', 'svm', 'knn', 'naive_bayes', 'kmeans', 'hierarchical_clustering', 'pca',
    'mlp', 'cnn', 'rnn', 'transformer', 'autoencoder', 'dbscan'] as const;
  type Family = typeof familyOrder[number];
  const bookFamilies = new Set<Family>(['kmeans', 'hierarchical_clustering', 'pca', 'autoencoder', 'dbscan']);
  const spaceNumbers = ['feature_dimensions_min', 'feature_dimensions_max', 'training_vocabulary_size_min',
    'training_vocabulary_size_max', 'maximum_sequence_length', 'process_working_set_before_min_mb',
    'process_working_set_after_max_mb', 'process_lifetime_peak_working_set_max_mb', 'neural_parameter_count_min',
    'neural_parameter_count_max', 'float32_parameter_bytes_max', 'artifact_bytes'] as const;
  type Space = {[Key in typeof spaceNumbers[number]]: number | null} & {scope: string};
  interface Approach {
    family: Family; family_label: string; algorithm: string; algorithm_label: string;
    language: 'en' | 'ro'; notes: string[]; space: Space | null;
  }
  interface ClassificationRow extends Approach {
    task: string; accuracy_fraction: number; macro_f1_fraction: number;
    total_training_seconds: number; total_prediction_seconds: number;
    prediction_ms_per_row: number; batched_rows_per_second: number; rows: number;
  }
  interface BookRow extends Approach {
    training_seconds: number; assessment_seconds: number; semantic_accuracy: null;
    metrics: Record<string, number | null>; scope: string;
  }
  interface Summary {
    classification_primary_task: string; classification_rows: ClassificationRow[]; books_rows: BookRow[];
  }
  class ReportError extends Error {}
  const invalid = () => { throw new ReportError('The comparison summary is incomplete or has an invalid format. Reload it after a complete report is published.'); };
  function object(value: unknown): Record<string, unknown> {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return invalid();
    return value as Record<string, unknown>;
  }
  function text(value: unknown, maximum = 250): string {
    if (typeof value !== 'string' || !value.trim() || value.length > maximum) return invalid();
    return value;
  }
  function nonnegative(value: unknown): number {
    if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) return invalid();
    return value;
  }
  function fraction(value: unknown): number {
    const result = nonnegative(value);
    return result <= 1 ? result : invalid();
  }
  function notes(value: unknown): string[] {
    if (!Array.isArray(value) || value.length > 50) return invalid();
    return value.map(note => text(note, 6000));
  }
  function parseSpace(value: unknown): Space | null {
    // Older summaries omitted the entire object. Optional missing numeric fields stay
    // unavailable; explicitly supplied values must be finite, nonnegative numbers.
    if (value === undefined || value === null) return null;
    const row = object(value), result = {scope: text(row.scope, 6000)} as Space;
    for (const key of spaceNumbers) result[key] = row[key] === undefined || row[key] === null ? null : nonnegative(row[key]);
    for (const [minimum, maximum] of [['feature_dimensions_min', 'feature_dimensions_max'],
      ['training_vocabulary_size_min', 'training_vocabulary_size_max'], ['neural_parameter_count_min', 'neural_parameter_count_max']] as const) {
      const lower = result[minimum], upper = result[maximum];
      if (lower !== null && upper !== null && lower > upper) return invalid();
    }
    return result;
  }
  function approach(value: unknown): Approach {
    const row = object(value), family = text(row.family);
    if (!(familyOrder as readonly string[]).includes(family) || row.language !== 'en' && row.language !== 'ro') return invalid();
    return {family: family as Family, family_label: text(row.family_label), algorithm: text(row.algorithm),
      algorithm_label: text(row.algorithm_label), language: row.language, notes: notes(row.notes), space: parseSpace(row.space)};
  }
  function classification(value: unknown): ClassificationRow {
    const row = object(value), base = approach(row), count = nonnegative(row.rows);
    if (bookFamilies.has(base.family) || count < 1 || !Number.isSafeInteger(count)) return invalid();
    return {...base, task: text(row.task), accuracy_fraction: fraction(row.accuracy_fraction),
      macro_f1_fraction: fraction(row.macro_f1_fraction), total_training_seconds: nonnegative(row.total_training_seconds),
      total_prediction_seconds: nonnegative(row.total_prediction_seconds), prediction_ms_per_row: nonnegative(row.prediction_ms_per_row),
      batched_rows_per_second: nonnegative(row.batched_rows_per_second), rows: count};
  }
  function book(value: unknown): BookRow {
    const row = object(value), base = approach(row), sourceMetrics = object(row.metrics);
    if (!bookFamilies.has(base.family) || row.semantic_accuracy !== null || Object.keys(sourceMetrics).length > 60) return invalid();
    const metrics: Record<string, number | null> = Object.create(null) as Record<string, number | null>;
    for (const [key, value] of Object.entries(sourceMetrics)) {
      text(key, 150);
      if (value !== null && (typeof value !== 'number' || !Number.isFinite(value))) return invalid();
      metrics[key] = value as number | null;
    }
    return {...base, training_seconds: nonnegative(row.training_seconds), assessment_seconds: nonnegative(row.assessment_seconds),
      semantic_accuracy: null, metrics, scope: text(row.scope, 6000)};
  }
  function parseSummary(value: unknown): Summary {
    const data = object(value), coverage = object(data.coverage);
    if (data.status !== 'completed_algorithm_comparison' || coverage.families !== 17
      || data.schema_version !== undefined && data.schema_version !== 1
      || !Array.isArray(data.classification_rows) || data.classification_rows.length < 1 || data.classification_rows.length > 5000
      || !Array.isArray(data.books_rows) || data.books_rows.length < 1 || data.books_rows.length > 500) return invalid();
    const classificationRows = data.classification_rows.map(classification), bookRows = data.books_rows.map(book);
    const families = new Set([...classificationRows, ...bookRows].map(row => row.family));
    const primary = text(data.classification_primary_task);
    if (families.size !== 17 || familyOrder.some(family => !families.has(family))
      || !classificationRows.some(row => row.task === primary)) return invalid();
    const identities = new Set<string>();
    for (const row of classificationRows) {
      const key = JSON.stringify([row.task, row.language, row.algorithm]);
      if (identities.has(key)) return invalid();
      identities.add(key);
    }
    identities.clear();
    for (const row of bookRows) {
      const key = JSON.stringify([row.language, row.algorithm]);
      if (identities.has(key)) return invalid();
      identities.add(key);
    }
    return {classification_primary_task: primary, classification_rows: classificationRows, books_rows: bookRows};
  }

  function element<T extends HTMLElement>(id: string): T {
    const result = document.getElementById(id);
    if (!result) throw new Error('Missing comparison element: ' + id);
    return result as T;
  }
  const section = element('algorithm-comparison'), status = element('comparison-status');
  const results = element('comparison-results'), retry = element<HTMLButtonElement>('comparison-retry');
  const task = element<HTMLSelectElement>('comparison-task'), language = element<HTMLSelectElement>('comparison-language');
  let summary: Summary | null = null, loading = false;
  const taskNames: Record<string, string> = {
    timebank_event_class: 'Event class', timebank_event_tense: 'Event tense', timebank_timex_type: 'Time-expression type',
    timebank_tlink: 'TLINK · temporal relations', timebank_slink: 'SLINK · subordinate relations', timebank_alink: 'ALINK · aspectual relations'
  };
  const languageName = (value: string) => value === 'ro' ? 'Romanian' : value === 'en' ? 'English' : value;
  const seconds = (value: number) => value.toLocaleString(undefined, {minimumFractionDigits: value < 1 ? 3 : 2, maximumFractionDigits: value < 1 ? 3 : 2});
  const percent = (value: number) => (value * 100).toFixed(2) + '%';
  const decimal = (value: number) => value.toLocaleString(undefined, {maximumFractionDigits: 3});
  const metricName = (key: string) => key.replace(/_/g, ' ').replace(/^./, letter => letter.toUpperCase());
  const metricValue = (value: number | null) => value === null ? 'N/A' : value !== 0 && Math.abs(value) < 0.0001
    ? value.toExponential(3) : value.toLocaleString(undefined, {maximumFractionDigits: 5});
  function cell(row: HTMLTableRowElement, value: string, className = ''): HTMLTableCellElement {
    const result = document.createElement('td'); result.textContent = value; result.className = className; row.append(result); return result;
  }
  function nameCell(row: HTMLTableRowElement, approach: Approach) {
    const result = cell(row, ''), family = document.createElement('strong'), algorithm = document.createElement('span');
    family.className = 'comparison-family'; family.textContent = approach.family_label;
    algorithm.className = 'comparison-approach'; algorithm.textContent = approach.algorithm_label;
    result.append(family, algorithm);
  }
  function renderNotes(parent: HTMLElement, values: string[]) {
    if (!values.length) return;
    const details = document.createElement('details'), heading = document.createElement('summary');
    heading.textContent = 'Interpretation notes'; details.append(heading);
    for (const note of values) { const paragraph = document.createElement('p'); paragraph.textContent = note; details.append(paragraph); }
    parent.append(details);
  }
  function renderSpace(parent: HTMLElement, measurement: Space | null) {
    if (measurement === null) {
      const unavailable = document.createElement('p'); unavailable.className = 'comparison-space-unavailable';
      unavailable.textContent = 'Space measurements unavailable.'; parent.append(unavailable); return;
    }
    const details = document.createElement('details'), heading = document.createElement('summary');
    details.className = 'comparison-space'; heading.textContent = 'Space and storage'; details.append(heading);
    const scope = document.createElement('p'); scope.className = 'comparison-space-scope'; scope.textContent = measurement.scope; details.append(scope);
    const metrics = document.createElement('dl'); metrics.className = 'comparison-metrics comparison-space-metrics';
    function entry(name: string, value: string) {
      const row = document.createElement('div'), title = document.createElement('dt'), result = document.createElement('dd');
      title.textContent = name; result.textContent = value; row.append(title, result); metrics.append(row);
    }
    function range(minimum: number | null, maximum: number | null): string | null {
      if (minimum === null && maximum === null) return null;
      if (minimum === null) return 'Maximum ' + decimal(maximum!);
      if (maximum === null) return 'Minimum ' + decimal(minimum);
      return minimum === maximum ? decimal(minimum) : decimal(minimum) + '–' + decimal(maximum);
    }
    const dimensions = range(measurement.feature_dimensions_min, measurement.feature_dimensions_max);
    const vocabulary = range(measurement.training_vocabulary_size_min, measurement.training_vocabulary_size_max);
    const parameters = range(measurement.neural_parameter_count_min, measurement.neural_parameter_count_max);
    if (dimensions !== null) entry('Feature dimensions', dimensions);
    if (vocabulary !== null) entry('Training vocabulary', vocabulary);
    if (measurement.maximum_sequence_length !== null) entry('Sequence limit', decimal(measurement.maximum_sequence_length) + ' tokens');
    if (parameters !== null) entry('Neural parameters', parameters);
    if (measurement.float32_parameter_bytes_max !== null) entry('Float32 parameter bytes (maximum estimate)', decimal(measurement.float32_parameter_bytes_max) + ' B');
    if (measurement.process_working_set_before_min_mb !== null) entry('Sampled process memory before (minimum)', decimal(measurement.process_working_set_before_min_mb) + ' MiB');
    if (measurement.process_working_set_after_max_mb !== null) entry('Sampled process memory after (maximum)', decimal(measurement.process_working_set_after_max_mb) + ' MiB');
    if (measurement.process_lifetime_peak_working_set_max_mb !== null) entry('Process lifetime peak memory (maximum)', decimal(measurement.process_lifetime_peak_working_set_max_mb) + ' MiB');
    entry('Exported artifact bytes', measurement.artifact_bytes === null ? 'Unavailable' : decimal(measurement.artifact_bytes) + ' B');
    details.append(metrics);
    const meaning = document.createElement('p');
    meaning.textContent = 'Process memory includes shared runtime and earlier trials; it is not isolated model memory. Float32 parameter bytes estimate weights only. Storage sizes measure exported artifacts only.';
    details.append(meaning); parent.append(details);
  }
  function emptyRow(body: HTMLElement, columns: number, message: string) {
    const row = document.createElement('tr'), messageCell = cell(row, message, 'comparison-empty');
    messageCell.colSpan = columns; body.append(row);
  }
  function order<T extends Approach>(rows: T[]): T[] {
    return rows.sort((left, right) => familyOrder.indexOf(left.family) - familyOrder.indexOf(right.family)
      || left.algorithm_label.localeCompare(right.algorithm_label) || left.language.localeCompare(right.language));
  }
  function render() {
    if (!summary) return;
    const selectedLanguage = language.value;
    const classificationRows = order(summary.classification_rows.filter(row => row.task === task.value && (selectedLanguage === 'all' || row.language === selectedLanguage)));
    const bookRows = order(summary.books_rows.filter(row => selectedLanguage === 'all' || row.language === selectedLanguage));
    const classBody = element('comparison-classification-rows'), bookBody = element('comparison-book-rows');
    classBody.replaceChildren(); bookBody.replaceChildren();
    for (const result of classificationRows) {
      const row = document.createElement('tr'); nameCell(row, result); cell(row, languageName(result.language));
      cell(row, percent(result.accuracy_fraction), 'comparison-number'); cell(row, percent(result.macro_f1_fraction), 'comparison-number');
      cell(row, seconds(result.total_training_seconds), 'comparison-number'); cell(row, seconds(result.total_prediction_seconds), 'comparison-number');
      cell(row, decimal(result.prediction_ms_per_row), 'comparison-number');
      const noteCell = cell(row, '', 'comparison-note'), count = document.createElement('p');
      count.textContent = result.rows.toLocaleString() + ' validation rows · ' + decimal(result.batched_rows_per_second) + ' rows/s in batch';
      noteCell.append(count); renderSpace(noteCell, result.space); renderNotes(noteCell, result.notes); classBody.append(row);
    }
    if (!classificationRows.length) emptyRow(classBody, 8, 'No published classification results for this task and language.');
    for (const result of bookRows) {
      const row = document.createElement('tr'); nameCell(row, result); cell(row, languageName(result.language));
      cell(row, seconds(result.training_seconds), 'comparison-number'); cell(row, seconds(result.assessment_seconds), 'comparison-number');
      cell(row, 'N/A · unlabeled passages');
      const diagnostic = cell(row, '', 'comparison-note'), scope = document.createElement('p'); scope.textContent = result.scope; diagnostic.append(scope);
      const metrics = document.createElement('dl'); metrics.className = 'comparison-metrics';
      for (const [key, value] of Object.entries(result.metrics)) {
        const entry = document.createElement('div'), name = document.createElement('dt'), number = document.createElement('dd');
        name.textContent = metricName(key); number.textContent = metricValue(value); entry.append(name, number); metrics.append(entry);
      }
      diagnostic.append(metrics); renderSpace(diagnostic, result.space); renderNotes(diagnostic, result.notes); bookBody.append(row);
    }
    if (!bookRows.length) emptyRow(bookBody, 6, 'No published book exploration results for this language.');
    element('comparison-classification-summary').textContent = `${classificationRows.length} approaches shown for ${taskNames[task.value] || task.value}. Each score is conditional on the supplied annotations.`;
    element('comparison-books-summary').textContent = `${bookRows.length} exploration results shown. The task filter applies to classification only; the language filter applies to both tables.`;
  }
  function populate(data: Summary) {
    const tasks = [...new Set(data.classification_rows.map(row => row.task))].sort();
    task.replaceChildren();
    for (const value of tasks) { const option = document.createElement('option'); option.value = value; option.textContent = taskNames[value] || value; task.append(option); }
    task.value = data.classification_primary_task;
    language.replaceChildren();
    const all = document.createElement('option'); all.value = 'all'; all.textContent = 'All languages'; language.append(all);
    for (const value of [...new Set([...data.classification_rows, ...data.books_rows].map(row => row.language))].sort()) {
      const option = document.createElement('option'); option.value = value; option.textContent = languageName(value); language.append(option);
    }
    language.value = 'all';
    const classificationVariants = new Set(data.classification_rows.map(row => row.algorithm)).size;
    const bookVariants = new Set(data.books_rows.map(row => row.algorithm)).size;
    element('comparison-coverage').textContent = `17 algorithm families · ${classificationVariants} classification approaches across ${tasks.length} tasks · ${bookVariants} book approaches. Expanded algorithm selection is exploratory; no independent selection holdout was used.`;
  }
  async function load() {
    if (loading) return;
    loading = true; section.setAttribute('aria-busy', 'true'); results.hidden = true; retry.hidden = true;
    task.disabled = language.disabled = true; status.dataset.tone = 'info'; status.textContent = 'Loading the comparison summary…';
    try {
      const response = await fetch('/results/algorithm-comparison.json', {mode: 'same-origin', credentials: 'same-origin', signal: AbortSignal.timeout(30000)});
      if (!response.ok) throw new ReportError(response.status === 404 ? 'The comparison has not been published yet. Try reloading later.' : 'The comparison summary could not be loaded. Try reloading it.');
      if (!(response.headers.get('content-type') || '').toLowerCase().includes('json')) invalid();
      const length = response.headers.get('content-length');
      if (length && (!Number.isFinite(Number(length)) || Number(length) < 0 || Number(length) > 2000000)) invalid();
      const body = await response.text(); if (body.length > 2000000) invalid();
      summary = parseSummary(JSON.parse(body) as unknown); populate(summary); render();
      results.hidden = false; task.disabled = language.disabled = false; status.textContent = 'Published comparison loaded.';
    } catch (error) {
      summary = null; status.dataset.tone = 'error'; retry.hidden = false;
      status.textContent = error instanceof ReportError ? error.message : error instanceof Error && (error.name === 'TimeoutError' || error.name === 'AbortError')
        ? 'The comparison took too long to load. Try reloading it.' : 'The comparison summary is unavailable or has an invalid format. Try reloading later.';
    } finally { loading = false; section.setAttribute('aria-busy', 'false'); }
  }
  task.addEventListener('change', render); language.addEventListener('change', render); retry.addEventListener('click', () => { void load(); });
  void load();
})();
