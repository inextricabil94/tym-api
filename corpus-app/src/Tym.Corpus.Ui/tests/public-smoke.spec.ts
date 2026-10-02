import { test, expect } from 'playwright/test';
const publicUrl = process.env.TYM_PUBLIC_UI_URL;
test.describe('opt-in public deployment smoke (original authored prose only)', () => {
  test.skip(!publicUrl, 'Set TYM_PUBLIC_UI_URL after deployment. Private book passages are never used here.');
  test.beforeEach(async ({page}) => {
    if (!publicUrl || new URL(publicUrl).protocol !== 'https:') throw new Error('Public smoke requires an explicit HTTPS UI URL.');
    await page.goto(publicUrl);
    await expect(page.locator('#catalog-status')).toHaveText('16 of 16 models available');
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
    await page.getByRole('button',{name:'Validate reviewed graph',exact:true}).click(); await expect(page.locator('#validation-state')).toContainText('Consistent annotation structure');
    const pending = page.waitForEvent('download'); await page.getByRole('button',{name:'Download annotation JSON',exact:true}).click();
    const download=await pending,stream=await download.createReadStream(); let json=''; for await(const chunk of stream) json+=chunk.toString();
    expect(JSON.parse(json).text).toBe(original);
  });
});
