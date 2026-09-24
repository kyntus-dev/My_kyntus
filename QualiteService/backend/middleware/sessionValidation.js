// Opaque bootstrap tokens are never accepted here; the usual JWT verification still runs.
const axios = require("axios");

async function isSessionValid(token) {
  const base = (process.env.SESSION_VALIDATION_BASE_URL || "").replace(/\/$/, "");
  if (!base) return true; // Backward compatibility outside the integrated Compose deployment.
  try {
    const response = await axios.get(`${base}/api/Auth/session/validate`, {
      headers: { Authorization: `Bearer ${token}` },
      timeout: 5000,
      maxRedirects: 0,
      validateStatus: () => true,
    });
    return response.status === 204;
  } catch {
    return false; // Auth unavailable: fail closed for protected operations.
  }
}

module.exports = { isSessionValid };
