import { test, expect } from '@playwright/test';

test('linked components, overrides, persistent history and real prototype pointer navigation', async ({ page }, testInfo) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await page.waitForFunction(() => window.prodesigner?.state().ready, null, { timeout: 150000 });
  await expect(page.locator('#boot')).toHaveCount(0);
  const workspace = () => page.evaluate(() => JSON.parse(JSON.parse(window.prodesigner.exportWorkspace()).payload));
  const initial = await workspace();
  const componentId = await page.evaluate(() => {
    window.prodesigner.select('RevenueCard');
    return window.prodesigner.captureComponent('Reusable revenue');
  });
  const instanceId = await page.evaluate(id => {
    window.prodesigner.select('DashboardCanvas');
    return window.prodesigner.insertComponent(id);
  }, componentId);
  expect((await workspace()).designSystem.instances).toHaveLength(1);
  await page.evaluate(id => window.prodesigner.setComponentOverride(id, '$root', 'Width', '288'), instanceId);
  const definition = (await workspace()).designSystem.components[0];
  await page.evaluate(({ id, source }) => window.prodesigner.updateComponent(id, source), {
    id: componentId, source: definition.xaml.replace('Total revenue', 'Linked revenue')
  });
  let state = await workspace();
  expect(state.designSystem.components[0].revision).toBe(2);
  expect(state.documents[0].source).toContain('Linked revenue');
  expect(state.documents[0].source).toContain('Width="288"');
  await page.evaluate(() => window.prodesigner.command('studio-undo'));
  state = await workspace();
  expect(state.designSystem.components[0].revision).toBe(1);
  expect(state.documents[0].source).not.toContain('Linked revenue');
  await page.evaluate(() => window.prodesigner.command('studio-redo'));
  expect((await workspace()).documents[0].source).toContain('Linked revenue');
  await page.evaluate(() => window.prodesigner.command('components'));
  await page.waitForTimeout(200);
  await page.screenshot({ path: testInfo.outputPath('prodesigner-components.png') });
  await page.keyboard.press('Escape');

  const saved = await page.evaluate(() => window.prodesigner.exportWorkspace());
  await page.evaluate(json => window.prodesigner.importWorkspace(json), saved);
  expect((await workspace()).designSystem.instances[0].id).toBe(instanceId);
  expect((await workspace()).documents[0].history.undo.length).toBeGreaterThan(0);

  await page.evaluate(() => {
    window.prodesigner.configurePrototype(1, 'SignInButton', 2);
    window.prodesigner.command('prototype');
  });
  await page.waitForTimeout(200);
  await page.screenshot({ path: testInfo.outputPath('prodesigner-prototype-flows.png') });
  await page.keyboard.press('Escape');
  await page.evaluate(() => window.prodesigner.command('prototype-run'));
  await page.waitForFunction(() => window.prodesigner.prototypeBounds('SignInButton')?.width > 0);
  expect(await page.evaluate(() => window.prodesigner.prototypeState().document)).toBe('SignIn.axaml');
  await page.screenshot({ path: testInfo.outputPath('prodesigner-prototype-running.png') });
  const button = await page.evaluate(() => window.prodesigner.prototypeBounds('SignInButton'));
  await page.mouse.click(button.x + button.width / 2, button.y + button.height / 2);
  await expect.poll(() => page.evaluate(() => window.prodesigner.prototypeState()?.document)).toBe('Settings.axaml');
  expect(await page.evaluate(() => window.prodesigner.prototypeState().backCount)).toBe(1);
  await page.evaluate(() => window.prodesigner.prototypeBack());
  expect(await page.evaluate(() => window.prodesigner.prototypeState().document)).toBe('SignIn.axaml');
  await page.keyboard.press('Escape');
  expect((await workspace()).documents[1].source).toBe(initial.documents[1].source);
  expect((await workspace()).documents[2].source).toBe(initial.documents[2].source);
  expect(errors).toEqual([]);
});
