import { test, expect } from 'playwright/test';
const publicUrl = process.env.TYM_PUBLIC_UI_URL;
test.describe('opt-in public deployment smoke (original authored prose only)', () => {
  // Both Container Apps may scale to zero. Give initial HTTPS navigation and
  // catalog startup separate bounded time before checking the deployed behavior.
  test.setTimeout(120000);
  test.skip(!publicUrl, 'Set TYM_PUBLIC_UI_URL after deployment. Private book passages are never used here.');
  test.beforeEach(async ({page}) => {
    if (!publicUrl || new URL(publicUrl).protocol !== 'https:') throw new Error('Public smoke requires an explicit HTTPS UI URL.');
    await page.goto(publicUrl, { timeout: 60000 });
    await expect(page.locator('#catalog-status')).toHaveText('16 of 16 models available', { timeout: 65000 });
  });
  test('completed algorithm comparison exposes quality, space details and exact CSV downloads', async ({page}) => {
    await page.getByRole('link', {name: 'Compare algorithms', exact: true}).click();
    await expect(page.locator('#comparison-status')).toHaveText('Published comparison loaded.');
    await expect(page.locator('#comparison-coverage')).toContainText('17 algorithm families');
    await expect(page.locator('#comparison-task')).toHaveValue('timebank_tlink');
    await expect(page.locator('#comparison-classification-rows tr')).toHaveCount(14);
    await expect(page.locator('#comparison-book-rows tr')).toHaveCount(10);
    await expect(page.locator('#comparison-results .comparison-space')).toHaveCount(24);
    const native = page.locator('#comparison-classification-rows tr').first();
    await native.getByText('Space and storage', {exact: true}).click();
    await expect(native.locator('.comparison-space')).toContainText('Feature dimensions');
    await expect(native.locator('.comparison-space')).toContainText('not isolated model memory');
    await expect(page.locator('#comparison-book-rows td:nth-child(5)')).toHaveText(Array(10).fill('N/A · unlabeled passages'));
    await page.locator('#comparison-language').selectOption('en');
    await expect(page.locator('#comparison-classification-rows')).toContainText('No published classification results');
    await expect(page.locator('#comparison-book-rows tr')).toHaveCount(5);
    const link = page.locator('#comparison-downloads').getByRole('link', {name: 'CSV table', exact: true});
    const url = new URL((await link.getAttribute('href'))!, page.url());
    expect(url.origin).toBe(new URL(page.url()).origin);
    const response = await page.request.get(url.href);
    expect(response.status()).toBe(200);
    expect(response.headers()['content-type']).toMatch(/^text\/csv(?:\s*;|$)/i);
    const expectedBytes = await response.body();
    expect(expectedBytes.toString('utf8').split(/\r?\n/).filter(Boolean)).toHaveLength(95);
    await response.dispose();
    const pending = page.waitForEvent('download'); await link.click();
    const download = await pending, stream = await download.createReadStream();
    expect(download.suggestedFilename()).toBe('algorithm-comparison.csv');
    if (!stream) throw new Error('Published CSV download has no readable stream.');
    const chunks: Buffer[] = []; for await (const chunk of stream) chunks.push(Buffer.from(chunk));
    expect(Buffer.concat(chunks)).toEqual(expectedBytes);
  });
  test('English baseline returns the deployed memory snapshot', async ({page}) => {
    await page.locator('#model-select').selectOption('segment_type_en');
    await page.locator('#input-text').fill('Years after leaving home, Mara remembered the blue room and the letter her brother had left.');
    await page.getByRole('button',{name:'Predict label',exact:true}).click(); await expect(page.locator('#predicted-label')).toHaveText('REM');
    await expect(page.locator('#prediction-limit')).toContainText('Independent accuracy');
  });
  test('Romanian structured tense and original document review/export work', async ({page}) => {
    await page.locator('#model-select').selectOption('timebank_event_tense_ro_structured');
    await page.locator('#input-text').fill('Context: Mâine Mara [TARGET] va pleca [/TARGET] spre munte.\nEvent: va pleca');
    await page.getByRole('button',{name:'Predict label',exact:true}).click(); await expect(page.locator('#predicted-label')).toHaveText('FUTURE');
    const original='Ieri, Mara a ajuns la Iași. Astăzi pleacă spre munte.';
    await page.locator('#document-language').selectOption('ro'); await page.locator('#document-text').fill(original);
    await page.getByRole('button',{name:'Analyze passage',exact:true}).click(); await expect(page.locator('#preserved-source')).toHaveValue(original);
    await page.locator('#segment-rows').getByRole('button',{name:/^Review /}).first().click();
    // Verify the final deployment's manual-annotation provenance contract with
    // authored prose. Selection offsets remain in the unchanged source.
    await page.locator('#preserved-source').press('Control+Home');
    for(let i=0;i<6;i++) await page.locator('#preserved-source').press('ArrowRight');
    for(let i=0;i<4;i++) await page.locator('#preserved-source').press('Shift+ArrowRight');
    await page.getByRole('button',{name:'Add time segment',exact:true}).click();
    await page.locator('#segment-rows tr:last-child select').selectOption('NAR');
    await page.locator('#preserved-source').press('Control+Home');
    for(let i=0;i<6;i++) await page.locator('#preserved-source').press('ArrowRight');
    for(let i=0;i<4;i++) await page.locator('#preserved-source').press('Shift+ArrowRight');
    await page.locator('#new-mention-kind').selectOption('ACTOR');
    await page.getByRole('button',{name:'Add mention',exact:true}).click();
    const endpoints=await page.locator('#relation-from option').evaluateAll(options=>options.map(option=>(option as HTMLOptionElement).value));
    await page.locator('#relation-from').selectOption(endpoints[0]);
    await page.locator('#relation-to').selectOption(endpoints[1]);
    await page.locator('#relation-label').selectOption('BEFORE');
    await page.getByRole('button',{name:'Add draft relation',exact:true}).click();
    await page.locator('#relation-rows').getByRole('button',{name:/^Review /}).first().click();
    await page.getByRole('button',{name:'Validate reviewed graph',exact:true}).click(); await expect(page.locator('#validation-state')).toContainText('Consistent annotation structure');
    await expect(page.locator('#graph-scope')).toContainText('PARTIAL');
    const pending = page.waitForEvent('download'); await page.getByRole('button',{name:'Download annotation JSON',exact:true}).click();
    const download=await pending,stream=await download.createReadStream(); let json=''; for await(const chunk of stream) json+=chunk.toString();
    expect(JSON.parse(json).text).toBe(original);
  });
});
