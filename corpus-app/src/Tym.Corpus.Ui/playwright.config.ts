import { defineConfig } from 'playwright/test';

const api = process.env.TYM_UI_TEST_API_URL || 'http://127.0.0.1:8871';
const publicUrl = process.env.TYM_PUBLIC_UI_URL;
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  timeout: 60000,
  expect: { timeout: 15000 },
  reporter: 'list',
  outputDir: 'test-results',
  use: {
    baseURL: publicUrl || 'http://127.0.0.1:8891',
    channel: 'chrome',
    headless: true,
    // Private book prose must never be copied into test traces, videos or screenshots.
    trace: 'off', screenshot: 'off', video: 'off',
    acceptDownloads: true
  },
  webServer: publicUrl ? undefined : {
    command: 'dotnet bin/Release/net10.0/Tym.Corpus.Ui.dll --urls http://127.0.0.1:8891',
    url: 'http://127.0.0.1:8891/health',
    env: { TYM_API_BASE_URL: api },
    reuseExistingServer: true,
    timeout: 120000
  }
});
