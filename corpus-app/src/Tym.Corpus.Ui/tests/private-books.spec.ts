import { test, expect, type Page } from 'playwright/test';
import { readFileSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve, join } from 'node:path';
import { execFileSync } from 'node:child_process';

interface PrivateSample { language:string; text:string; source:string; document_id:string; }
const configured = Boolean(process.env.TYM_PRIVATE_BOOK_SAMPLES || process.env.TYM_PRIVATE_BOOKS_ZIP);
let samples: PrivateSample[] = [];
let scratch: string | undefined;
const api = process.env.TYM_UI_TEST_API_URL || 'http://127.0.0.1:8871';
function isLocal(url: string): boolean { return ['127.0.0.1','localhost','[::1]'].includes(new URL(url).hostname); }

test.describe('private book passages (consistency checks, no accuracy claim)', () => {
  test.skip(!configured, 'Set TYM_PRIVATE_BOOKS_ZIP or TYM_PRIVATE_BOOK_SAMPLES to enable local private corpus tests.');
  test.beforeAll(() => {
    if (!isLocal(api)) throw new Error('Private book tests only permit a localhost API.');
    let path = process.env.TYM_PRIVATE_BOOK_SAMPLES;
    if (!path) {
      scratch = mkdtempSync(join(tmpdir(),'tym-private-ui-'));
      path = join(scratch,'samples.json');
      const dll = process.env.TYM_CORPUS_DATA_DLL || resolve('../../tools/Tym.Corpus.Data/bin/Release/net10.0/Tym.Corpus.Data.dll');
      execFileSync('dotnet',[dll,'book-sample','--zip',process.env.TYM_PRIVATE_BOOKS_ZIP!,'--out',path,'--count','5','--max-chars','1200'],{stdio:'pipe'});
    }
    samples = JSON.parse(readFileSync(path,'utf8')) as PrivateSample[];
    if (samples.length < 5 || samples.some(s => !s.text || s.language !== 'ro')) throw new Error('Expected five private Romanian book samples. Contents are redacted from this error.');
  });
  test.afterAll(() => { if (scratch) rmSync(scratch,{recursive:true,force:true}); });
  async function localPage(page: Page): Promise<void> {
    // Fail closed: no private passage, API request, trace or export can go to Azure.
    await page.route('**/*', route => isLocal(route.request().url()) ? route.continue() : route.abort('blockedbyclient'));
    await page.goto('/');
    await expect(page.locator('#catalog-status')).toContainText('models available');
    const configuredApi = await page.evaluate(() => (window as Window & {TYM_CONFIG?:{apiBaseUrl:string}}).TYM_CONFIG?.apiBaseUrl || '');
    if (!isLocal(configuredApi)) throw new Error('The UI config points outside localhost. Private tests stopped.');
  }
  for (let index=0; index<5; index++) {
    test(`Romanian book sample ${index+1}: score, review, validate, export unchanged source`, async ({page}) => {
      await localPage(page);
      const sample = samples[index];
      await page.locator('#model-select').selectOption('segment_type_ro');
      await page.locator('#input-text').fill(sample.text);
      await page.getByRole('button',{name:'Predict label',exact:true}).click();
      await expect(page.locator('#request-status')).toHaveText('Prediction complete.');
      const predicted = await page.locator('#predicted-label').innerText();
      expect(['NAR','REM','SUP','GEN','FIC'].includes(predicted),'Output belongs to the task inventory. This is not a gold label check.').toBe(true);
      await page.locator('#document-language').selectOption('ro');
      await page.locator('#document-date').fill('2026-10-02');
      await page.locator('#document-text').fill(sample.text);
      await page.getByRole('button',{name:'Analyze passage',exact:true}).click();
      await expect(page.locator('#document-results')).toBeVisible();
      const preserved = await page.locator('#preserved-source').inputValue();
      expect(preserved === sample.text,'Private source equality (contents redacted).').toBe(true);
      const label = page.locator('#segment-rows select').first(); await label.selectOption('REM');
      await page.locator('#segment-rows').getByRole('button',{name:/^Review /}).first().click();
      await page.getByRole('button',{name:'Validate reviewed graph',exact:true}).click();
      await expect(page.locator('#validation-state')).toContainText('Consistent annotation structure');
      const pendingDownload = page.waitForEvent('download'); await page.getByRole('button',{name:'Download annotation JSON',exact:true}).click();
      const download = await pendingDownload, stream = await download.createReadStream(); let json=''; for await (const chunk of stream) json += chunk.toString();
      const exported = JSON.parse(json) as {text:string; offset_encoding:string; segments:{start:number;end:number;text:string;review_status:string;provenance:Record<string,{method:string}>}[]};
      expect(exported.text === sample.text,'Export source equality (contents redacted).').toBe(true);
      expect(exported.offset_encoding).toBe('utf-16');
      expect(exported.segments.every(span=>exported.text.slice(span.start,span.end)===span.text),'Every annotation is an exact private source slice (contents redacted).').toBe(true);
      expect(exported.segments[0].review_status).toBe('reviewed');
      expect(exported.segments[0].provenance['label'].method).toBe('human_corrected');
    });
  }
  test('English narrative flow uses original text because Books.zip supplies Romanian passages', async ({page}) => {
    await localPage(page); await page.locator('#model-select').selectOption('segment_type_en');
    await page.locator('#input-text').fill('Years later, Mara remembered the blue room where her brother had waited.');
    await page.getByRole('button',{name:'Predict label',exact:true}).click(); await expect(page.locator('#request-status')).toHaveText('Prediction complete.');
    await page.locator('#document-language').selectOption('en'); await page.locator('#document-text').fill('Mara arrived yesterday. Today she leaves for the mountains.');
    await page.getByRole('button',{name:'Analyze passage',exact:true}).click(); await expect(page.locator('#document-results')).toBeVisible();
  });
  test('real API accepts manual source annotations and their complete provenance', async ({page}) => {
    await localPage(page);
    await page.locator('#document-text').fill('Mara arrived yesterday. Today she leaves for Iași.');
    await page.getByRole('button',{name:'Analyze passage',exact:true}).click(); await expect(page.locator('#document-results')).toBeVisible();
    await page.locator('#preserved-source').press('Control+Home');
    for(let i=0;i<4;i++) await page.locator('#preserved-source').press('Shift+ArrowRight');
    await page.getByRole('button',{name:'Add time segment',exact:true}).click();
    await page.locator('#segment-rows tr:last-child select').selectOption('NAR');
    await page.locator('#preserved-source').press('Control+Home');
    for(let i=0;i<4;i++) await page.locator('#preserved-source').press('Shift+ArrowRight');
    await page.locator('#new-mention-kind').selectOption('ACTOR'); await page.getByRole('button',{name:'Add mention',exact:true}).click();
    const endpoints = await page.locator('#relation-from option').evaluateAll(options=>options.map(option=>(option as HTMLOptionElement).value));
    await page.locator('#relation-from').selectOption(endpoints[0]); await page.locator('#relation-to').selectOption(endpoints[1]); await page.locator('#relation-label').selectOption('BEFORE');
    await page.getByRole('button',{name:'Add draft relation',exact:true}).click();
    await page.locator('#relation-rows').getByRole('button',{name:/^Review /}).first().click();
    await page.getByRole('button',{name:'Validate reviewed graph',exact:true}).click();
    await expect(page.locator('#validation-state')).toContainText('Consistent annotation structure');
    await expect(page.locator('#graph-scope')).toContainText('PARTIAL');
  });
});
