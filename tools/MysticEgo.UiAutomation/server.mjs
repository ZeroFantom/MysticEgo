import express from 'express';
import { readFileSync } from 'node:fs';
import { UiAutomationService } from './service.mjs';
import { makeMcp, mapController } from './controller.mjs';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';

const path = process.env.MYSTICEGO_UI_CONFIG;
if (!path) throw new Error('Set MYSTICEGO_UI_CONFIG to a trusted local config file');
const config = JSON.parse(readFileSync(path, 'utf8'));
if (!Array.isArray(config.origins) || !config.origins.every(x => {
  try { const u = new URL(x); return ['http:', 'https:'].includes(u.protocol) && u.origin === x; } catch { return false; }
}) || !config.apps || typeof config.apps !== 'object' || Array.isArray(config.apps)) throw new Error('Invalid target configuration');
const service = new UiAutomationService(config);
let http, mcp;
if (process.argv.includes('--stdio')) {
  mcp = makeMcp(service);
  await mcp.connect(new StdioServerTransport());
  process.stdin.on('end', () => void stop());
} else {
  const token = process.env.MYSTICEGO_UI_TOKEN;
  if (!token || token.length < 32) throw new Error('Set MYSTICEGO_UI_TOKEN to a random secret (minimum 32 characters)');
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
  try { await mcp?.close(); await service.stop(); } finally { process.exit(0); }
}
process.on('SIGINT', () => void stop());
process.on('SIGTERM', () => void stop());
