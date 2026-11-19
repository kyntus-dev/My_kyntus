const { verifyKyntusToken, extractKyntusClaims } = require("./kyntusJwt");
const { hasKcqRole } = require("./kcqRoles");
const { resolveCurrentUser, listAllowedPilotIds } = require("../services/directorySync");
const { adminRestriction } = require("./adminRestrictions");

async function auth(req, res, next) {
  try {
    const authHeader = req.headers["authorization"] || "";
    const queryToken = typeof req.query?.access_token === "string" ? req.query.access_token : "";
    const allowQueryToken = /picking-audio|\/stream\//i.test(req.path || "");
    const raw = authHeader || (allowQueryToken && queryToken ? `Bearer ${queryToken}` : "");
    const decoded = verifyKyntusToken(raw);
    const claims = extractKyntusClaims(decoded);
    // v60 — Fonctionnalité retirée par le Super Admin à cet administrateur : refus (403).
    const retiree = await adminRestriction(req, claims, raw);
    if (retiree) {
      return res.status(403).json({
        code: "ADMIN_RESTRICTED",
        action: retiree,
        message: "Cette fonctionnalité vous a été retirée par le Super Admin.",
      });
    }
    const { doc, mapped, scope } = await resolveCurrentUser(claims, raw);

    req.user = {
      id: doc._id,
      _id: doc._id,
      role: mapped.kcqRole,
      kcqRoles: mapped.kcqRoles,
      name: doc.name,
      email: doc.email,
      myKyntusRole: claims.role,
      subjectId: claims.subjectId,
      employeeId: doc.employeeId,
      cell: doc.cell,
      celluleId: doc.celluleId,
      serviceId: doc.serviceId,
      poleId: doc.poleId,
    };
    req.scope = scope;
    req.allowedPilotIds = await listAllowedPilotIds(scope);
    next();
  } catch (err) {
    if (err.code === "CONFIG") {
      return res.status(500).json({ message: "Configuration serveur invalide (JWT)." });
    }
    if (err.code === "FORBIDDEN") {
      return res.status(403).json({ message: err.message });
    }
    if (err.code === "NO_PROFILE") {
      return res.status(401).json({ message: err.message });
    }
    return res.status(401).json({ message: "Token expiré ou invalide" });
  }
}

function allowManagement(req, res, next) {
  if (!hasKcqRole(req.user, "management")) {
    return res.status(403).json({ message: "Accès réservé au management." });
  }
  next();
}

auth.allowManagement = allowManagement;

module.exports = auth;
module.exports.allowManagement = allowManagement;
