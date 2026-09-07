import React, { useMemo, useState, useRef, useEffect } from "react";
import { FiMenu, FiSearch, FiLogOut, FiSettings, FiX, FiSend, FiMessageCircle } from "react-icons/fi";
import ThemeToggle from "./ThemeToggle.jsx";
import NotificationsBell from "./NotificationsBell.jsx";
import ProfileModal from "./ProfileModal.jsx";
import api from "../api";

/* Simple Markdown to HTML */
function md(text) {
  let h = String(text || "")
    .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
    // Headers
    .replace(/^### (.+)$/gm, '<div style="font-weight:800;font-size:0.95rem;margin:8px 0 4px">$1</div>')
    .replace(/^## (.+)$/gm, '<div style="font-weight:800;font-size:1rem;margin:10px 0 4px">$1</div>')
    .replace(/^# (.+)$/gm, '<div style="font-weight:800;font-size:1.05rem;margin:10px 0 6px">$1</div>')
    // Bold
    .replace(/\*\*(.+?)\*\*/g, '<b>$1</b>')
    // Italic
    .replace(/\*(.+?)\*/g, '<i>$1</i>')
    // Inline code
    .replace(/`(.+?)`/g, '<code style="background:rgba(0,0,0,0.06);padding:1px 4px;border-radius:4px;font-size:0.82em">$1</code>')
    // Bullet lists
    .replace(/^[\-\*] (.+)$/gm, '<div style="padding-left:12px;margin:2px 0">• $1</div>')
    // Numbered lists
    .replace(/^\d+\. (.+)$/gm, (match, p1, offset, str) => `<div style="padding-left:12px;margin:2px 0">${match.split('.')[0]}. ${p1}</div>`)
    // Remove markdown table separators (|---|---|)
    .replace(/^\|[\s\-\|]+\|$/gm, '')
    // Simple table rows: | a | b | c |
    .replace(/^\|(.+)\|$/gm, (match, inner) => {
      const cells = inner.split('|').map(c => c.trim()).filter(Boolean);
      return '<div style="display:flex;gap:8px;padding:3px 0;border-bottom:1px solid rgba(0,0,0,0.06)">' +
        cells.map(c => `<span style="flex:1;font-size:0.82rem">${c}</span>`).join('') + '</div>';
    })
    // Line breaks
    .replace(/\n/g, '<br/>');
  // Clean up double <br/> from removed lines
  h = h.replace(/(<br\/>){3,}/g, '<br/><br/>');
  return h;
}

function AIBubble({ m }) {
  if (m.role === "user") {
    return (
      <div style={{ display: "flex", justifyContent: "flex-end" }}>
        <div style={{ maxWidth: "85%", padding: "8px 12px", borderRadius: 12, background: "var(--primary)", color: "#fff", fontSize: "0.85rem", lineHeight: 1.55, whiteSpace: "pre-wrap", wordBreak: "break-word" }}>
          {m.text}
        </div>
      </div>
    );
  }
  return (
    <div style={{ display: "flex", justifyContent: "flex-start" }}>
      <div style={{
        maxWidth: "85%", padding: "8px 12px", borderRadius: 12,
        background: m.error ? "var(--danger-bg)" : "var(--chip)",
        color: m.error ? "var(--danger)" : "var(--text)",
        fontSize: "0.85rem", lineHeight: 1.6, wordBreak: "break-word",
      }} dangerouslySetInnerHTML={{ __html: md(m.text) }} />
    </div>
  );
}
function AIChatWidget() {
  const [open, setOpen] = useState(false);
  const [messages, setMessages] = useState([]);
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(false);
  const bottomRef = useRef(null);

  useEffect(() => { bottomRef.current?.scrollIntoView({ behavior: "smooth" }); }, [messages]);

  async function send() {
    const q = input.trim();
    if (!q || loading) return;
    setInput("");
    setMessages((prev) => [...prev, { role: "user", text: q }]);
    setLoading(true);
    try {
      const res = await api.post("/admin/ai/chat", { message: q }, { toast: false });
      setMessages((prev) => [...prev, { role: "ai", text: res.data?.reply || "Pas de réponse.", model: res.data?.model || "" }]);
    } catch (e) {
      setMessages((prev) => [...prev, { role: "ai", text: e?.response?.data?.message || "Erreur. Vérifiez la clé API IA dans Santé > Configuration.", error: true }]);
    } finally { setLoading(false); }
  }

  return (
    <>
      {/* Floating button */}
      <button
        onClick={() => setOpen((v) => !v)}
        title="Assistant IA"
        style={{
          position: "fixed", bottom: 24, right: 24, zIndex: 250,
          width: 52, height: 52, borderRadius: 16,
          background: "var(--primary)", color: "#fff", border: "none",
          display: "grid", placeItems: "center", cursor: "pointer",
          boxShadow: "0 6px 24px rgba(79,70,229,0.4)",
          transition: "transform 200ms, box-shadow 200ms",
        }}
        onMouseEnter={(e) => { e.currentTarget.style.transform = "scale(1.08)"; }}
        onMouseLeave={(e) => { e.currentTarget.style.transform = "scale(1)"; }}
      >
        {open ? <FiX size={22} /> : <FiMessageCircle size={22} />}
      </button>

      {/* Chat window */}
      {open && (
        <div style={{
          position: "fixed", bottom: 88, right: 24, zIndex: 250,
          width: 380, maxWidth: "calc(100vw - 48px)", height: 480, maxHeight: "calc(100vh - 120px)",
          background: "var(--panel)", border: "1px solid var(--border-strong)",
          borderRadius: 20, boxShadow: "var(--shadow-lg)",
          display: "flex", flexDirection: "column", overflow: "hidden",
          animation: "slideUp 250ms cubic-bezier(0.16,1,0.3,1)",
        }}>
          {/* Header */}
          <div style={{ padding: "12px 16px", borderBottom: "1px solid var(--border)", display: "flex", alignItems: "center", justifyContent: "space-between" }}>
            <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
              <span style={{ fontSize: "1.1rem" }}>🤖</span>
              <div>
                <div style={{ fontWeight: 800, fontSize: "0.9rem" }}>Assistant QA</div>
                <div style={{ fontSize: "0.7rem", color: "var(--muted)" }}>Kyntus IA</div>
              </div>
            </div>
            <button onClick={() => setOpen(false)} style={{ background: "none", border: "none", cursor: "pointer", color: "var(--muted)", padding: 0 }}><FiX size={18} /></button>
          </div>

          {/* Messages */}
          <div style={{ flex: 1, overflow: "auto", padding: "12px 14px", display: "flex", flexDirection: "column", gap: 10 }}>
            {messages.length === 0 && (
              <div style={{ flex: 1, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: 8, color: "var(--muted)", textAlign: "center", padding: 16 }}>
                <span style={{ fontSize: "2rem" }}>🤖</span>
                <div style={{ fontWeight: 700, fontSize: "0.9rem" }}>Assistant QA</div>
                <div style={{ fontSize: "0.8rem", lineHeight: 1.5 }}>
                  Posez vos questions sur la qualité, les scores, les agents, le coaching…
                </div>
                <div style={{ display: "flex", flexDirection: "column", gap: 4, marginTop: 8, width: "100%" }}>
                  {["Quels agents ont le plus bas score ?", "Comment améliorer le taux de contestation ?", "Conseils coaching pour un agent NC sur l'accueil"].map((s) => (
                    <button key={s} onClick={() => { setInput(s); }} style={{ padding: "6px 10px", borderRadius: 8, border: "1px solid var(--border)", background: "var(--panel-2)", cursor: "pointer", fontSize: "0.78rem", textAlign: "left", fontWeight: 500, color: "var(--text-secondary)" }}>{s}</button>
                  ))}
                </div>
              </div>
            )}
            {messages.map((m, i) => (
              <AIBubble key={i} m={m} />
            ))}
            {loading && (
              <div style={{ display: "flex", justifyContent: "flex-start" }}>
                <div style={{ padding: "8px 12px", borderRadius: 12, background: "var(--chip)", fontSize: "0.85rem", color: "var(--muted)" }}>
                  ⏳ Réflexion…
                </div>
              </div>
            )}
            <div ref={bottomRef} />
          </div>

          {/* Input */}
          <div style={{ padding: "10px 12px", borderTop: "1px solid var(--border)", display: "flex", gap: 8 }}>
            <input
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); send(); } }}
              placeholder="Votre question…"
              style={{ flex: 1, border: "1px solid var(--border-strong)", borderRadius: 10, padding: "8px 12px", fontSize: "0.85rem", outline: "none", background: "var(--panel-2)", color: "var(--text)" }}
            />
            <button onClick={send} disabled={loading || !input.trim()} style={{
              width: 38, height: 38, borderRadius: 10, border: "none",
              background: "var(--primary)", color: "#fff", cursor: "pointer",
              display: "grid", placeItems: "center", flexShrink: 0, opacity: loading || !input.trim() ? 0.5 : 1,
            }}>
              <FiSend size={16} />
            </button>
          </div>
        </div>
      )}
    </>
  );
}

/* =============== AppShell =============== */
export default function AppShell({ user, onLogout, navSections, children }) {
  const [collapsed, setCollapsed] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);

  // Inline search
  const [searchQuery, setSearchQuery] = useState("");
  const [searchFocused, setSearchFocused] = useState(false);
  const searchRef = useRef(null);

  const role = user?.role || "";

  const allCommands = useMemo(() => {
    const cmds = [];
    if (role === "admin") {
      cmds.push(
        { label: "Tableau de bord", key: "dashboard", icon: "📊" },
        { label: "Évaluations", key: "evaluations", icon: "📋" },
        { label: "Coaching", key: "coaching", icon: "🎧" },
        { label: "Grilles", key: "grids", icon: "⊞" },
        { label: "Notifications", key: "notifications", icon: "🔔" },
        { label: "Santé & Configuration", key: "health", icon: "🏥" },
      );
    }
    if (role === "cq") {
      cmds.push(
        { label: "Tableau de bord", key: "stats", icon: "📊" },
        { label: "Évaluations", key: "list", icon: "📋" },
        { label: "Nouvelle évaluation", key: "new", icon: "➕" },
        { label: "Appels à évaluer", key: "picking", icon: "📞" },
        { label: "Coaching", key: "coaching", icon: "🎧" },
        { label: "Grilles", key: "grids", icon: "⊞" },
        { label: "Paramètres CQ", key: "settings", icon: "⚙" },
      );
    }
    if (role === "management") {
      cmds.push(
        { label: "Tableau de bord", key: "overview", icon: "📊" },
        { label: "Évaluations", key: "form", icon: "📋" },
        { label: "Nouvelle évaluation", key: "new", icon: "➕" },
        { label: "Appels à évaluer", key: "picking", icon: "📞" },
        { label: "Coaching", key: "coaching", icon: "🎧" },
      );
    }
    if (role === "pilote") {
      cmds.push({ label: "Tableau de bord", key: "dashboard", icon: "📊" });
    }
    return cmds;
  }, [role]);

  const filtered = useMemo(() => {
    if (!searchQuery.trim()) return searchFocused ? allCommands : [];
    const q = searchQuery.toLowerCase();
    return allCommands.filter((c) => c.label.toLowerCase().includes(q));
  }, [searchQuery, searchFocused, allCommands]);

  function navigate(key) {
    window.dispatchEvent(new CustomEvent("kcq:navigate", { detail: { role, view: key } }));
    setSearchQuery("");
    setSearchFocused(false);
    searchRef.current?.blur();
  }

  // Close dropdown on outside click
  useEffect(() => {
    const handler = (e) => {
      if (!e.target.closest(".search-wrapper")) setSearchFocused(false);
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  const initials = useMemo(() => {
    const n = (user?.name || "").trim();
    if (!n) return "U";
    const parts = n.split(/\s+/).filter(Boolean);
    return (parts[0]?.[0] || "U").toUpperCase() + (parts[1]?.[0] || "").toUpperCase();
  }, [user?.name]);

  return (
    <div className={collapsed ? "shell shell--collapsed" : "shell"}>
      <aside className="sidebar">
        <div className="sidebar__top">
          <div className="brand">
            <div className="brand__mark" aria-hidden="true">
              <img src="/kyntus.svg" alt="" style={{ width: 28, height: 28, display: "block" }} />
            </div>
            <div className="brand__name">Kyntus</div>
          </div>
        </div>

        <nav className="nav">
          {(navSections || []).map((sec) => (
            <div key={sec.title} className="nav__section">
              <div className="nav__title">{sec.title}</div>
              <div className="nav__items">
                {sec.items.map((it) => (
                  <button key={it.key} type="button" className={it.active ? "nav__item nav__item--active" : "nav__item"} onClick={it.onClick} title={it.label}>
                    <span className="nav__icon">{it.icon}</span>
                    <span className="nav__label">{it.label}</span>
                  </button>
                ))}
              </div>
            </div>
          ))}
        </nav>

        <div className="sidebar__bottom">
          <div className="usercard">
            <div className="usercard__avatar">{initials}</div>
            <div className="usercard__meta" style={{ cursor: "pointer" }} onClick={() => setProfileOpen(true)} title="Paramètres du profil">
              <div className="usercard__name" style={{ display: "flex", alignItems: "center", gap: 4 }}>
                {user?.name || "Utilisateur"}
                <FiSettings style={{ fontSize: 12, opacity: 0.5, flexShrink: 0 }} />
              </div>
              <div className="usercard__role">{(user?.role || "").toUpperCase()}</div>
            </div>
            <button className="iconbtn" onClick={onLogout} title="Déconnexion"><FiLogOut /></button>
          </div>
        </div>
      </aside>

      <div className="main">
        <header className="topbar">
          <button className="iconbtn" onClick={() => setCollapsed((v) => !v)} title="Menu"><FiMenu /></button>
          <div className="topbar__center">
            <div className="search-wrapper" style={{ position: "relative", flex: 1, maxWidth: 500, minWidth: 200 }}>
              <div className="search">
                <FiSearch className="search__icon" />
                <input
                  ref={searchRef}
                  className="search__input"
                  placeholder="Rechercher une page…"
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  onFocus={() => setSearchFocused(true)}
                  onKeyDown={(e) => {
                    if (e.key === "Escape") { setSearchFocused(false); searchRef.current?.blur(); }
                    if (e.key === "Enter" && filtered.length > 0) navigate(filtered[0].key);
                  }}
                />
              </div>

              {/* Dropdown */}
              {searchFocused && filtered.length > 0 && (
                <div style={{
                  position: "absolute", top: "calc(100% + 6px)", left: 0, right: 0,
                  background: "var(--panel)", border: "1px solid var(--border-strong)",
                  borderRadius: 12, boxShadow: "var(--shadow-lg)", overflow: "hidden", zIndex: 100,
                  maxHeight: 320, overflowY: "auto",
                }}>
                  {filtered.map((c) => (
                    <div
                      key={c.key}
                      onClick={() => navigate(c.key)}
                      style={{ padding: "10px 14px", cursor: "pointer", display: "flex", alignItems: "center", gap: 10, fontSize: "0.875rem", fontWeight: 600, transition: "background 120ms" }}
                      onMouseEnter={(e) => e.currentTarget.style.background = "var(--chip)"}
                      onMouseLeave={(e) => e.currentTarget.style.background = "transparent"}
                    >
                      <span style={{ fontSize: "1rem", width: 24, textAlign: "center" }}>{c.icon}</span>
                      <span>{c.label}</span>
                    </div>
                  ))}
                </div>
              )}
              {searchFocused && searchQuery.trim() && filtered.length === 0 && (
                <div style={{
                  position: "absolute", top: "calc(100% + 6px)", left: 0, right: 0,
                  background: "var(--panel)", border: "1px solid var(--border-strong)",
                  borderRadius: 12, boxShadow: "var(--shadow-lg)", padding: "16px 14px",
                  color: "var(--muted)", fontSize: "0.85rem", textAlign: "center", zIndex: 100,
                }}>
                  Aucun résultat pour « {searchQuery} »
                </div>
              )}
            </div>
          </div>
          <div className="topbar__actions">
            <ThemeToggle />
            <NotificationsBell />
          </div>
        </header>
        <main className="content">{children}</main>
      </div>

      <ProfileModal open={profileOpen} onClose={() => setProfileOpen(false)} />
      {user?.role !== "pilote" && <AIChatWidget />}
    </div>
  );
}
