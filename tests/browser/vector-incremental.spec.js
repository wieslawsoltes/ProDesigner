import { test, expect } from '@playwright/test';

test('native path Booleans, analytic arcs and incremental controls run in the actual WebAssembly workbench', async ({ page }, testInfo) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await page.waitForFunction(() => window.prodesigner?.state().ready, null, { timeout: 150000 });
  await expect(page.locator('#boot')).toHaveCount(0);
  await page.evaluate(() => {
    window.prodesigner.command('new');
    window.prodesigner.setSource(`<Canvas xmlns="https://github.com/avaloniaui"><Path Name="A" Data="F1 M0 0h100v80H0Z" Fill="#8570D8" Canvas.Left="40" Canvas.Top="40"/><Path Name="B" Data="M0 0H100V80H0Z" Fill="#CD97D2" Canvas.Left="100" Canvas.Top="40"/></Canvas>`);
  });
  await page.waitForTimeout(500);
  const original = await page.evaluate(() => window.prodesigner.state().source);
  await page.evaluate(() => { window.prodesigner.command('select-all'); window.prodesigner.command('path-union'); });
  let state = await page.evaluate(() => window.prodesigner.state());
  expect(state.valid).toBe(true);
  expect(state.source.match(/<Path\b/g)).toHaveLength(1);
  expect(state.source).not.toContain('Name="B"');
  await page.evaluate(() => window.prodesigner.command('undo'));
  expect(await page.evaluate(() => window.prodesigner.state().source)).toBe(original);

  await page.evaluate(() => {
    window.prodesigner.select('A');
    window.prodesigner.setProperty('Data', 'F1 M10 40 C25 5 65 5 80 40 S120 75 140 40 A35 25 20 0 1 210 65 z');
  });
  await page.waitForTimeout(500);
  state = await page.evaluate(() => window.prodesigner.state());
  expect(state.syntaxUpdate).toBe('AttributeValues');
  const deltaCount = state.previewDeltas;
  await page.evaluate(() => window.prodesigner.setProperty('Fill', '#71BDAA'));
  await page.waitForTimeout(500);
  state = await page.evaluate(() => window.prodesigner.state());
  expect(state.previewDeltas).toBeGreaterThan(deltaCount);
  expect(state.valid).toBe(true);
  await page.evaluate(() => window.prodesigner.command('vector'));
  await page.waitForTimeout(250);
  await page.screenshot({ path: testInfo.outputPath('prodesigner-arcs-editor.png') });
  await page.keyboard.press('Escape');
  await page.evaluate(() => {
    window.prodesigner.setProperty('Stroke', '#304459');
    window.prodesigner.setProperty('StrokeThickness', '6');
    window.prodesigner.command('stroke-outline');
  });
  expect(await page.evaluate(() => window.prodesigner.state().valid)).toBe(true);
  await page.waitForTimeout(250);
  await page.screenshot({ path: testInfo.outputPath('prodesigner-vector-outline.png') });
  expect(errors).toEqual([]);
});
