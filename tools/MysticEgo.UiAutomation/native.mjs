import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';
import { randomUUID } from 'node:crypto';
import { isAbsolute } from 'node:path';
import { existsSync } from 'node:fs';
const exec = promisify(execFile);

export class NativeWindowService {
  #active = null;
  constructor(config) { this.config = config; }
  close() { this.#active = null; }
  async run(name, a) {
    const config = this.config;
    if (!config || process.platform !== 'win32' || !isAbsolute(config.executablePath ?? ''))
      throw new Error('Native Windows control is disabled');
    const invoke = async args => {
      const { stdout } = await exec(config.executablePath, args.map(String), { timeout: 10000, maxBuffer: 32 * 1024 * 1024, windowsHide: true });
      return JSON.parse(stdout);
    };
    if (name === 'native_targets') return { processes: config.processes ?? [], pointerSpeed: config.allowPointerSpeed === true, vendorApps: Object.keys(config.vendorApps ?? {}) };
    if (name === 'native_pointer_get' || name === 'native_pointer_set') {
      if (config.allowPointerSpeed !== true) throw new Error('Pointer changes not enabled by owner');
      return invoke(name === 'native_pointer_get' ? ['pointer-get','local'] : ['pointer-set','local',a.speed]);
    }
    if (name === 'native_vendor_launch') {
      const path = Object.hasOwn(config.vendorApps ?? {}, a.vendor) ? config.vendorApps[a.vendor] : '';
      if (!['nvidia','logitech','msicenter'].includes(a.vendor) || !isAbsolute(path) || !existsSync(path))
        throw new Error('Vendor app not configured locally');
      const child = spawn(path, [], { detached: true, stdio: 'ignore' });
      await new Promise((resolve, reject) => { child.once('spawn', resolve); child.once('error', reject); });
      child.unref();
      return { launched: true, note: 'Select the vendor window and inspect its update screen. Installation and reboot require owner confirmation.' };
    }
    if (name === 'native_attach') {
      if (this.#active) throw new Error('Close existing native session');
      if (!config.processes?.includes(a.process)) throw new Error('Process not approved');
      const { hwnd, pid, ...info } = await invoke(['attach',a.process]);
      const sessionId = randomUUID();
      this.#active = { sessionId, hwnd, pid, process:a.process };
      return { sessionId, ...info };
    }
    const s = this.#active;
    if (!s || s.sessionId !== a.sessionId) throw new Error('No matching native session');
    if (name === 'native_close') { this.close(); return { closed: true }; }
    const base = [s.process,s.hwnd,s.pid];
    if (name === 'native_snapshot') return invoke(['snapshot',...base]);
    if (name === 'native_click') return invoke(['click',...base,a.x,a.y,a.button]);
    if (name === 'native_replay_clicks') {
      const scenario = JSON.parse(a.scenarioJson);
      if (scenario.targetProcess !== s.process || scenario.coordinateSpace !== 'client' || !Array.isArray(scenario.clicks) ||
          scenario.clicks.length < 1 || scenario.clicks.length > 20)
        throw new Error('Use a client-coordinate recorder scenario for this window (1–20 clicks)');
      let last = 0;
      for (const click of scenario.clicks) {
        if (!Number.isInteger(click.x) || !Number.isInteger(click.y) || click.x < 0 || click.y < 0 ||
            !Number.isInteger(click.atMs) || click.atMs < last || click.atMs > 30000 || !['left','right'].includes(click.button))
          throw new Error('Invalid recorder scenario');
        last = click.atMs;
      }
      const start = Date.now();
      for (const click of scenario.clicks) {
        await new Promise(resolve => setTimeout(resolve, Math.max(0, click.atMs - (Date.now() - start))));
        if (this.#active !== s) throw new Error('Native session stopped');
        await invoke(['click',...base,click.x,click.y,click.button]);
      }
      return { replayed: scenario.clicks.length };
    }
    if (name === 'native_key') return invoke(['key',...base,a.key]);
    throw new Error('Unknown native operation');
  }
}
