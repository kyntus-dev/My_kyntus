namespace Prime.Infrastructure.Services;

public static class GlobalPoolScopeTypes
{
    public const string Service = "Service";
    public const string Cellule = "Cellule";
    public const string Pole = "Pole";

    public static bool IsValid(string? scopeType) =>
        string.Equals(scopeType, Service, StringComparison.Ordinal) ||
        string.Equals(scopeType, Cellule, StringComparison.Ordinal) ||
        string.Equals(scopeType, Pole, StringComparison.Ordinal);
}

public static class GlobalPoolSynthesisLineStatuses
{
    public const string PendingReview = "PendingReview";
    public const string Approved = "Approved";
    public const string LineRejected = "LineRejected";
}

public static class GlobalPoolLineDecisions
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    public static string DeriveLineStatus(string? rhDecision, string? managerDecision)
    {
        _ = managerDecision;
        var rh = rhDecision ?? Pending;
        if (string.Equals(rh, Pending, StringComparison.Ordinal))
            return GlobalPoolSynthesisLineStatuses.PendingReview;
        if (string.Equals(rh, Approved, StringComparison.Ordinal))
            return GlobalPoolSynthesisLineStatuses.Approved;
        return GlobalPoolSynthesisLineStatuses.LineRejected;
    }

    public static bool RegularizationEditable(string? rhDecision) =>
        string.Equals(rhDecision ?? Pending, Pending, StringComparison.Ordinal);
}

public static class GlobalPoolSynthesisLineHistoryActions
{
    public const string Approved = "Approved";
    public const string LineRejected = "LineRejected";
    public const string Paid = "Paid";
    public const string Unpaid = "Unpaid";
    public const string RegularizationUpdated = "RegularizationUpdated";
}

public static class GlobalPoolPaymentStatuses
{
    public const string Unpaid = "Unpaid";
    public const string Paid = "Paid";
}

/// <summary>Etat de paiement déduit au niveau d'une synthèse (agrégat des lignes).</summary>
public static class GlobalPoolPaymentState
{
    public const string Unpaid = "Unpaid";
    public const string Partial = "Partial";
    public const string Paid = "Paid";
}
