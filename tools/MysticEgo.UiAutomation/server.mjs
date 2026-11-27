import express from 'express';
import { readFileSync } from 'node:fs';
import { createInterface } from 'node:readline/promises';
import { UiAutomationService } from './service.mjs';
import { makeMcp, mapController } from './controller.mjs';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';

const path = process.env.MYSTICEGO_UI_CONFIG;
if (!path) throw new Error('Set MYSTICEGO_UI_CONFIG to a trusted local config file');
const config = JSON.parse(readFileSync(path, 'utf8'));
if (!Array.isArray(config.origins) || !config.origins.every(x => {
  try { const u = new URL(x); return ['http:', 'https:'].includes(u.protocol) && u.origin === x; } catch { return false; }
}) || !config.apps || typeof config.apps !== 'object' || Array.isArray(config.apps)) throw new Error('Invalid target configuration');
if (process.argv.includes('--stdio')) delete config.native; // Native control requires interactive HTTP consent.
const service = new UiAutomationService(config);
let http, mcp, localConsole;
if (process.argv.includes('--stdio')) {
  mcp = makeMcp(service);
  await mcp.connect(new StdioServerTransport());
  process.stdin.on('end', () => void stop());
} else {
  const token = process.env.MYSTICEGO_UI_TOKEN;
  if (!token || token.length < 32) throw new Error('Set MYSTICEGO_UI_TOKEN to a random secret (minimum 32 characters)');
  if (!process.stdin.isTTY || !process.stdout.isTTY) throw new Error('HTTP control requires a local interactive terminal');
  localConsole = createInterface({ input: process.stdin, output: process.stdout });
  const answer = await localConsole.question('Allow local authenticated assistants to control configured Chromium/Electron targets for 30 minutes? Type ALLOW: ');
  if (answer !== 'ALLOW') { localConsole.close(); throw new Error('Local consent denied'); }
  if (config.native) {
    const native = await localConsole.question('Also allow native input and configured Windows settings? Type NATIVE to enable: ');
    if (native !== 'NATIVE') { delete config.native; service.config.native = undefined; service.native.config = undefined; }
  }
  if (service.library) {
    const library = await localConsole.question('Allow access to locally approved scenarios for the active target? Type LIBRARY to enable: ');
    if (library !== 'LIBRARY') service.library = null;
  }
  console.log('UI control enabled for 30 minutes. Type status or stop, or press Ctrl+C. Supervise actions.');
  localConsole.on('line', line => {
    if (line.trim().toLowerCase() === 'stop') void stop();
    else if (line.trim().toLowerCase() === 'status') void service.run('ui_session_status', {}).then(status => console.log(JSON.stringify(status))).catch(() => {});
  });
  localConsole.on('SIGINT', () => void stop());
  localConsole.on('close', () => void stop());
  setTimeout(() => void stop(), 30 * 60 * 1000).unref();
  const app = express();
  app.use(express.json({ limit: '64kb' }));
  mapController(app, service, token);
  http = app.listen(1247, '127.0.0.1');
}
let stopping = false;
async function stop() {
  if (stopping) return;
  stopping = true;
  http?.close();
  localConsole?.close();
  try { await mcp?.close(); await service.stop(); } finally { process.exit(0); }
}
process.on('SIGINT', () => void stop());
process.on('SIGTERM', () => void stop());
