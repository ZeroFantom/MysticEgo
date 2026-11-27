import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { randomUUID } from 'node:crypto';
import { isAbsolute } from 'node:path';
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
    if (name === 'native_targets') return { processes: config.processes ?? [], pointerSpeed: config.allowPointerSpeed === true };
    if (name === 'native_pointer_get' || name === 'native_pointer_set') {
      if (config.allowPointerSpeed !== true) throw new Error('Pointer changes not enabled by owner');
      return invoke(name === 'native_pointer_get' ? ['pointer-get','local'] : ['pointer-set','local',a.speed]);
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
    if (name === 'native_key') return invoke(['key',...base,a.key]);
    throw new Error('Unknown native operation');
  }
}
