import { chromium, _electron as electron } from 'playwright';
import { randomUUID } from 'node:crypto';
import { isAbsolute } from 'node:path';
import { NativeWindowService } from './native.mjs';
import { parseScenario, scenarioSchema, interruptibleDelay } from './scenario.mjs';
import { ScenarioLibrary } from './scenario-library.mjs';
import { DesktopSettingsService } from './desktop-settings.mjs';

export class UiAutomationService {
  #active = null;
  #tail = Promise.resolve();
  #stopping = false;
  #stopPromise = null;
  #pending = 0;
  #recording = null;
  #replayAbort = null;
  #scenarios = new Map();
  constructor(config, engines = { chromium, electron }) {
    this.config = config;
    this.engines = engines;
    this.lease = null;
    this.native = new NativeWindowService(config.native);
    this.desktopSettings = new DesktopSettingsService(config.desktopSettings);
    this.library = config.scenarioDirectory ? new ScenarioLibrary(config.scenarioDirectory) : null;
  }
  run(name, args) {
    if (name === 'ui_session_status') return Promise.resolve({ active: Boolean(this.#active), recording: Boolean(this.#recording), replaying: Boolean(this.#replayAbort), libraryEnabled: Boolean(this.library), nativeEnabled: Boolean(this.config.native), stopping: this.#stopping, pending: this.#pending, consent: this.lease?.status() ?? null });
    if (this.#stopping) return Promise.reject(new Error('Executor stopped'));
    try { this.lease?.assertActive(); } catch (err) { return Promise.reject(err); }
    if (name === 'ui_replay_cancel') {
      if (!this.#active || this.#active.id !== args.sessionId) return Promise.reject(new Error('No matching active session'));
      const running = Boolean(this.#replayAbort);
      this.#replayAbort?.abort();
      return Promise.resolve({ cancelled: running });
    }
    if (this.#pending >= 16) return Promise.reject(new Error('Executor queue full'));
    this.#pending++;
    const job = this.#tail.then(() => {
      if (this.#stopping) throw new Error('Executor stopped');
      this.lease?.assertActive();
      return this.#execute(name, args);
    }).finally(() => { this.#pending--; });
    this.#tail = job.catch(() => {});
    return job;
  }
  stop() {
    if (this.#stopPromise) return this.#stopPromise;
    this.#stopping = true;
    this.lease?.revoke();
    this.#replayAbort?.abort();
    this.native.close();
    this.#stopPromise = (async () => {
      await this.#tail;
      this.native.close();
      await this.#close();
    })();
    return this.#stopPromise;
  }
  async #execute(name, a) {
    if (name.startsWith('desktop_')) return this.desktopSettings.call(name,a);
    if (name.startsWith('native_')) return this.native.run(name, a);
    const session = this.#active;
    if (name === 'ui_library_list') {
      if (!this.library) throw new Error('Library disabled');
      if (!session || session.id !== a.sessionId) throw new Error('No matching active session');
      return { items: (await this.library.list()).filter(item => item.target.kind === session.target.kind && item.target.id === session.target.id) };
    }
    if (name === 'ui_library_load') {
      if (!this.library || !session || session.id !== a.sessionId) throw new Error('Library or session unavailable');
      const scenario = await this.library.load(a.libraryId);
      return this.#execute('ui_scenario_import', { sessionId: a.sessionId, scenarioJson: JSON.stringify(scenario) });
    }
    if (['ui_record_start','ui_record_stop','ui_replay','ui_scenario_export','ui_scenario_import'].includes(name)) {
      if (!session || session.id !== a.sessionId) throw new Error('No matching active session');
      if (name === 'ui_scenario_export') {
        const saved = this.#scenarios.get(a.scenarioId);
        if (!saved || saved.sessionId !== session.id) throw new Error('Unknown scenario for this session');
        const portable = scenarioSchema.parse({ version: 1, target: session.target, steps: saved.steps });
        return { scenarioJson: JSON.stringify(portable) };
      }
      if (name === 'ui_scenario_import') {
        if (this.#recording) throw new Error('Stop recording before import');
        const portable = parseScenario(a.scenarioJson, session.target);
        const scenarioId = randomUUID();
        this.#scenarios.set(scenarioId, { sessionId: session.id, steps: portable.steps });
        while (this.#scenarios.size > 20) this.#scenarios.delete(this.#scenarios.keys().next().value);
        return { scenarioId, steps: portable.steps.length };
      }
      if (name === 'ui_record_start') {
        if (this.#recording) throw new Error('Recording already active');
        this.#recording = { sessionId: session.id, started: Date.now(), steps: [] };
        return { recording: true };
      }
      if (name === 'ui_record_stop') {
        if (!this.#recording) throw new Error('No active recording');
        const { steps } = this.#recording;
        this.#recording = null;
        const scenarioId = randomUUID();
        this.#scenarios.set(scenarioId, { sessionId: session.id, steps });
        while (this.#scenarios.size > 20) this.#scenarios.delete(this.#scenarios.keys().next().value);
        return { scenarioId, steps };
      }
      if (this.#recording) throw new Error('Stop recording before replay');
      const scenario = this.#scenarios.get(a.scenarioId);
      if (!scenario || scenario.sessionId !== session.id) throw new Error('Unknown scenario for this session');
      const controller = new AbortController();
      this.#replayAbort = controller;
      let completed = 0;
      try {
        for (const step of scenario.steps) {
          await interruptibleDelay(step.delayMs, controller.signal);
          if (this.#stopping || controller.signal.aborted) throw new Error('Replay cancelled');
          this.lease?.assertActive();
          await this.#dispatch(step.name, { ...step.args, sessionId: session.id });
          completed++;
          if (controller.signal.aborted) return { replayed: completed, cancelled: true };
        }
        return { replayed: completed };
      } catch (err) {
        if (controller.signal.aborted) return { replayed: completed, cancelled: true };
        throw err;
      } finally {
        this.#replayAbort = null;
      }
    }
    const result = await this.#dispatch(name, a);
    if (this.#recording && ['ui_click', 'ui_scroll', 'ui_assert_visible'].includes(name)) {
      const r = this.#recording;
      if (r.steps.length < 20) {
        const now = Date.now();
        const { sessionId, ...args } = a;
        r.steps.push({ name, args, delayMs: Math.min(2000, now - r.started) });
        r.started = now;
      }
    }
    return result;
  }
  async #close() {
    this.#recording = null;
    this.#scenarios.clear();
    const previous = this.#active;
    this.#active = null;
    if (previous) await previous.owner.close();
  }
  #url(value) {
    const url = new URL(value);
    if (!['http:', 'https:'].includes(url.protocol) ||
        url.username || url.password || !this.config.origins.includes(url.origin))
      throw new Error('Navigation outside configured origins is denied');
    return url.href;
  }
  async #dispatch(name, a) {
    if (name === 'ui_targets') return { panels: this.config.origins, desktop: Object.keys(this.config.apps) };
    if (name === 'ui_open') {
      if (this.#active) throw new Error('Close active session before opening a new one');
      let owner;
      try {
        let page;
        if (a.kind === 'web') {
          const url = this.#url(a.target);
          owner = await this.engines.chromium.launch({ headless: false, slowMo: 180, timeout: 15000 });
          const context = await owner.newContext({ acceptDownloads: false });
          await context.route('**/*', route => {
            const request = route.request();
            if (request.isNavigationRequest()) {
              try { this.#url(request.url()); } catch { return route.abort(); }
            }
            return route.continue();
          });
          page = await context.newPage();
          await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 15000 });
        } else if (a.kind === 'desktop') {
          const app = Object.hasOwn(this.config.apps, a.target) ? this.config.apps[a.target] : null;
          if (!app || !isAbsolute(app.executablePath)) throw new Error('Unknown configured Electron application');
          owner = await this.engines.electron.launch({ executablePath: app.executablePath, args: app.args ?? [], timeout: 15000 });
          page = await owner.firstWindow({ timeout: 15000 });
        } else throw new Error('Unknown target kind');
        page.setDefaultTimeout(8000);
        const id = randomUUID();
        this.#active = { id, owner, page, kind: a.kind, target: { kind: a.kind, id: a.kind === 'web' ? this.#url(a.target) : a.target } };
        return { sessionId: id, title: await page.title(), url: page.url() };
      } catch (err) {
        this.#active = null;
        if (owner) await owner.close().catch(() => {});
        throw err;
      }
    }
    const s = this.#active;
    if (!s || s.id !== a.sessionId) throw new Error('No matching active session');
    const p = s.page;
    if (name === 'ui_close') { await this.#close(); return { closed: true }; }
    if (p.isClosed()) throw new Error('Window closed; close the session before reopening');
    if (s.kind === 'web') this.#url(p.url());
    if (name === 'ui_snapshot') return { title: await p.title(), url: p.url(), tree: (await p.locator('body').ariaSnapshot()).slice(0, 24000) };
    if (name === 'ui_screenshot') return { image: (await p.screenshot({ timeout: 8000 })).toString('base64') };
    if (name === 'ui_navigate') {
      if (s.kind !== 'web') throw new Error('Navigation is web-only');
      await p.goto(this.#url(a.url), { waitUntil: 'domcontentloaded', timeout: 15000 });
      return { url: p.url(), title: await p.title() };
    }
    if (name === 'ui_scroll') { await p.mouse.wheel(0, a.deltaY); return { scrolled: true }; }
    const locator = p.getByRole(a.role, { name: a.name, exact: true });
    if (name === 'ui_click') { await locator.click(); return { clicked: true, url: p.url() }; }
    if (name === 'ui_fill') { await locator.fill(a.value); return { filled: true }; }
    if (name === 'ui_assert_visible') {
      await locator.waitFor({ state: 'visible' });
      return { passed: true };
    }
    throw new Error('Unknown UI operation');
  }
}
