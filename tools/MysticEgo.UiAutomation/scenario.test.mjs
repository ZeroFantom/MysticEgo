import test from 'node:test';
import assert from 'node:assert/strict';
import { parseScenario, interruptibleDelay } from './scenario.mjs';
const target = { kind: 'web', id: 'https://example.test/' };
const valid = { version: 1, target, steps: [{ name: 'ui_click', args: { role: 'button', name: 'Overview' }, delayMs: 0 }] };

test('portable scenario is strictly bound to the configured target', () => {
  assert.deepEqual(parseScenario(JSON.stringify(valid), target), valid);
  assert.throws(() => parseScenario(JSON.stringify(valid), { ...target, id: 'https://other.test/' }), /target/);
  assert.throws(() => parseScenario(JSON.stringify({ ...valid, version: 2 }), target));
  assert.throws(() => parseScenario(JSON.stringify({ ...valid, steps: [{name:'ui_fill',args:{value:'secret'},delayMs:0}] }), target));
  assert.throws(() => parseScenario(JSON.stringify({ ...valid, steps: [{...valid.steps[0],args:{...valid.steps[0].args,value:'secret'}}] }), target));
  assert.throws(() => parseScenario(JSON.stringify({ ...valid, steps:Array(21).fill(valid.steps[0]) }), target));
});
test('cancel interrupts pending replay delay', async () => {
  const controller = new AbortController();
  const pending = interruptibleDelay(2000, controller.signal);
  controller.abort();
  await assert.rejects(pending, /cancelled/);
  await assert.rejects(interruptibleDelay(0, controller.signal), /cancelled/);
  await interruptibleDelay(0, new AbortController().signal);
});
