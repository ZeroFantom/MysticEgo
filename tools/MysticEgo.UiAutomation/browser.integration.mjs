import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { UiAutomationService } from './service.mjs';

test('headed Chromium opens a page and performs visible interaction', async () => {
  const site = createServer((_req,res) => {
    res.setHeader('Content-Type','text/html');
    res.end('<h1>Overview</h1><button onclick="this.textContent=\'Done\'">Run</button>');
  });
  await new Promise(resolve => site.listen(0,'127.0.0.1',resolve));
  const origin = 'http://127.0.0.1:' + site.address().port;
  const service = new UiAutomationService({ origins: [origin], apps: {} });
  try {
    const { sessionId } = await service.run('ui_open',{kind:'web',target:origin});
    const snapshot = await service.run('ui_snapshot',{sessionId});
    assert.match(snapshot.tree,/Overview/);
    await service.run('ui_click',{sessionId,role:'button',name:'Run'});
    assert.deepEqual(await service.run('ui_assert_visible',{sessionId,role:'button',name:'Done'}),{passed:true});
    const screenshot = await service.run('ui_screenshot',{sessionId});
    assert.ok(Buffer.from(screenshot.image,'base64').length > 100);
  } finally {
    await service.stop();
    await new Promise(resolve => site.close(resolve));
  }
});
