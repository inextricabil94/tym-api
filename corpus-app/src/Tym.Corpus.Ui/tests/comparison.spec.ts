import { test, expect, type Page } from 'playwright/test';
import { mockCatalog } from './helpers';

// Rendering uses original synthetic software fixtures, not measured model results. The
// download check verifies the actual aggregate CSV served by this local UI. No private
// corpus text, book passages, model weights, or remote API is read by these tests.
const classifiers = [
  ['linear_regression', 'Linear regression', 'linear_regression_ovr', 'Class-indicator regression'],
  ['logistic_regression', 'Logistic regression', 'sdca', 'SDCA'],
  ['logistic_regression', 'Logistic regression', 'lbfgs', 'L-BFGS'],
  ['decision_tree', 'Decision tree', 'decision_tree', 'CART'],
  ['random_forest', 'Random forest', 'fastforest_ova', 'FastForest'],
  ['gradient_boosting', 'Gradient boosting', 'fasttree_ova', 'FastTree'],
  ['gradient_boosting', 'Gradient boosting', 'lightgbm', 'LightGBM'],
  ['svm', 'SVM', 'linear_svm_ova', 'Linear SVM'],
  ['knn', 'KNN', 'knn', 'Cosine KNN'],
  ['naive_bayes', 'Naive Bayes', 'naive_bayes', 'Presence Naive Bayes'],
  ['mlp', 'MLP', 'mlp', 'Scratch MLP'],
  ['cnn', 'CNN', 'cnn', 'Scratch text CNN'],
  ['rnn', 'RNN', 'rnn', 'Scratch LSTM'],
  ['transformer', 'Transformer', 'transformer', 'Scratch encoder']
] as const;
const books = [
  ['kmeans', 'K-Means', 'mlnet_kmeans', 'Passage K-Means'],
  ['pca', 'PCA', 'mlnet_pca_kmeans', 'PCA + K-Means'],
  ['hierarchical_clustering', 'Hierarchical clustering', 'csharp_average_linkage_hierarchical', 'Average linkage'],
  ['autoencoder', 'Autoencoder', 'csharp_torchsharp_autoencoder', 'Numeric reconstruction'],
  ['dbscan', 'DBSCAN', 'csharp_dbscan', 'Density expansion']
] as const;

interface SyntheticSpace {
  feature_dimensions_min: number | null; feature_dimensions_max: number | null;
  training_vocabulary_size_min: number | null; training_vocabulary_size_max: number | null;
  maximum_sequence_length: number | null; process_working_set_before_min_mb: number | null;
  process_working_set_after_max_mb: number | null; process_lifetime_peak_working_set_max_mb: number | null;
  neural_parameter_count_min: number | null; neural_parameter_count_max: number | null;
  float32_parameter_bytes_max: number | null; artifact_bytes: number | null; scope: string;
}
function syntheticSpace(values: Partial<SyntheticSpace> = {}): SyntheticSpace {
  return {
    feature_dimensions_min: null, feature_dimensions_max: null, training_vocabulary_size_min: null,
    training_vocabulary_size_max: null, maximum_sequence_length: null, process_working_set_before_min_mb: null,
    process_working_set_after_max_mb: null, process_lifetime_peak_working_set_max_mb: null,
    neural_parameter_count_min: null, neural_parameter_count_max: null, float32_parameter_bytes_max: null,
    artifact_bytes: null, scope: 'Original synthetic space fixture; no measured corpus or model values.', ...values
  };
}

function comparisonFixture() {
  const classificationRows = classifiers.map(([family, family_label, algorithm, algorithm_label]) => ({
    family, family_label: String(family_label), algorithm, algorithm_label: String(algorithm_label),
    task: 'timebank_tlink', language: 'ro', accuracy_fraction: 0.5, macro_f1_fraction: 0.25,
    total_training_seconds: 2, total_prediction_seconds: 0.1, prediction_ms_per_row: 1,
    batched_rows_per_second: 1000, rows: 100,
    notes: ['Original synthetic software fixture; these are not NLP evaluation scores.'],
    space: undefined as SyntheticSpace | undefined
  }));
  // One additional task is enough to exercise the task filter without inventing a full
  // six-task measurement report. All twelve classification families remain represented.
  classificationRows.push({...classificationRows[1], task: 'timebank_event_tense', accuracy_fraction: 0.75});
  const bookRows = books.flatMap(([family, family_label, algorithm, algorithm_label]) =>
    ['en', 'ro'].map(language => ({
      family, family_label: String(family_label), algorithm, algorithm_label: String(algorithm_label), language,
      training_seconds: 3, assessment_seconds: 0.2, semantic_accuracy: null,
      metrics: {synthetic_geometric_distance: 0.25, synthetic_unavailable_metric: null} as Record<string, number | null>,
      scope: 'Original synthetic unlabeled numeric sample; no private book passages.',
      notes: ['Geometric fixture values do not constitute semantic accuracy.'],
      space: undefined as SyntheticSpace | undefined
    })));
  return {
    schema_version: 1, status: 'completed_algorithm_comparison',
    coverage: {families: 17, unique_variants: 19, classification_variants: 14, classification_families: 12,
      classification_tasks: 2, book_variants: 5},
    classification_primary_task: 'timebank_tlink', classification_rows: classificationRows, books_rows: bookRows,
    limitations: ['Synthetic software fixture only; no independent gold evaluation.']
  };
}

async function routeComparison(page: Page, body: string) {
  await page.route('**/results/algorithm-comparison.json', route => route.fulfill({contentType: 'application/json', body}));
}

test.beforeEach(async ({page}) => {
  // Only local UI assets may reach the network. The existing helper fulfills catalog and
  // prediction calls, and each comparison response is fulfilled separately below.
  await page.route('**/*', route => {
    const host = new URL(route.request().url()).hostname;
    return host === '127.0.0.1' || host === 'localhost' || host === '[::1]' ? route.continue() : route.abort('blockedbyclient');
  });
  await mockCatalog(page);
});

test('comparison renders seventeen families, primary TLINK, book N/A, filters and local downloads', async ({page}) => {
  const fixture = comparisonFixture();
  expect(new Set([...fixture.classification_rows, ...fixture.books_rows].map(row => row.family)).size).toBe(17);
  await routeComparison(page, JSON.stringify(fixture));
  await page.goto('/');
  await expect(page.locator('#comparison-status')).toHaveText('Published comparison loaded.');
  await expect(page.locator('#comparison-results')).toBeVisible();
  await expect(page.locator('#comparison-coverage')).toContainText('17 algorithm families');
  await expect(page.locator('#comparison-task')).toHaveValue('timebank_tlink');
  await expect(page.locator('#comparison-classification-rows tr')).toHaveCount(14);
  await expect(page.locator('#comparison-classification-rows td:nth-child(3)')).toHaveText(Array(14).fill('50.00%'));
  await expect(page.locator('#comparison-classification-rows td:nth-child(4)')).toHaveText(Array(14).fill('25.00%'));
  await expect(page.locator('#comparison-book-rows tr')).toHaveCount(10);
  await expect(page.locator('#comparison-book-rows td:nth-child(5)')).toHaveText(Array(10).fill('N/A · unlabeled passages'));
  await expect(page.locator('#comparison-classification-rows .comparison-space-unavailable')).toHaveCount(14);
  await expect(page.locator('#comparison-book-rows .comparison-space-unavailable')).toHaveCount(10);

  await page.getByLabel('Language', {exact: true}).selectOption('ro');
  await expect(page.locator('#comparison-classification-rows tr')).toHaveCount(14);
  await expect(page.locator('#comparison-book-rows tr')).toHaveCount(5);
  await page.getByLabel('Classification task', {exact: true}).selectOption('timebank_event_tense');
  await expect(page.locator('#comparison-classification-rows tr')).toHaveCount(1);
  await expect(page.locator('#comparison-classification-rows td:nth-child(3)')).toHaveText('75.00%');
  await expect(page.locator('#comparison-book-rows tr')).toHaveCount(5);
  await page.getByLabel('Language', {exact: true}).selectOption('en');
  await expect(page.locator('#comparison-classification-rows')).toContainText('No published classification results');
  await expect(page.locator('#comparison-book-rows tr')).toHaveCount(5);
  await expect(page.locator('#comparison-book-rows td:nth-child(2)')).toHaveText(Array(5).fill('English'));
  await page.getByLabel('Language', {exact: true}).selectOption('all');
  await page.getByLabel('Classification task', {exact: true}).selectOption('timebank_tlink');
  await expect(page.locator('#comparison-classification-rows tr')).toHaveCount(14);

  for (const [name, extension] of [['JSON data', 'json'], ['CSV table', 'csv'], ['Markdown report', 'md']] as const) {
    const link = page.locator('#comparison-downloads').getByRole('link', {name, exact: true});
    await expect(link).toHaveAttribute('href', '/results/algorithm-comparison.' + extension);
    await expect(link).toHaveAttribute('download', '');
    const href = await link.getAttribute('href');
    expect(new URL(href!, page.url()).origin).toBe(new URL(page.url()).origin);
  }
  // Browser downloads can bypass page routing. Fetch the real same-origin asset first,
  // then compare its complete bytes with the browser download instead of a routed mock.
  const csvLink = page.locator('#comparison-downloads').getByRole('link', {name: 'CSV table', exact: true});
  const csvUrl = new URL((await csvLink.getAttribute('href'))!, page.url());
  expect(csvUrl.origin).toBe(new URL(page.url()).origin);
  const csvResponse = await page.request.get(csvUrl.href);
  expect(csvResponse.status()).toBe(200);
  expect(new URL(csvResponse.url()).origin).toBe(csvUrl.origin);
  expect(csvResponse.headers()['content-type'] || '').toMatch(/^text\/csv(?:\s*;|$)/i);
  const expectedBytes = await csvResponse.body();
  const expectedColumns = ['config', 'task', 'language', 'status', 'train_seconds', 'predict_seconds',
    'batched_ms_per_row', 'batched_rows_per_second', 'accuracy_fraction', 'macro_f1_fraction', 'unsupervised_metric',
    'feature_dimensions_min', 'feature_dimensions_max', 'training_vocabulary_size_min', 'training_vocabulary_size_max',
    'maximum_sequence_length', 'process_working_set_before_min_mb', 'process_working_set_after_max_mb',
    'process_lifetime_peak_working_set_max_mb', 'neural_parameter_count_min', 'neural_parameter_count_max',
    'float32_parameter_bytes_max', 'artifact_bytes', 'space_scope', 'notes'];
  expect(expectedBytes.toString('utf8').split(/\r?\n/, 1)[0]).toBe(expectedColumns.map(column => '"' + column + '"').join(','));
  await csvResponse.dispose();
  const downloadPromise = page.waitForEvent('download');
  await csvLink.click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe('algorithm-comparison.csv');
  const stream = await download.createReadStream();
  if (!stream) throw new Error('Published CSV download has no readable stream.');
  const chunks: Buffer[] = []; for await (const chunk of stream) chunks.push(Buffer.from(chunk));
  expect(Buffer.concat(chunks)).toEqual(expectedBytes);
});

test('optional space details show fold dimensions, shared process memory, neural weights and exported storage', async ({page}) => {
  const fixture = comparisonFixture();
  fixture.classification_rows[0].space = syntheticSpace({feature_dimensions_min: 64, feature_dimensions_max: 96,
    process_working_set_before_min_mb: 100, process_working_set_after_max_mb: 180,
    process_lifetime_peak_working_set_max_mb: 200});
  fixture.classification_rows[10].space = syntheticSpace({training_vocabulary_size_min: 128, training_vocabulary_size_max: 256,
    maximum_sequence_length: 64, neural_parameter_count_min: 2000, neural_parameter_count_max: 2100,
    float32_parameter_bytes_max: 8400});
  fixture.books_rows[0].space = syntheticSpace({feature_dimensions_min: 32, feature_dimensions_max: 32, artifact_bytes: 16384});
  await routeComparison(page, JSON.stringify(fixture));
  await page.goto('/');
  await expect(page.locator('#comparison-status')).toHaveText('Published comparison loaded.');

  const native = page.locator('#comparison-classification-rows tr').filter({hasText: 'Class-indicator regression'});
  await native.getByText('Space and storage', {exact: true}).click();
  await expect(native.getByText('64–96', {exact: true})).toBeVisible();
  await expect(native.getByText('100 MiB', {exact: true})).toBeVisible();
  await expect(native.getByText('180 MiB', {exact: true})).toBeVisible();
  await expect(native.getByText('200 MiB', {exact: true})).toBeVisible();
  await expect(native.getByText('Unavailable', {exact: true})).toBeVisible();
  await expect(native.locator('.comparison-space')).toContainText('not isolated model memory');
  await expect(native.locator('.comparison-space')).toContainText('exported artifacts only');

  const neural = page.locator('#comparison-classification-rows tr').filter({hasText: 'Scratch MLP'});
  await neural.getByText('Space and storage', {exact: true}).click();
  await expect(neural.getByText('128–256', {exact: true})).toBeVisible();
  await expect(neural.getByText('64 tokens', {exact: true})).toBeVisible();
  await expect(neural.locator('.comparison-space-metrics')).toContainText(/2.?000–2.?100/);
  await expect(neural.locator('.comparison-space-metrics')).toContainText(/8.?400 B/);
  await expect(neural.getByText('Float32 parameter bytes (maximum estimate)', {exact: true})).toBeVisible();

  const book = page.locator('#comparison-book-rows tr').filter({hasText: 'Passage K-Means'}).first();
  await book.getByText('Space and storage', {exact: true}).click();
  await expect(book.locator('.comparison-space-metrics')).toContainText(/16.?384 B/);
  await expect(book.getByText('Exported artifact bytes', {exact: true})).toBeVisible();
});

test('malformed space numbers and reversed ranges fail inline without blocking the catalog', async ({page}) => {
  const fixture = comparisonFixture();
  fixture.classification_rows[0].space = syntheticSpace({feature_dimensions_min: 64, feature_dimensions_max: 96,
    process_working_set_after_max_mb: 180, artifact_bytes: 128});
  fixture.books_rows[0].space = syntheticSpace({artifact_bytes: 256});
  const valid = JSON.stringify(fixture);
  const invalidBodies = [
    valid.replace('"feature_dimensions_max":96', '"feature_dimensions_max":-1'),
    valid.replace('"process_working_set_after_max_mb":180', '"process_working_set_after_max_mb":1e999'),
    valid.replace('"artifact_bytes":128', '"artifact_bytes":"128"'),
    valid.replace('"artifact_bytes":256', '"artifact_bytes":-256'),
    valid.replace('"feature_dimensions_min":64', '"feature_dimensions_min":97')
  ];
  let body = invalidBodies[0];
  await page.route('**/results/algorithm-comparison.json', route => route.fulfill({contentType: 'application/json', body}));
  for (body of invalidBodies) {
    await page.goto('/');
    await expect(page.locator('#comparison-status')).toHaveAttribute('data-tone', 'error');
    await expect(page.locator('#comparison-status')).toContainText(/invalid format|incomplete/);
    await expect(page.locator('#comparison-results')).toBeHidden();
    await expect(page.getByRole('button', {name: 'Reload comparison', exact: true})).toBeVisible();
    await expect(page.locator('#catalog-status')).toHaveText('16 of 16 models available');
  }
});

test('invalid numeric data and incomplete coverage fail inline while prediction stays usable', async ({page}) => {
  const valid = JSON.stringify(comparisonFixture());
  const incomplete = comparisonFixture();
  incomplete.books_rows = incomplete.books_rows.filter(row => row.family !== 'dbscan');
  // NaN is not legal JSON. 1e999 is legal JSON syntax but parses to nonfinite Infinity,
  // so these separately exercise parsing failure and the explicit finite-value guard.
  const invalidBodies = [
    valid.replace('"accuracy_fraction":0.5', '"accuracy_fraction":NaN'),
    valid.replace('"total_training_seconds":2', '"total_training_seconds":1e999'),
    JSON.stringify(incomplete) // The declared count is still 17; the actual union is 16.
  ];
  let body = invalidBodies[0];
  await page.route('**/results/algorithm-comparison.json', route => route.fulfill({contentType: 'application/json', body}));
  for (body of invalidBodies) {
    await page.goto('/');
    await expect(page.locator('#comparison-status')).toHaveAttribute('data-tone', 'error');
    await expect(page.locator('#comparison-status')).toContainText(/invalid format|incomplete/);
    await expect(page.locator('#comparison-results')).toBeHidden();
    await expect(page.getByRole('button', {name: 'Reload comparison', exact: true})).toBeVisible();
    await expect(page.locator('#workbench')).toBeVisible();
    await expect(page.locator('#catalog-status')).toHaveText('16 of 16 models available');
    await page.locator('#input-text').fill('An original synthetic prediction remains usable.');
    await page.getByRole('button', {name: 'Predict label', exact: true}).click();
    await expect(page.locator('#predicted-label')).toHaveText('NAR');
  }
});

test('report labels, scopes, metric names and notes render as inert source text', async ({page}) => {
  const fixture = comparisonFixture();
  const script = '<script>globalThis.__comparison_xss = true</script>';
  const image = '<img src="/not-an-image" onerror="globalThis.__comparison_xss = true">';
  const note = '<b>Original literal note, not markup.</b>';
  fixture.classification_rows[0].algorithm_label = script;
  fixture.classification_rows[0].family_label = image;
  fixture.classification_rows[0].notes = [note];
  fixture.classification_rows[0].space = syntheticSpace({scope: script});
  fixture.books_rows[0].scope = image;
  fixture.books_rows[0].metrics = {'<strong>Literal metric name</strong>': 0.125};
  await routeComparison(page, JSON.stringify(fixture));
  await page.goto('/');
  await expect(page.locator('#comparison-status')).toHaveText('Published comparison loaded.');
  await expect(page.locator('#comparison-classification-rows .comparison-approach').first()).toHaveText(script);
  await expect(page.locator('#comparison-classification-rows .comparison-family').first()).toHaveText(image);
  const firstClassRow = page.locator('#comparison-classification-rows tr').first();
  await firstClassRow.getByText('Interpretation notes', {exact: true}).click();
  await expect(firstClassRow.getByText(note, {exact: true})).toBeVisible();
  await firstClassRow.getByText('Space and storage', {exact: true}).click();
  await expect(firstClassRow.locator('.comparison-space-scope')).toHaveText(script);
  await expect(page.locator('#comparison-book-rows').getByText(image, {exact: true})).toBeVisible();
  await expect(page.locator('#comparison-book-rows dt').filter({hasText: '<strong>Literal metric name</strong>'})).toHaveText('<strong>Literal metric name</strong>');
  await expect(page.locator('#comparison-results script, #comparison-results img, #comparison-results b, #comparison-results strong strong')).toHaveCount(0);
  expect(await page.evaluate(() => (window as Window & {__comparison_xss?: boolean}).__comparison_xss)).toBeUndefined();
  await expect(page.locator('#catalog-status')).toHaveText('16 of 16 models available');
});
