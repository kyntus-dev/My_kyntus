/**
 * v60 — Restrictions posées par le Super Admin sur un administrateur MyKyntus (module Contrôle qualité).
 * Un administrateur a tous les droits ; le Super Admin peut retirer chaque fonctionnalité
 * (« écran.Verbe »). Les restrictions sont lues dans Employee Directory
 * (GET /api/iam/admin-restrictions/me, jeton relayé) et gardées 15 s en mémoire.
 * Sans effet pour tout autre rôle, pour le Super Admin et pour un administrateur sans restriction.
 */
const axios = require("axios");

const DIRECTORY_BASE_URL = String(process.env.DIRECTORY_BASE_URL || "http://employee-directory-backend:8080").replace(/\/+$/, "");
const TTL_MS = 15000;
const FAIL_TTL_MS = 5000;
const cache = new Map();

const EXPORT = ["export", "download", "telecharger", "excel", "csv", "pdf", "xlsx"];

/** Écrans « Accès par rôle » d'une route /api/qualite/... (l'un suffit) ; null = jamais restreint. */
function screensFor(method, segments) {
  const zone = segments[0] || "";
  const read = method === "GET" || method === "HEAD";
  switch (zone) {
    case "admin":
      return ["cq.admin"];
    case "grids":
      return read ? ["cq.evaluations", "cq.parametres", "cq.self"] : ["cq.parametres"];
    case "coaching":
      return read ? ["cq.coaching", "cq.self"] : ["cq.coaching"];
    case "scores":
    case "management":
    case "cq":
    case "calls":
      return read ? ["cq.evaluations", "cq.self"] : ["cq.evaluations"];
    default:
      // auth, notifications, users, health, uploads, training : techniques ou référentiels partagés.
      return null;
  }
}

function verbFor(method, segments) {
  const exporte = segments.some((s) => EXPORT.some((m) => s.includes(m)));
  if (method === "GET" || method === "HEAD") return exporte ? "Export" : "View";
  if (method === "DELETE") return "Delete";
  if (exporte) return "Export";
  return segments[0] === "grids" ? "Configure" : "Act";
}

async function deniedFor(subjectId, rawToken) {
  const hit = cache.get(subjectId);
  if (hit && hit.until > Date.now()) return hit.denied;
  try {
    const res = await axios.get(`${DIRECTORY_BASE_URL}/api/iam/admin-restrictions/me`, {
      headers: { Authorization: rawToken },
      timeout: 2000,
    });
    const denied = new Set(Array.isArray(res.data?.denied) ? res.data.denied : []);
    cache.set(subjectId, { denied, until: Date.now() + TTL_MS });
    return denied;
  } catch (err) {
    // Annuaire indisponible : comportement historique (aucune restriction appliquée).
    console.warn("[kcq] restrictions administrateur indisponibles :", err?.message || err);
    const denied = new Set();
    cache.set(subjectId, { denied, until: Date.now() + FAIL_TTL_MS });
    return denied;
  }
}

/**
 * Fonctionnalité refusée (« écran.Verbe ») pour cette requête, ou null si elle est permise.
 * @param {import('express').Request} req
 * @param {{ role?: string, subjectId?: string, raw?: Record<string, unknown> }} claims
 * @param {string} rawToken « Bearer … »
 */
async function adminRestriction(req, claims, rawToken) {
  if (String(claims?.role || "").toLowerCase() !== "admin") return null;
  if (String(claims?.raw?.kyntus_super_admin || "").toLowerCase() === "true") return null;
  if (!claims.subjectId || !rawToken) return null;
  const method = String(req.method || "GET").toUpperCase();
  if (method === "OPTIONS") return null;

  const path = String(req.originalUrl || req.url || "").split("?")[0].toLowerCase();
  const prefix = "/api/qualite/";
  const rest = path.startsWith(prefix) ? path.slice(prefix.length) : path.replace(/^\/+/, "");
  const segments = rest.split("/").filter(Boolean);
  const screens = screensFor(method, segments);
  if (!screens) return null;

  const denied = await deniedFor(claims.subjectId, rawToken);
  if (denied.size === 0) return null;
  const verb = verbFor(method, segments);
  const permis = screens.some((s) => !denied.has(`${s}.View`) && !denied.has(`${s}.${verb}`));
  return permis ? null : `${screens[0]}.${verb}`;
}

module.exports = { adminRestriction, screensFor, verbFor };
