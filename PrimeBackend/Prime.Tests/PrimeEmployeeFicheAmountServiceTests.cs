using Prime.Domain.Entities;
using Prime.Infrastructure.Services;

namespace Prime.Tests;

public class PrimeEmployeeFicheAmountServiceTests
{
    [Fact]
    public void ResolveWorkflowDisplayAmounts_does_not_use_plafonds_as_payable_amounts()
    {
        var fiche = new EmployeePrimeServiceFiche
        {
            ServiceSaisieJson = """
                {
                  "plafondPrime": "1500",
                  "plafondChallenge": "400",
                  "rows": [
                    { "ponderationPrime": "100", "montantPrime": "" }
                  ]
                }
                """,
        };

        var amounts = PrimeEmployeeFicheAmountService.ResolveWorkflowDisplayAmounts(fiche);

        Assert.Null(amounts.PrimeAmount);
        Assert.Null(amounts.ChallengeAmount);
        Assert.Null(amounts.TotalAmount);
    }

    [Fact]
    public void ResolveWorkflowDisplayAmounts_prefers_calculated_montants_over_plafonds()
    {
        var fiche = new EmployeePrimeServiceFiche
        {
            ServiceSaisieJson = """
                {
                  "plafondPrime": "1500",
                  "plafondChallenge": "400",
                  "rows": [
                    { "montantPrime": "1200", "montantChallenge": "250" }
                  ]
                }
                """,
        };

        var amounts = PrimeEmployeeFicheAmountService.ResolveWorkflowDisplayAmounts(fiche);

        Assert.Equal(1200m, amounts.PrimeAmount);
        Assert.Equal(250m, amounts.ChallengeAmount);
        Assert.Equal(1450m, amounts.TotalAmount);
    }

    [Fact]
    public void ResolveWorkflowDisplayAmounts_uses_entity_columns_when_saisie_empty()
    {
        var fiche = new EmployeePrimeServiceFiche
        {
            PrimeAmount = 800m,
            ChallengeAmount = 100m,
            TotalAmount = 900m,
            ServiceSaisieJson = "{}",
        };

        var amounts = PrimeEmployeeFicheAmountService.ResolveWorkflowDisplayAmounts(fiche);

        Assert.Equal(800m, amounts.PrimeAmount);
        Assert.Equal(100m, amounts.ChallengeAmount);
        Assert.Equal(900m, amounts.TotalAmount);
    }

    [Fact]
    public void ResolveWorkflowDisplayAmounts_prefers_entity_total_general_over_saisie_and_plafonds()
    {
        var fiche = new EmployeePrimeServiceFiche
        {
            PrimeAmount = 215.46m,
            ChallengeAmount = 0m,
            TotalAmount = 215.46m,
            ServiceSaisieJson = """
                {
                  "plafondPrime": "1000",
                  "plafondChallenge": "0",
                  "rows": [
                    { "montantPrime": "50", "montantChallenge": "10" }
                  ]
                }
                """,
        };

        var amounts = PrimeEmployeeFicheAmountService.ResolveWorkflowDisplayAmounts(fiche);

        Assert.Equal(215.46m, amounts.PrimeAmount);
        Assert.Equal(0m, amounts.ChallengeAmount);
        Assert.Equal(215.46m, amounts.TotalAmount);
    }

    [Fact]
    public void ExtractPlafondsFromFiche_still_reads_plafond_fields()
    {
        var fiche = new EmployeePrimeServiceFiche
        {
            ServiceSaisieJson = """
                { "plafondPrime": "1000", "plafondChallenge": "0" }
                """,
        };

        var plafonds = PrimeEmployeeFicheAmountService.ExtractPlafondsFromFiche(fiche);

        Assert.Equal(1000m, plafonds.PrimeAmount);
        Assert.Equal(0m, plafonds.ChallengeAmount);
        Assert.Equal(1000m, plafonds.TotalAmount);
    }
}
