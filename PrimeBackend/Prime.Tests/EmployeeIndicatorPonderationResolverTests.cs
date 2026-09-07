using Prime.Application;
using Prime.Application.DTOs;

namespace PrimeBackend.Tests;

public class EmployeeIndicatorPonderationResolverTests
{
    [Fact]
    public void Resolve_uses_service_defaults_when_no_override()
    {
        var (prime, challenge) = EmployeeIndicatorPonderationResolver.Resolve(40m, 10m, null, null);
        Assert.Equal(40m, prime);
        Assert.Equal(10m, challenge);
        Assert.False(EmployeeIndicatorPonderationResolver.HasOverride(null, null));
    }

    [Fact]
    public void Resolve_applies_partial_override_prime_only()
    {
        var (prime, challenge) = EmployeeIndicatorPonderationResolver.Resolve(40m, 10m, 55m, null);
        Assert.Equal(55m, prime);
        Assert.Equal(10m, challenge);
        Assert.True(EmployeeIndicatorPonderationResolver.HasOverride(55m, null));
    }

    [Fact]
    public void Resolve_applies_full_override()
    {
        var (prime, challenge) = EmployeeIndicatorPonderationResolver.Resolve(40m, 10m, 20m, 80m);
        Assert.Equal(20m, prime);
        Assert.Equal(80m, challenge);
    }

    [Fact]
    public void Apply_leaves_indicator_without_override_unchanged()
    {
        var src = Sample(40m, 10m);
        var applied = EmployeeIndicatorPonderationResolver.Apply(src, null, null);
        Assert.Same(src, applied);
    }

    [Fact]
    public void Apply_overlays_agent_percentages()
    {
        var src = Sample(40m, 10m);
        var applied = EmployeeIndicatorPonderationResolver.Apply(src, 25m, null);
        Assert.Equal(25m, applied.PonderationPrimePct);
        Assert.Equal(10m, applied.PonderationChallengePct);
        Assert.Equal(src.Id, applied.Id);
        Assert.Equal(src.Label, applied.Label);
    }

    private static ServicePrimeIndicatorDto Sample(decimal? prime, decimal? challenge) =>
        new()
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            ServiceId = "svc-1",
            SortOrder = 0,
            Label = "indicateur 1",
            PonderationPrimePct = prime,
            PonderationChallengePct = challenge,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
