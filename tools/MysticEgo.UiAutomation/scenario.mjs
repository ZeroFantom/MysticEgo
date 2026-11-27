import { z } from 'zod';

const element = {role: z.enum(['button','link','textbox','checkbox','radio','combobox','tab','menuitem','heading','switch','option']),name:z.string().min(1).max(300)};
const delayMs = z.number().int().min(0).max(2000);
const step = z.discriminatedUnion('name', [
  z.object({name:z.literal('ui_click'),args:z.object(element).strict(),delayMs}).strict(),
  z.object({name:z.literal('ui_assert_visible'),args:z.object(element).strict(),delayMs}).strict(),
  z.object({name:z.literal('ui_scroll'),args:z.object({deltaY:z.number().int().min(-2000).max(2000)}).strict(),delayMs}).strict()
]);
export const scenarioSchema = z.object({
  version:z.literal(1),
  target:z.object({kind:z.enum(['web','desktop']),id:z.string().min(1).max(2048)}).strict(),
  steps:z.array(step).min(1).max(20)
}).strict();

export function parseScenario(json, target) {
  if (typeof json !== 'string' || json.length > 24000) throw new Error('Invalid scenario size');
  const scenario = scenarioSchema.parse(JSON.parse(json));
  if (scenario.target.kind !== target.kind || scenario.target.id !== target.id)
    throw new Error('Scenario target does not match current session');
  return scenario;
}

export function interruptibleDelay(ms, signal) {
  return new Promise((resolve,reject) => {
    if (signal.aborted) return reject(new Error('Replay cancelled'));
    const cancel = () => { clearTimeout(timer); reject(new Error('Replay cancelled')); };
    const timer = setTimeout(() => {
      signal.removeEventListener('abort',cancel);
      resolve();
    },ms);
    signal.addEventListener('abort',cancel,{once:true});
  });
}
