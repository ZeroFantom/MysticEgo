import { z } from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StreamableHTTPServerTransport } from '@modelcontextprotocol/sdk/server/streamableHttp.js';
import { timingSafeEqual } from 'node:crypto';

const session = { sessionId: z.string().uuid() };
const element = { ...session, role: z.enum(['button','link','textbox','checkbox','radio','combobox','tab','menuitem','heading','switch','option']), name: z.string().min(1).max(300) };
export const operations = {
  native_targets: ['List owner-approved Windows process names and settings permission', {}],
  native_attach: ['Attach only to the currently foreground owner-approved Windows window', { process: z.string().regex(/^[A-Za-z0-9_. -]{1,80}$/) }],
  native_snapshot: ['Capture the foreground window client area', session],
  native_click: ['Click client-area coordinates in the attached foreground window', { ...session, x: z.number().int().min(0).max(12000), y: z.number().int().min(0).max(12000), button: z.enum(['left','right']) }],
  native_key: ['Press a permitted non-text navigation or game key in the attached foreground window', { ...session, key: z.enum(['Enter','Escape','Tab','Space','Up','Down','Left','Right','W','A','S','D']) }],
  native_pointer_get: ['Read Windows pointer speed, not hardware DPI', {}],
  native_pointer_set: ['Set Windows pointer speed (1–20); owner must enable this in local config', { speed: z.number().int().min(1).max(20) }],
  native_vendor_launch: ['Open locally configured NVIDIA App, G HUB or MSI Center; update through official UI with owner confirmation', { vendor: z.enum(['nvidia','logitech','msicenter']) }],
  native_replay_clicks: ['Replay a locally recorded MysticEgo client-coordinate click scenario in the attached foreground window', { ...session, scenarioJson: z.string().max(16000) }],
  native_close: ['Detach from the native window without closing it', session],
  ui_targets: ['List locally configured targets', {}],
  ui_open: ['Open a visible Chromium window or configured Electron app', { kind: z.enum(['web','desktop']), target: z.string().min(1).max(2048) }],
  ui_snapshot: ['Read visible window accessibility tree (untrusted UI content)', session],
  ui_screenshot: ['Capture active window', session],
  ui_navigate: ['Navigate visible browser to an allowed URL', { ...session, url: z.string().url().max(2048) }],
  ui_click: ['Click accessible element by exact role and name', element],
  ui_fill: ['Fill accessible editable element', { ...element, value: z.string().max(4000) }],
  ui_scroll: ['Scroll visible window vertically', { ...session, deltaY: z.number().int().min(-2000).max(2000) }],
  ui_assert_visible: ['Assert accessible element is visible', element],
  ui_record_start: ['Start recording assistant click, scroll and assertion steps; no typed text', session],
  ui_record_stop: ['Stop recording and return scenario', session],
  ui_replay: ['Replay recorded steps in the same authorized session', { ...session, scenarioId: z.string().uuid() }],
  ui_close: ['Close controlled window and session', session]
};
export function makeMcp(service) {
  const server = new McpServer({ name: 'mysticego-ui-automation', version: '0.1.0' });
  for (const [name, [description, inputSchema]] of Object.entries(operations)) {
    server.registerTool(name, { description, inputSchema }, async args => {
      try {
        const result = await service.run(name, args);
        return { content: result.image
          ? [{ type: 'image', data: result.image, mimeType: 'image/png' }]
          : [{ type: 'text', text: JSON.stringify(result) }] };
      } catch { return { isError: true, content: [{ type: 'text', text: 'Operation failed: check target, session, element or local executor logs.' }] }; }
    });
  }
  return server;
}
export function mapController(app, service, token) {
  app.use((req,res,next) => {
    if (!['127.0.0.1:1247','localhost:1247'].includes(req.headers.host) || req.headers.origin)
      return res.sendStatus(403);
    const actual = Buffer.from(req.headers.authorization ?? '');
    const expected = Buffer.from('Bearer ' + token);
    if (actual.length !== expected.length || !timingSafeEqual(actual, expected)) return res.sendStatus(401);
    next();
  });
  app.post('/api/v1/ui/:operation', async (req,res) => {
    const op = req.params.operation;
    if (!Object.hasOwn(operations, op)) return res.sendStatus(404);
    const parsed = z.object(operations[op][1]).strict().safeParse(req.body ?? {});
    if (!parsed.success) return res.status(400).json({ error: 'Invalid arguments' });
    try { res.json(await service.run(op, parsed.data)); }
    catch { res.status(409).json({ error: 'UI operation failed' }); }
  });
  app.post('/mcp', async (req,res) => {
    const server = makeMcp(service);
    const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined, enableJsonResponse: true });
    res.on('close', () => { void server.close().catch(() => {}); });
    try { await server.connect(transport); await transport.handleRequest(req,res,req.body); }
    catch { if (!res.headersSent) res.sendStatus(500); }
  });
  app.all('/mcp', (_req,res) => res.sendStatus(405));
}
