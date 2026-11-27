import test from 'node:test';
import assert from 'node:assert/strict';
import { UiAutomationService } from './service.mjs';

const config = { origins: ['https://example.test'], apps: {} };
test('rejects unknown targets before launching', async () => {
  const svc = new UiAutomationService(config, {});
  for (const target of ['https://other.test', 'file:///etc/passwd', 'https://user:pass@example.test']) {
    await assert.rejects(svc.run('ui_open', { kind: 'web', target }), /denied/);
  }
  await assert.rejects(svc.run('ui_open', { kind: 'desktop', target: 'constructor' }), /Unknown configured/);
  await assert.rejects(svc.run('ui_click', { sessionId: 'wrong' }), /No matching/);
  assert.deepEqual(await svc.run('ui_targets', {}), { panels: config.origins, desktop: [] });
  await svc.stop();
  await assert.rejects(svc.run('ui_targets', {}), /stopped/);
});
test('visible browser session, exact locator, ownership and close', async () => {
  let options, clicked, closed = 0;
  const page = {
    goto: async () => {}, url: () => 'https://example.test/', title: async () => 'Overview',
    setDefaultTimeout: () => {}, isClosed: () => false,
    getByRole: (role, opts) => ({ click: async () => { clicked = [role, opts]; } })
  };
  const browser = {
    newContext: async () => ({ route: async () => {}, newPage: async () => page }),
    close: async () => { closed++; }
  };
  const svc = new UiAutomationService(config, { chromium: { launch: async opts => { options = opts; return browser; } } });
  const { sessionId } = await svc.run('ui_open', { kind: 'web', target: 'https://example.test/' });
  assert.equal(options.headless, false);
  await assert.rejects(svc.run('ui_open', { kind: 'web', target: 'https://example.test/' }), /Close active/);
  await svc.run('ui_click', { sessionId, role: 'button', name: 'Overview' });
  assert.deepEqual(clicked, ['button', { name: 'Overview', exact: true }]);
  await svc.run('ui_close', { sessionId });
  assert.equal(closed, 1);
  await assert.rejects(svc.run('ui_snapshot', { sessionId }), /No matching/);
});
