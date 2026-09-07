using Prime.Application.DTOs;

namespace Prime.Application;

/// <summary>Résout les pondérations effectives d'un indicateur service (défaut + override agent optionnel).</summary>
public static class EmployeeIndicatorPonderationResolver
{
    public static (decimal? Prime, decimal? Challenge) Resolve(
        decimal? servicePrime,
        decimal? serviceChallenge,
        decimal? overridePrime,
        decimal? overrideChallenge) =>
        (overridePrime ?? servicePrime, overrideChallenge ?? serviceChallenge);

    public static bool HasOverride(decimal? overridePrime, decimal? overrideChallenge) =>
        overridePrime.HasValue || overrideChallenge.HasValue;

    public static ServicePrimeIndicatorDto Apply(
        ServicePrimeIndicatorDto source,
        decimal? overridePrime,
        decimal? overrideChallenge)
    {
        var (prime, challenge) = Resolve(
            source.PonderationPrimePct,
            source.PonderationChallengePct,
            overridePrime,
            overrideChallenge);
        if (prime == source.PonderationPrimePct && challenge == source.PonderationChallengePct)
            return source;
        return new ServicePrimeIndicatorDto
        {
            Id = source.Id,
            ServiceId = source.ServiceId,
            SortOrder = source.SortOrder,
            Label = source.Label,
            PonderationPrimePct = prime,
            PonderationChallengePct = challenge,
            IsActive = source.IsActive,
            TemplateStableId = source.TemplateStableId,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
        };
    }
}
