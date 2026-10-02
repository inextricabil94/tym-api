import { test, expect } from 'playwright/test';
import { mockCatalog, mockWorkbench, modelIds, baselineIds, analyzeSynthetic, originalText } from './helpers';

test.beforeEach(async ({page}) => { await mockCatalog(page); await mockWorkbench(page); });

test('catalog supports all 16 models and retains both original baseline examples', async ({page}) => {
  await page.goto('/');
  await expect(page.locator('#catalog-status')).toHaveText('16 of 16 models available');
  await expect(page.locator('#model-select option')).toHaveCount(16);
  for (const id of modelIds) {
    await page.getByLabel('Select a classifier',{exact:true}).selectOption(id);
    for (const example of ['Example 1','Example 2']) {
      await page.getByRole('button',{name:example,exact:true}).click();
      expect(await page.locator('#input-text').inputValue()).not.toBe('');
      await page.getByRole('button',{name:'Predict label',exact:true}).click();
      await expect(page.locator('#predicted-label')).toHaveText('NAR');
    }
  }
  expect(baselineIds).toHaveLength(10);
});

test('edits retain per-model drafts and hide stale output; blank input disables scoring', async ({page}) => {
  await page.goto('/'); await expect(page.locator('#catalog-status')).toContainText('16 of 16');
  await page.locator('#input-text').fill('An original edited draft.');
  await page.locator('#model-select').selectOption('segment_type_ro');
  await page.locator('#model-select').selectOption('segment_type_en');
  await expect(page.locator('#input-text')).toHaveValue('An original edited draft.');
  await page.getByRole('button',{name:'Predict label',exact:true}).click(); await expect(page.locator('#prediction-result')).toBeVisible();
  await page.locator('#input-text').fill(''); await expect(page.locator('#prediction-result')).toBeHidden();
  await expect(page.getByRole('button',{name:'Predict label',exact:true})).toBeDisabled();
});

test('catalog connection errors allow explicit retry', async ({page}) => {
  let attempts = 0;
  await page.route('**/v1/models', async route => { if (++attempts === 1) await route.abort('failed'); else await route.fulfill({json:{models:[{model_id:'segment_type_en',language:'en',available:true,labels:['NAR']}]}}); });
  await page.goto('/'); await expect(page.locator('#catalog-status')).toHaveText('API connection unavailable');
  await expect(page.getByRole('button',{name:'Predict label',exact:true})).toBeDisabled();
  await page.getByRole('button',{name:'Reconnect to the API',exact:true}).click();
  await expect(page.locator('#catalog-status')).toHaveText('1 of 1 models available');
});

test('prediction failure is visible and a second request succeeds', async ({page}) => {
  let attempts = 0;
  await page.route('**/v1/predictions', route => route.fulfill(++attempts === 1 ? {status:503,json:{error:'Model temporarily unavailable.'}} : {json:{predicted_label:'REM'}}));
  await page.goto('/'); await expect(page.locator('#catalog-status')).toContainText('16 of 16');
  await page.getByRole('button',{name:'Predict label',exact:true}).click(); await expect(page.locator('#request-status')).toHaveText('Model temporarily unavailable.');
  await page.getByRole('button',{name:'Predict label',exact:true}).click(); await expect(page.locator('#predicted-label')).toHaveText('REM');
});

test('report links stay on the UI origin and phone layout has no page overflow', async ({page}) => {
  await page.setViewportSize({width:390,height:844}); await page.goto('/');
  await expect(page.locator('#pdf-link')).toHaveAttribute('href',/\/results\/Deep_Learning_Spatial_Temporal_Semantics_Updated_Results_20261002\.pdf$/);
  await expect(page.locator('#pptx-link')).toHaveAttribute('href',/\/results\/TYM_MLNET_Updated_Results_20261002\.pptx$/);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});

test('source is preserved, labels are editable, and reviewed provenance is explicit', async ({page}) => {
  await analyzeSynthetic(page); await expect(page.locator('#preserved-source')).toHaveValue(originalText);
  await page.getByLabel('Narrative mode for ts1',{exact:true}).selectOption('REM');
  await page.getByRole('button',{name:'Review ts1',exact:true}).click();
  await expect(page.locator('#annotation-evidence')).toContainText('human_corrected');
  await expect(page.locator('#annotation-evidence')).toContainText('not independently adjudicated');
  await page.getByLabel('Kind for e1',{exact:true}).selectOption('TIME');
  await page.getByLabel('Normalized value for e1',{exact:true}).fill('2026-10-02');
  await page.getByLabel('Normalized value for e1',{exact:true}).press('Tab');
  await expect(page.locator('#preserved-source')).toHaveValue(originalText);
});

test('reviewed temporal edges show partial order and cycles block export', async ({page}) => {
  await analyzeSynthetic(page);
  await page.locator('#relation-from').selectOption('ts1'); await page.locator('#relation-to').selectOption('ts2'); await page.locator('#relation-label').selectOption('BEFORE');
  await page.getByRole('button',{name:'Add draft relation',exact:true}).click();
  await page.getByRole('button',{name:'Validate reviewed graph',exact:true}).click(); await expect(page.locator('#graph-scope')).toContainText('UNKNOWN');
  await page.getByRole('button',{name:'Review r1',exact:true}).click();
  await page.getByRole('button',{name:'Validate reviewed graph',exact:true}).click(); await expect(page.locator('#graph-scope')).toContainText('PARTIAL');
  await expect(page.locator('#temporal-graph svg')).toBeVisible();
  await page.locator('#relation-from').selectOption('ts2'); await page.locator('#relation-to').selectOption('ts1');
  await page.getByRole('button',{name:'Add draft relation',exact:true}).click(); await page.getByRole('button',{name:'Review r2',exact:true}).click();
  await page.getByRole('button',{name:'Validate reviewed graph',exact:true}).click(); await expect(page.locator('#validation-diagnostics')).toContainText('temporal_cycle');
  await expect(page.getByRole('button',{name:'Download annotation JSON',exact:true})).toBeDisabled();
});

test('validated export preserves source, schema, and reviewer provenance', async ({page}) => {
  await analyzeSynthetic(page); await page.getByRole('button',{name:'Review ts1',exact:true}).click();
  await page.getByRole('button',{name:'Validate reviewed graph',exact:true}).click();
  const downloadPromise = page.waitForEvent('download'); await page.getByRole('button',{name:'Download annotation JSON',exact:true}).click();
  const download = await downloadPromise; const stream = await download.createReadStream(); let json=''; for await (const chunk of stream) json += chunk.toString();
  const exported = JSON.parse(json); expect(exported.text).toBe(originalText); expect(exported.offset_encoding).toBe('utf-16'); expect(exported.segments[0].review_status).toBe('reviewed'); expect(exported.segments[0].provenance.review_status.method).toBe('user_supplied');
});

test('manually added segments, mentions, and relations retain required field provenance', async ({page}) => {
  await analyzeSynthetic(page);
  await page.locator('#preserved-source').press('Control+Home');
  for(let i=0;i<4;i++) await page.locator('#preserved-source').press('Shift+ArrowRight');
  await page.getByRole('button',{name:'Add time segment',exact:true}).click();
  await page.locator('#segment-rows tr:last-child select').selectOption('NAR');
  await page.locator('#preserved-source').press('Control+Home');
  for(let i=0;i<4;i++) await page.locator('#preserved-source').press('Shift+ArrowRight');
  await page.locator('#new-mention-kind').selectOption('ACTOR'); await page.getByRole('button',{name:'Add mention',exact:true}).click();
  await page.locator('#relation-from').selectOption('ts1'); await page.locator('#relation-to').selectOption('ts2'); await page.getByRole('button',{name:'Add draft relation',exact:true}).click();
  await page.getByRole('button',{name:'Validate reviewed graph',exact:true}).click(); await expect(page.locator('#validation-state')).toContainText('Consistent annotation structure');
});
