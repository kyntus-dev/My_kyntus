require("dotenv").config();
const express = require("express");
const cors = require("cors");
const mongoose = require("mongoose");
const http = require("http");
const path = require("path");
const fs = require("fs");
const { Server: SocketIOServer } = require("socket.io");

const { isSessionValid } = require("./middleware/sessionValidation");
const User = require("./models/User");
const { verifyKyntusToken, extractKyntusClaims } = require("./middleware/kyntusJwt");

const authRoutes = require("./routes/auth.routes");
const adminRoutes = require("./routes/admin.routes");
const scoreRoutes = require("./routes/score.routes");
const managementRoutes = require("./routes/management.routes");
const gridRoutes = require("./routes/grid.routes");
const cqRoutes = require("./routes/cq.routes");
const notificationRoutes = require("./routes/notification.routes");
const coachingRoutes = require("./routes/coaching.routes");
const usersRoutes = require("./routes/users.routes");
const callsRoutes = require("./routes/calls.routes");

const app = express();
app.set("startedAt", new Date().toISOString());

app.use(cors());
app.use(express.json({ limit: "10mb" }));
app.use(express.urlencoded({ extended: true, limit: "10mb" }));
app.disable("x-powered-by");

const uploadsDir = path.join(__dirname, "uploads");
if (!fs.existsSync(uploadsDir)) fs.mkdirSync(uploadsDir, { recursive: true });
app.use("/uploads", express.static(uploadsDir, { maxAge: "7d", etag: true }));
app.use("/api/qualite/uploads", express.static(uploadsDir, { maxAge: "7d", etag: true }));

app.get("/", (req, res) => {
  res.json({ message: "API Qualité KCQ (module MyKyntus)", prefix: "/api/qualite" });
});

app.get("/api/qualite/health", async (req, res) => {
  const mongoState = mongoose.connection?.readyState || 0;
  res.status(mongoState === 1 ? 200 : 503).json({
    ok: mongoState === 1,
    mongo: mongoState === 1 ? "connected" : "down",
    startedAt: app.get("startedAt"),
  });
});

const goneTraining = (req, res) =>
  res.status(410).json({ message: "Les formations sont gérées par le module Formation MyKyntus." });

const api = express.Router();
api.use("/auth", authRoutes);
api.use("/admin", adminRoutes);
api.use("/scores", scoreRoutes);
api.use("/management", managementRoutes);
api.use("/grids", gridRoutes);
api.use("/cq", cqRoutes);
api.use("/notifications", notificationRoutes);
api.use("/coaching", coachingRoutes);
api.use("/users", usersRoutes);
api.use("/calls", callsRoutes);
api.use("/training", goneTraining);

app.use("/api/qualite", api);

const server = http.createServer(app);
let socketClients = 0;

const io = new SocketIOServer(server, {
  cors: { origin: "*", methods: ["GET", "POST", "PATCH", "DELETE"] },
  path: "/socket.io",
});

app.set("io", io);
app.set("socketClients", () => socketClients);

io.use(async (socket, next) => {
  if (!process.env.SESSION_VALIDATION_BASE_URL) return next();
  const token = socket.handshake.auth?.token || socket.handshake.query?.token || "";
  if (typeof token !== "string" || !token || !await isSessionValid(token))
    return next(new Error("Session expirée ou révoquée"));
  next();
});

io.on("connection", async (socket) => {
  socketClients += 1;
  socket.on("disconnect", () => {
    socketClients = Math.max(0, socketClients - 1);
  });

  try {
    const token =
      socket.handshake.auth?.token ||
      socket.handshake.query?.token ||
      "";
    if (!token) return;

    const decoded = verifyKyntusToken(token);
    const claims = extractKyntusClaims(decoded);
    if (!claims.email) { socket.disconnect(true); return; }

    const user = await User.findOne({ email: claims.email.toLowerCase() })
      .select("_id celluleId cell")
      .lean();
    if (!user) { socket.disconnect(true); return; }

    let checking = false;
    const sessionTimer = setInterval(async () => {
      if (checking) return;
      checking = true;
      try { if (!await isSessionValid(token)) socket.disconnect(true); }
      finally { checking = false; }
    }, 15000);
    sessionTimer.unref();
    socket.once("disconnect", () => clearInterval(sessionTimer));
    socket.use(async (_packet, next) => {
      if (!await isSessionValid(token)) {
        socket.disconnect(true);
        return next(new Error("Session révoquée"));
      }
      next();
    });
    socket.join(`user:${user._id}`);
    const cell = (user.celluleId || user.cell || "").toString().trim();
    if (cell) socket.join(`cell:${cell}`);
  } catch (e) {
    if (process.env.SESSION_VALIDATION_BASE_URL) socket.disconnect(true);
  }
});

const PORT = process.env.PORT || 5000;
const MONGO_URI = process.env.MONGO_URI || "mongodb://127.0.0.1:27017/kcq";

mongoose
  .connect(MONGO_URI)
  .then(() => {
    console.log("MongoDB Qualité connectée");
    const { startDirectorySyncScheduler } = require("./services/directorySync");
    startDirectorySyncScheduler();
    server.listen(PORT, () => {
      console.log(`Qualité KCQ sur le port ${PORT} (préfixe /api/qualite)`);
    });
  })
  .catch((err) => {
    console.error("Erreur MongoDB :", err);
    process.exit(1);
  });

module.exports = app;

