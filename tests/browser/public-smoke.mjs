import assert from 'node:assert/strict';
import { chromium } from '@playwright/test';

const url = process.env.PRODESIGNER_PUBLIC_URL;
assert(url?.startsWith('https://'), 'An HTTPS deployment URL is required.');
const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
try {
  const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 120000 });
  await page.waitForFunction(() => window.prodesigner?.state().ready, null, { timeout: 180000 });
  await page.waitForFunction(() => !document.querySelector('#boot'));
  const workspace = await page.evaluate(() => JSON.parse(window.prodesigner.exportWorkspace()));
  assert.equal(workspace.format, 'ProDesigner.Workspace');
  await page.waitForTimeout(1000);
  await page.screenshot({ path: 'public-workbench.png' });
  const edited = await page.evaluate(() => {
    window.prodesigner.command('new');
    window.prodesigner.insert('Path');
    return window.prodesigner.state();
  });
  assert(edited.valid && edited.source.includes('Path1'), 'Published authoring engine must produce valid Path XAML.');
  assert.deepEqual(errors, []);
  await page.waitForTimeout(400);
  await page.screenshot({ path: 'public-vector-authoring.png' });
  console.log(`Verified published workbench, workspace codec and authoring at ${url}`);
} finally {
  await browser.close();
}
