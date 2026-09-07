using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Parrainage.Infrastructure.Services;
using Xunit;

namespace ParrainageBackend.Tests;

public sealed class ParrainageRequestUserResolverTests
{
    [Fact]
    public void Resolve_Throws_WhenUnauthenticated_EvenWithHeaders()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var resolver = new ParrainageRequestUserResolver(
            accessor,
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<ParrainageRequestUserResolver>.Instance);

        accessor.HttpContext!.Request.Headers["X-Parrainage-Role"] = "RH";
        accessor.HttpContext.Request.Headers["X-Parrainage-User-Id"] = "rh-1";

        Assert.Throws<UnauthorizedAccessException>(() => resolver.Resolve(accessor.HttpContext.Request));
    }

    [Fact]
    public void Resolve_IgnoresSpoofHeaders_WhenJwtPresent()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "jwt-user-1"),
                new Claim(ClaimTypes.Role, "Employee"),
                new Claim("role", "Employee"),
            ],
            authenticationType: "Bearer");
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        ctx.Request.Headers["X-Parrainage-Role"] = "RH";
        ctx.Request.Headers["X-Parrainage-User-Id"] = "spoof-rh";

        var accessor = new HttpContextAccessor { HttpContext = ctx };
        var resolver = new ParrainageRequestUserResolver(
            accessor,
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<ParrainageRequestUserResolver>.Instance);

        var user = resolver.Resolve(ctx.Request);

        Assert.Equal("jwt-user-1", user.UserId);
        Assert.Equal("PILOTE", user.Role); // Employee → PILOTE via mapping
        Assert.NotEqual("spoof-rh", user.UserId);
    }

    [Fact]
    public void ResolveEffectiveRole_PrefersJwtRh_OverPortalPilote()
    {
        Assert.Equal("RH", ParrainageRequestUserResolver.ResolveEffectiveRole("RH", "PILOTE"));
        Assert.Equal("PILOTE", ParrainageRequestUserResolver.ResolveEffectiveRole("PILOTE", "PILOTE"));
        Assert.Equal("MANAGER", ParrainageRequestUserResolver.ResolveEffectiveRole("PILOTE", "MANAGER"));
    }

    [Fact]
    public void ResolveEffectiveRole_PrefersPrivilegedSecondary_WhenPrimaryIsPilote()
    {
        Assert.Equal("RH", ParrainageRequestUserResolver.ResolveEffectiveRole("PILOTE", "RH"));
    }
}
