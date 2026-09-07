import React, { useEffect, useState } from "react";
import CQDashboard from "./pages/CQDashboard.jsx";
import AdminDashboard from "./pages/AdminDashboard.jsx";
import PilotDashboard from "./pages/PilotDashboard.jsx";
import PilotEvaluations from "./pages/PilotEvaluations.jsx";
import PilotCoachingsPage from "./pages/PilotCoachingsPage.jsx";
import ManagementDashboard from "./pages/ManagementDashboard.jsx";
import api from "./api.js";

import { ThemeProvider } from "./theme/theme.js";
import { ToastProvider } from "./toast/ToastProvider.jsx";
import AppShell from "./components/AppShell.jsx";
import { resetSocketAuth } from "./socket.js";
import { applyEmbedDocumentClass, isCqEmbed } from "./embed.js";
import {
  FiBarChart2,
  FiList,
  FiHeadphones,
  FiSettings,
  FiGrid,
  FiUsers,
  FiPlus,
  FiActivity,
  FiPhone,
} from "react-icons/fi";

function readQuery() {
  return new URLSearchParams(window.location.search);
}

function isEmbed() {
  return isCqEmbed();
}

function mapViewForRole(view, role) {
  const v = String(view || "").trim();
  if (!v) return null;
  if (role === "admin") {
    if (["evaluations", "list"].includes(v)) return "evaluations";
    if (["grids", "coaching", "picking", "dashboard", "health", "audit", "notifications", "new"].includes(v)) return v;
    return v;
  }
  if (role === "cq") {
    if (["evaluations", "list"].includes(v)) return "list";
    if (["dashboard", "stats", "overview"].includes(v)) return "stats";
    if (["grids", "coaching", "picking", "new", "settings"].includes(v)) return v;
    return v;
  }
  if (role === "management") {
    if (["evaluations", "list", "form"].includes(v)) return "form";
    if (["grids", "coaching", "picking", "new", "overview", "dashboard"].includes(v)) {
      return v === "dashboard" || v === "overview" ? "overview" : v;
    }
    return v;
  }
  if (role === "pilote") {
    if (["evaluations", "list", "mine"].includes(v)) return "evaluations";
    if (["coachings", "coaching", "coachings-me"].includes(v)) return "coachings";
    return "dashboard";
  }
  return v;
}

function App() {
  const embed = isEmbed();
  applyEmbedDocumentClass();
  const [user, setUser] = useState(null);
  const [waitingSso, setWaitingSso] = useState(embed);
  const [adminTab, setAdminTab] = useState("dashboard");
  const [cqView, setCqView] = useState("list");
  const [mgmtView, setMgmtView] = useState("overview");
  const [pilotView, setPilotView] = useState("dashboard");
  const [editScoreId, setEditScoreId] = useState("");

  const applyUrlView = (role) => {
    const view = mapViewForRole(readQuery().get("view"), role);
    if (!view) return;
    if (role === "admin") setAdminTab(view);
    if (role === "cq") setCqView(view);
    if (role === "management") setMgmtView(view);
    if (role === "pilote") setPilotView(view);
  };

  const acceptSession = (userData, token) => {
    localStorage.setItem("token", token);
    localStorage.setItem("user", JSON.stringify(userData));
    resetSocketAuth();
    setUser(userData);
    setWaitingSso(false);
    applyUrlView(userData.role);
  };

  useEffect(() => {
    const onMessage = async (ev) => {
      const data = ev?.data || {};
      if (data.type !== "KYNTUS_CQ_TOKEN" || typeof data.token !== "string") return;
      try {
        localStorage.setItem("token", data.token);
        const me = await api.get("/auth/me", { toast: false });
        acceptSession(me.data, data.token);
      } catch {
        setWaitingSso(false);
      }
    };
    window.addEventListener("message", onMessage);
    return () => window.removeEventListener("message", onMessage);
  }, []);

  useEffect(() => {
    if (embed) {
      window.parent?.postMessage({ type: "KYNTUS_CQ_READY" }, "*");
      return;
    }
    const savedUser = localStorage.getItem("user");
    const token = localStorage.getItem("token");
    if (savedUser && token) {
      try {
        setUser(JSON.parse(savedUser));
      } catch {
        /* ignore */
      }
    }
  }, [embed]);

  useEffect(() => {
    if (!user) return;
    applyUrlView(user.role);
  }, [user]);

  useEffect(() => {
    const handler = (ev) => {
      const d = ev?.detail || {};
      const role = d.role;
      const view = d.view;
      const id = d.editScoreId || "";
      if (typeof id === "string") setEditScoreId(id);
      if (!view) return;
      if (role === "cq") setCqView(view);
      if (role === "management") setMgmtView(view);
      if (role === "admin") setAdminTab(view);
      if (role === "pilote") setPilotView(view);
    };
    window.addEventListener("kcq:navigate", handler);
    return () => window.removeEventListener("kcq:navigate", handler);
  }, []);

  const handleLogout = () => {
    localStorage.removeItem("token");
    localStorage.removeItem("user");
    setUser(null);
    if (embed) window.parent?.postMessage({ type: "KYNTUS_CQ_SESSION_EXPIRED" }, "*");
  };

  const wrap = (navSections, inner) => {
    if (embed) {
      return <div style={{ minHeight: "100%" }}>{inner}</div>;
    }
    return (
      <AppShell user={user} onLogout={handleLogout} navSections={navSections}>
        {inner}
      </AppShell>
    );
  };

  const content = (() => {
    if (!user) {
      if (embed || waitingSso) {
        return (
          <div style={{ padding: 32, fontFamily: "sans-serif", color: "#334155" }}>
            Connexion au module Qualité via MyKyntus…
          </div>
        );
      }
      return (
        <div style={{ padding: 32, fontFamily: "sans-serif", color: "#334155" }}>
          Ouvrez ce module depuis MyKyntus (menu Qualité). Le login local est désactivé.
        </div>
      );
    }

    if (user.role === "admin") {
      const navSections = [
        {
          title: "WORKSPACE",
          items: [
            { key: "dashboard", label: "Tableau de bord", icon: <FiBarChart2 />, active: adminTab === "dashboard", onClick: () => setAdminTab("dashboard") },
            { key: "evaluations", label: "Évaluations", icon: <FiList />, active: adminTab === "evaluations", onClick: () => setAdminTab("evaluations") },
            { key: "coaching", label: "Coaching", icon: <FiHeadphones />, active: adminTab === "coaching", onClick: () => setAdminTab("coaching") },
          ],
        },
        {
          title: "MANAGEMENT",
          items: [
            { key: "grids", label: "Grilles", icon: <FiGrid />, active: adminTab === "grids", onClick: () => setAdminTab("grids") },
            { key: "notifications", label: "Notifications", icon: <FiUsers />, active: adminTab === "notifications", onClick: () => setAdminTab("notifications") },
            { key: "health", label: "Santé", icon: <FiActivity />, active: adminTab === "health", onClick: () => setAdminTab("health") },
          ],
        },
      ];
      return wrap(navSections, <AdminDashboard controlledTab={adminTab} />);
    }

    if (user.role === "cq") {
      const navSections = [
        {
          title: "WORKSPACE",
          items: [
            { key: "list", label: "Évaluations", icon: <FiList />, active: cqView === "list", onClick: () => setCqView("list") },
            { key: "new", label: "Nouvelle évaluation", icon: <FiPlus />, active: cqView === "new", onClick: () => { setEditScoreId(""); setCqView("new"); } },
            { key: "picking", label: "Appels à évaluer", icon: <FiPhone />, active: cqView === "picking", onClick: () => setCqView("picking") },
            { key: "coaching", label: "Coaching", icon: <FiHeadphones />, active: cqView === "coaching", onClick: () => setCqView("coaching") },
            { key: "grids", label: "Grilles", icon: <FiGrid />, active: cqView === "grids", onClick: () => setCqView("grids") },
          ],
        },
        {
          title: "PREFERENCES",
          items: [
            { key: "settings", label: "Paramètres CQ", icon: <FiSettings />, active: cqView === "settings", onClick: () => setCqView("settings") },
          ],
        },
      ];
      return wrap(navSections, <CQDashboard forcedView={cqView} onViewChange={setCqView} editScoreId={editScoreId} />);
    }

    if (user.role === "pilote") {
      const navSections = [
        {
          title: "MON ESPACE",
          items: [
            { key: "dashboard", label: "Mon tableau de bord", icon: <FiBarChart2 />, active: pilotView === "dashboard", onClick: () => setPilotView("dashboard") },
            { key: "evaluations", label: "Mes évaluations", icon: <FiList />, active: pilotView === "evaluations", onClick: () => setPilotView("evaluations") },
            { key: "coachings", label: "Mes coachings", icon: <FiHeadphones />, active: pilotView === "coachings", onClick: () => setPilotView("coachings") },
          ],
        },
      ];
      const inner =
        pilotView === "evaluations" ? <PilotEvaluations />
          : pilotView === "coachings" ? <PilotCoachingsPage />
            : <PilotDashboard />;
      return wrap(navSections, inner);
    }

    if (user.role === "management") {
      const navSections = [
        {
          title: "WORKSPACE",
          items: [
            { key: "overview", label: "Tableau de bord", icon: <FiBarChart2 />, active: mgmtView === "overview", onClick: () => setMgmtView("overview") },
            { key: "form", label: "Évaluations", icon: <FiList />, active: mgmtView === "form", onClick: () => setMgmtView("form") },
            { key: "new", label: "Nouvelle évaluation", icon: <FiPlus />, active: mgmtView === "new", onClick: () => { setEditScoreId(""); setMgmtView("new"); } },
            { key: "picking", label: "Appels à évaluer", icon: <FiPhone />, active: mgmtView === "picking", onClick: () => setMgmtView("picking") },
            { key: "coaching", label: "Coaching", icon: <FiHeadphones />, active: mgmtView === "coaching", onClick: () => setMgmtView("coaching") },
          ],
        },
      ];
      return wrap(navSections, <ManagementDashboard forcedView={mgmtView} onViewChange={setMgmtView} editScoreId={editScoreId} />);
    }

    return wrap([], (
      <div className="page">
        <div className="card">
          <p>Rôle non reconnu : {user.role}</p>
        </div>
      </div>
    ));
  })();

  return (
    <ThemeProvider defaultTheme="light">
      <ToastProvider>
        {content}
      </ToastProvider>
    </ThemeProvider>
  );
}

export default App;
