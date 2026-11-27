import { chromium, _electron as electron } from 'playwright';
import { randomUUID } from 'node:crypto';
import { isAbsolute } from 'node:path';

export class UiAutomationService {
  #active = null;
  #tail = Promise.resolve();
  #stopping = false;
  constructor(config, engines = { chromium, electron }) {
    this.config = config;
    this.engines = engines;
  }
  run(name, args) {
    if (this.#stopping) return Promise.reject(new Error('Executor stopped'));
    const job = this.#tail.then(() => {
      if (this.#stopping) throw new Error('Executor stopped');
      return this.#dispatch(name, args);
    });
    this.#tail = job.catch(() => {});
    return job;
  }
  async stop() {
    this.#stopping = true;
    await this.#tail;
    await this.#close();
  }
  async #close() {
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
        this.#active = { id, owner, page, kind: a.kind };
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
