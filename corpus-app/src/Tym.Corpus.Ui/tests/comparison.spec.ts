import { test, expect, type Page } from 'playwright/test';
import { mockCatalog } from './helpers';

// Original software fixtures only. Their numbers are not measured model results, and no
// private corpus, book, model, generated public report, or remote API is read by these tests.
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

function comparisonFixture() {
  const classificationRows = classifiers.map(([family, family_label, algorithm, algorithm_label]) => ({
    family, family_label: String(family_label), algorithm, algorithm_label: String(algorithm_label),
    task: 'timebank_tlink', language: 'ro', accuracy_fraction: 0.5, macro_f1_fraction: 0.25,
    total_training_seconds: 2, total_prediction_seconds: 0.1, prediction_ms_per_row: 1,
    batched_rows_per_second: 1000, rows: 100,
    notes: ['Original synthetic software fixture; these are not NLP evaluation scores.']
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
      notes: ['Geometric fixture values do not constitute semantic accuracy.']
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
  const csv = 'family,accuracy_fraction\nsynthetic_software_only,0.5\n';
  await page.route('**/results/algorithm-comparison.csv', route => route.fulfill({contentType: 'text/csv', body: csv}));
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
  const downloadPromise = page.waitForEvent('download');
  await page.locator('#comparison-downloads').getByRole('link', {name: 'CSV table', exact: true}).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe('algorithm-comparison.csv');
  const stream = await download.createReadStream();
  if (!stream) throw new Error('Synthetic CSV download has no readable stream.');
  let downloaded = ''; for await (const chunk of stream) downloaded += chunk.toString();
  expect(downloaded).toBe(csv);
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
  await expect(page.locator('#comparison-book-rows').getByText(image, {exact: true})).toBeVisible();
  await expect(page.locator('#comparison-book-rows dt').filter({hasText: '<strong>Literal metric name</strong>'})).toHaveText('<strong>Literal metric name</strong>');
  await expect(page.locator('#comparison-results script, #comparison-results img, #comparison-results b, #comparison-results strong strong')).toHaveCount(0);
  expect(await page.evaluate(() => (window as Window & {__comparison_xss?: boolean}).__comparison_xss)).toBeUndefined();
  await expect(page.locator('#catalog-status')).toHaveText('16 of 16 models available');
});
