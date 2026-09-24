const { test } = require('node:test');
const assert = require('node:assert/strict');
const http = require('node:http');
const { isSessionValid } = require('../middleware/sessionValidation');

test('session validation accepts only 204, carries the bearer, and fails closed', async () => {
  let status = 204;
  let authorization;
  const server = http.createServer((req, res) => {
    authorization = req.headers.authorization;
    assert.equal(req.url, '/api/Auth/session/validate');
    res.writeHead(status); res.end();
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  process.env.SESSION_VALIDATION_BASE_URL = `http://127.0.0.1:${server.address().port}`;
  try {
    assert.equal(await isSessionValid('test-token'), true);
    assert.equal(authorization, 'Bearer test-token');
    for (const rejected of [200, 301, 401, 403, 500]) {
      status = rejected;
      assert.equal(await isSessionValid('test-token'), false);
    }
  } finally { await new Promise(resolve => server.close(resolve)); }
  assert.equal(await isSessionValid('test-token'), false);
  delete process.env.SESSION_VALIDATION_BASE_URL;
});
