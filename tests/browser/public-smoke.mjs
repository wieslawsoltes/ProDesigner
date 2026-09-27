import assert from 'node:assert/strict';
import { chromium } from '@playwright/test';

const url = process.env.PRODESIGNER_PUBLIC_URL;
assert(url?.startsWith('https://'), 'An HTTPS deployment URL is required.');
const browser = await chromium.launch({
  args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader']
});
try {
  const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const deployment = new URL(url);
  deployment.searchParams.set('validation', process.env.GITHUB_SHA ?? 'manual');
  await page.goto(deployment.href, { waitUntil: 'domcontentloaded', timeout: 120000 });
  await page.waitForFunction(() => window.prodesigner?.state().ready, null, { timeout: 180000 });
  await page.waitForFunction(() => !document.querySelector('#boot'));
  const workspace = () => page.evaluate(() => {
    const envelope = JSON.parse(window.prodesigner.exportWorkspace());
    if (envelope.format !== 'ProDesigner.Workspace') throw new Error('Unexpected workspace format.');
    return JSON.parse(envelope.payload);
  });
  const initial = await workspace();
  assert.equal(await page.evaluate(() => window.prodesigner.state().release), '0.4.0.0');
  assert.equal(initial.schemaVersion, 2, 'The deployed workbench must serve the schema-2 authoring release.');
  await page.waitForTimeout(700);
  await page.screenshot({ path: 'public-workbench.png' });

  const instance = await page.evaluate(() => {
    window.prodesigner.select('RevenueCard');
    const id = window.prodesigner.captureComponent('Published component');
    window.prodesigner.select('DashboardCanvas');
    return window.prodesigner.insertComponent(id);
  });
  assert.equal((await workspace()).designSystem.instances[0].id, instance);
  await page.evaluate(() => window.prodesigner.command('components'));
  await page.waitForTimeout(200);
  await page.screenshot({ path: 'public-components.png' });
  await page.keyboard.press('Escape');

  await page.evaluate(() => {
    window.prodesigner.configurePrototype(1, 'SignInButton', 2);
    window.prodesigner.command('prototype-run');
  });
  await page.waitForFunction(() => window.prodesigner.prototypeBounds('SignInButton')?.width > 0);
  const bounds = await page.evaluate(() => window.prodesigner.prototypeBounds('SignInButton'));
  await page.mouse.click(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
  await page.waitForFunction(() => window.prodesigner.prototypeState()?.document === 'Settings.axaml');
  assert.equal(await page.evaluate(() => window.prodesigner.prototypeState().backCount), 1);
  await page.screenshot({ path: 'public-prototype-navigation.png' });
  await page.evaluate(() => window.prodesigner.prototypeBack());
  assert.equal(await page.evaluate(() => window.prodesigner.prototypeState().document), 'SignIn.axaml');
  await page.keyboard.press('Escape');
  const afterPrototype = await workspace();
  assert.equal(afterPrototype.documents[1].source, initial.documents[1].source);
  assert.equal(afterPrototype.documents[2].source, initial.documents[2].source);

  const edited = await page.evaluate(() => {
    window.prodesigner.command('new');
    window.prodesigner.insert('Path');
    return window.prodesigner.state();
  });
  assert(edited.valid && edited.source.includes('Path1'), 'Published authoring must produce valid Path XAML.');
  await page.evaluate(() => {
    window.prodesigner.setSource('<Canvas xmlns="https://github.com/avaloniaui"><Path Name="Left" Data="M0 0H60V60H0Z" Fill="Red" Canvas.Left="30" Canvas.Top="30"/><Path Name="Right" Data="M0 0H60V60H0Z" Fill="Blue" Canvas.Left="60" Canvas.Top="30"/></Canvas>');
    window.prodesigner.command('select-all'); window.prodesigner.command('path-union');
  });
  const booleanResult = await page.evaluate(() => window.prodesigner.state());
  assert.equal((booleanResult.source.match(/<Path\b/g) ?? []).length, 1);
  assert(booleanResult.valid);
  assert.deepEqual(errors, []);
  await page.waitForTimeout(300);
  await page.screenshot({ path: 'public-vector-authoring.png' });
  console.log(`Verified published schema-2 workbench, linked components, pointer-driven prototype navigation and vector authoring at ${url}`);
} finally {
  await browser.close();
}
