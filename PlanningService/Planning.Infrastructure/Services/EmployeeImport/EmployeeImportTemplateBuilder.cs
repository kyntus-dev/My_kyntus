using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Planning.Application.Abstractions.EmployeeImport;
using Planning.Infrastructure.Persistence;
using Planning.Application.DTOs;

namespace Planning.Infrastructure.Services.EmployeeImport;

public class EmployeeImportTemplateBuilder(AppDbContext db, IEmployeeFieldService fieldService)
{
    private static readonly XLColor HeaderBg = XLColor.FromHtml("#1E3A5F");

    public async Task<byte[]> BuildAsync(CancellationToken ct = default)
    {
        var orgSnapshot = await LoadOrgDataAsync(ct);
        var roles = await db.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => r.Name)
            .ToListAsync(ct);
        roles = roles.Where(r => !EmployeeImportFieldRegistry.IsImportForbiddenRoleName(r)).ToList();

        var customFields = (await fieldService.GetAllAsync(enabledOnly: true, ct))
            .Where(f => !f.IsSystemField)
            .OrderBy(f => f.SortOrder)
            .ToList();

        using var wb = new XLWorkbook();
        BuildEmployeesSheet(wb, orgSnapshot, roles, customFields);
        BuildReferentialsSheet(wb, roles, orgSnapshot);
        BuildNoticeSheet(wb, customFields.Count);

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    private void BuildEmployeesSheet(
        XLWorkbook wb,
        OrgTemplateSample sample,
        List<string> roles,
        IReadOnlyList<EmployeeImportFieldConfigDto> customFields)
    {
        var ws = wb.Worksheets.Add("Employés");
        var fields = EmployeeImportFieldRegistry.TemplateFields;
        var col = 1;

        for (var i = 0; i < fields.Count; i++, col++)
        {
            var field = fields[i];
            var header = field.IsRequiredOnCreate ? $"{field.Label} *" : field.Label;
            StyleHeader(ws.Cell(1, col), header);
        }

        foreach (var custom in customFields)
        {
            var header = custom.IsRequiredOnCreate ? $"{custom.Label} *" : custom.Label;
            StyleHeader(ws.Cell(1, col), header);
            col++;
        }

        var example = new Dictionary<string, string>
        {
            ["email"] = "employe@exemple.fr",
            ["firstName"] = "Mohammed",
            ["lastName"] = "Alami",
            ["password"] = "",
            ["role"] = roles.FirstOrDefault(r => r.Equals("Pilote", StringComparison.OrdinalIgnoreCase))
                       ?? roles.FirstOrDefault() ?? "Pilote",
            ["pole"] = sample.PoleName,
            ["cellule"] = sample.CelluleName,
            ["service"] = sample.ServiceName,
            ["hireDate"] = "15/01/2024",
            ["isActive"] = "Oui",
            ["level"] = "Débutant",
            ["niveauExpertiseMetier"] = "1",
            ["dateNaissance"] = "12/05/1990",
            ["cin"] = "AB123456",
            ["rib"] = "007780000000000000000000",
            ["immatriculationCnss"] = "1234567",
            ["immatriculationInterne"] = "EMP-001",
            ["operationalDepartment"] = "",
            ["villeNaissance"] = "Casablanca",
            ["nationalite"] = "Marocain",
            ["numeroCarteAutoentrepreneur"] = "AE-123456",
            ["sexe"] = "M",
            ["situationFamiliale"] = "Célibataire",
            ["nombreEnfants"] = "0",
            ["adresse"] = "12 Rue Example, Casablanca",
            ["telephone1"] = "0612345678",
            ["telephoneUrgence"] = "0698765432",
            ["relationUrgence"] = "Père",
            ["dateEntree"] = "15/01/2024",
            ["dateAnciennete"] = "15/01/2024",
            ["dateSortie"] = "",
            ["dateEvolutionPoste"] = "",
            ["ancienPoste"] = "",
            ["ancienService"] = "",
            ["poste"] = "Pilote",
            ["numeroBox"] = "",
            ["typeEmploye"] = "Salarié",
            ["carteImmatriculation"] = "",
            ["numeroPasseport"] = "",
            ["niveauScolaire"] = "Bac +5 (Master, ingénieur)",
            ["intitulesEtudes"] = "Master Informatique",
            ["enFormation"] = "Non",
            ["dateDebutFormation"] = "",
            ["dateFinFormationPrevue"] = "",
            ["chefDeProjetName"] = "",
            ["superviseurName"] = "",
            ["referentTechniqueName"] = "",
            ["contractType"] = "CDI",
            ["contractStartDate"] = "15/01/2024",
            ["contractEndDate"] = "",
            ["contractProbationDays"] = "90",
            ["contractAlertThresholdDays"] = "",
            ["contractStatus"] = "En période d'essai",
            ["contractNotes"] = "",
        };

        col = 1;
        for (var i = 0; i < fields.Count; i++, col++)
        {
            if (example.TryGetValue(fields[i].FieldKey, out var val))
                ws.Cell(2, col).Value = val;
        }

        foreach (var custom in customFields)
        {
            ws.Cell(2, col).Value = custom.DataType switch
            {
                "date" => "01/01/2024",
                "number" => "1",
                "boolean" => "Oui",
                _ => $"Exemple {custom.Label}"
            };
            col++;
        }

        ws.Row(1).Height = 22;
        ws.Columns().AdjustToContents();
    }

    private void BuildReferentialsSheet(XLWorkbook wb, List<string> roles, OrgTemplateSample sample)
    {
        var ws = wb.Worksheets.Add("Référentiels");

        ws.Cell(1, 1).Value = "Rôles disponibles";
        StyleHeader(ws.Cell(1, 1), "Rôles disponibles");
        for (var i = 0; i < roles.Count; i++)
            ws.Cell(i + 2, 1).Value = roles[i];

        var startCol = 3;
        StyleHeader(ws.Cell(1, startCol), "Pôle");
        StyleHeader(ws.Cell(1, startCol + 1), "Cellule");
        StyleHeader(ws.Cell(1, startCol + 2), "Service");

        var row = 2;
        foreach (var line in sample.HierarchyLines)
        {
            ws.Cell(row, startCol).Value = line.Pole;
            ws.Cell(row, startCol + 1).Value = line.Cellule;
            ws.Cell(row, startCol + 2).Value = line.Service;
            row++;
        }

        var levelCol = startCol + 3;
        StyleHeader(ws.Cell(1, levelCol), "Niveau contractuel");
        for (var i = 0; i < EmployeeImportLevelResolver.Labels.Count; i++)
            ws.Cell(i + 2, levelCol).Value = EmployeeImportLevelResolver.Labels[i];

        var refCol = levelCol + 2;
        WriteReferentialColumn(ws, refCol, "Sexe", ["M", "F"]);
        WriteReferentialColumn(ws, refCol + 1, "Situation familiale",
            ["Célibataire", "Marié(e)", "Divorcé(e)", "Veuf / Veuve"]);
        WriteReferentialColumn(ws, refCol + 2, "Nationalité",
            ["Marocain", "Marocaine", "Sénégalais", "Sénégalaise", "Tunisien", "Tunisienne", "Autre"]);
        WriteReferentialColumn(ws, refCol + 3, "Niveau scolaire",
            ["CAP / BEP", "Baccalauréat", "Bac +2", "Bac +3", "Bac +5", "Bac +8", "Autre"]);
        WriteReferentialColumn(ws, refCol + 4, "Expertise métier", ["1 - Débutant", "2 - Confirmé", "3 - Expert"]);
        WriteReferentialColumn(ws, refCol + 5, "Type contrat", ["CDI", "CDD", "Stage", "ANAPEC"]);
        WriteReferentialColumn(ws, refCol + 6, "Statut contrat",
            ["En période d'essai", "Actif", "Expiré", "Résilié"]);

        ws.Columns().AdjustToContents();
    }

    private static void WriteReferentialColumn(IXLWorksheet ws, int col, string title, IReadOnlyList<string> values)
    {
        StyleHeader(ws.Cell(1, col), title);
        for (var i = 0; i < values.Count; i++)
            ws.Cell(i + 2, col).Value = values[i];
    }

    private static void BuildNoticeSheet(XLWorkbook wb, int customFieldCount)
    {
        var ws = wb.Worksheets.Add("Notice");
        var lines = new List<string>
        {
            "Règles d'import employés",
            "",
            "• Email = identifiant unique (création si nouveau, mise à jour si existant).",
            "• Champs marqués * obligatoires à la création.",
            "• Cellule vide = la valeur en base n'est pas effacée.",
            "• Mot de passe vide = un mot de passe unique est généré automatiquement (à récupérer dans le résultat d'import).",
            "• Pôle / Cellule / Service : utilisez les noms exacts de la feuille Référentiels.",
            "• Département de production : entité distincte du Pôle — colonne dédiée, requise seulement pour créer un nouveau pôle.",
            "• Ne pas mettre « Département … » dans la colonne Pôle (ce n'est pas un préfixe de pôle).",
            "• Niveau contractuel : Débutant, Confirmé ou Expert (voir feuille Référentiels).",
            "• Expertise métier : 1, 2 ou 3 (Débutant, Confirmé, Expert).",
            "• Sexe : M ou F. Situation familiale et nationalité : voir Référentiels.",
            "• Nationalité hors Sénégal/Tunisie : n° carte autoentrepreneur obligatoire.",
            "• Responsables : indiquez le nom complet (Prénom Nom) d'un employé existant, cohérent avec le pôle / la cellule / le service.",
            "• Chef de projet → titulaire du pôle ; superviseur → rattaché au chef et à la cellule ; référent → rattaché au superviseur et au service.",
            "• Contrat : type CDI/CDD/Stage/ANAPEC. CDD et Stage exigent une date de fin.",
            "• Seuil alerte contrat (jours) vide à la création = 7 jours (une semaine).",
            "• En formation = Oui : un contrat est créé si absent (CDI par défaut).",
            "• Les rôles Admin et Manager ne peuvent pas être attribués via l'import employés.",
            "• Une erreur sur une ligne n'arrête pas le reste du fichier.",
        };

        if (customFieldCount > 0)
            lines.Add($"• {customFieldCount} champ(s) personnalisé(s) inclus après les colonnes standard.");

        lines.Add("");
        lines.Add("Après dépôt du fichier : vérifiez le mapping des colonnes puis la prévisualisation avant de lancer l'import.");

        for (var i = 0; i < lines.Count; i++)
            ws.Cell(i + 1, 1).Value = lines[i];

        ws.Column(1).Width = 90;
    }

    private static void StyleHeader(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontColor = XLColor.White;
        cell.Style.Fill.BackgroundColor = HeaderBg;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private async Task<OrgTemplateSample> LoadOrgDataAsync(CancellationToken ct)
    {
        var floors = await db.Floors
            .AsNoTracking()
            .Include(f => f.Services)
            .ThenInclude(s => s.SubServices)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);

        var hierarchy = new List<OrgHierarchyLine>();
        OrgHierarchyLine? first = null;

        foreach (var floor in floors)
        {
            foreach (var service in floor.Services.OrderBy(s => s.Name))
            {
                foreach (var sub in service.SubServices.OrderBy(s => s.Name))
                {
                    var line = new OrgHierarchyLine(floor.Name, service.Name, sub.Name);
                    hierarchy.Add(line);
                    first ??= line;
                }
            }
        }

        first ??= new OrgHierarchyLine("Mon Pôle", "Ma Cellule", "Mon Service");

        return new OrgTemplateSample(
            first.Pole,
            first.Cellule,
            first.Service,
            hierarchy);
    }

    private sealed record OrgHierarchyLine(string Pole, string Cellule, string Service);

    private sealed record OrgTemplateSample(
        string PoleName,
        string CelluleName,
        string ServiceName,
        IReadOnlyList<OrgHierarchyLine> HierarchyLines);
}
