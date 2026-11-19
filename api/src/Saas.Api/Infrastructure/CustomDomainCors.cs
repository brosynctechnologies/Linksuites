using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Saas.Application.Orders;

namespace Saas.Api.Infrastructure;

/// <summary>
/// The configured origins (Cors:Origins), plus, for the public storefront routes only (/api/public), any https origin that is a custom
/// domain served right now (<see cref="CustomDomainCache"/>, kept a minute). Bearer tokens are never sent from a storefront, so nothing
/// signed in is opened to those origins.
/// </summary>
public sealed class CustomDomainCorsPolicyProvider(IOptions<CorsOptions> options, CustomDomainCache cache) : ICorsPolicyProvider
{
    private readonly DefaultCorsPolicyProvider inner = new(options);

    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var policy = await inner.GetPolicyAsync(context, policyName);
        var origin = context.Request.Headers.Origin.ToString();
        if (policyName is not null || policy is null || string.IsNullOrEmpty(origin) || policy.IsOriginAllowed(origin)
            || !context.Request.Path.StartsWithSegments("/api/public", StringComparison.OrdinalIgnoreCase))
            return policy;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || uri.AbsolutePath != "/" || await cache.SlugForAsync(uri.Host, context.RequestAborted) is null)
            return policy;
        // A new policy for just this origin (copying the builder would keep the default policy's origin check), same headers and methods.
        var custom = new CorsPolicyBuilder().WithOrigins(origin);
        if (policy.AllowAnyHeader) custom.AllowAnyHeader(); else custom.WithHeaders([.. policy.Headers]);
        if (policy.AllowAnyMethod) custom.AllowAnyMethod(); else custom.WithMethods([.. policy.Methods]);
        if (policy.ExposedHeaders.Count > 0) custom.WithExposedHeaders([.. policy.ExposedHeaders]);
        return custom.Build();
    }
}
