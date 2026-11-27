import test from 'node:test';
import assert from 'node:assert/strict';
import { ConsentLease } from './consent-lease.mjs';
import { UiAutomationService } from './service.mjs';

test('lease expires at boundary and revocation cannot extend it', () => {
  let now = 100;
  const lease = new ConsentLease(1000, () => now);
  assert.equal(lease.status().authorized, true);
  now = 1100;
  assert.equal(lease.active, false);
  assert.throws(() => lease.assertActive(), /expired/);
  const other = new ConsentLease(1000, () => now);
  other.revoke();
  assert.equal(other.status().revoked, true);
  assert.throws(() => other.assertActive(), /revoked/);
  assert.throws(() => new ConsentLease(30 * 60 * 1000 + 1));
});
test('queued work is rejected when lease expires while another action runs', async () => {
  let now = 100;
  let release;
  const page = {
    goto: async () => {}, url: () => 'https://example.test/', title: async () => 'Test',
    setDefaultTimeout: () => {}, isClosed: () => false,
    getByRole: () => ({ click: () => new Promise(resolve => { release = resolve; }) })
  };
  const browser = { newContext: async () => ({ route: async () => {}, newPage: async () => page }), close: async () => {} };
  const svc = new UiAutomationService({origins:['https://example.test'],apps:{}},{chromium:{launch:async()=>browser}});
  svc.lease = new ConsentLease(1000, () => now);
  const {sessionId} = await svc.run('ui_open',{kind:'web',target:'https://example.test/'});
  const inFlight = svc.run('ui_click',{sessionId,role:'button',name:'Test'});
  await new Promise(resolve => setImmediate(resolve));
  const queued = svc.run('ui_targets',{});
  assert.equal((await svc.run('ui_session_status',{})).pending,2);
  now = 1100;
  const rejection = assert.rejects(queued,/expired/);
  release();
  await inFlight;
  await rejection;
  await svc.stop();
  assert.equal((await svc.run('ui_session_status',{})).consent.revoked,true);
});
