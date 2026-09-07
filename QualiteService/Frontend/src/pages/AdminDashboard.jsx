import React from "react";
import DashboardView from "./admin/DashboardView.jsx";
import CoachingView from "./admin/CoachingView.jsx";
import GridsView from "./admin/GridsView.jsx";
import NotificationsView from "./admin/NotificationsView.jsx";
import AdminHealth from "./admin/AdminHealth.jsx";
import EvaluationsView from "./admin/EvaluationsView.jsx";
import PickingListPage from "./shared/PickingListPage.jsx";
import NewEvaluationPage from "./shared/NewEvaluationPage.jsx";

export default function AdminDashboard({ controlledTab }) {
  const tab = controlledTab || "dashboard";
  if (tab === "picking") return <PickingListPage />;
  if (tab === "new") return <NewEvaluationPage title="Nouvelle évaluation" />;
  if (tab === "coaching") return <CoachingView />;
  if (tab === "grids") return <GridsView />;
  if (tab === "notifications") return <NotificationsView />;
  if (tab === "health") return <AdminHealth />;
  if (tab === "evaluations") return <EvaluationsView />;
  return <DashboardView />;
}
