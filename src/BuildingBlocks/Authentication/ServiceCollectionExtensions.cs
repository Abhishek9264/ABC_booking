using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Tatkal.Authentication;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires JWT bearer validation the same way in every service. Called
    /// from each service's Program.cs; only UserService additionally
    /// registers IJwtTokenService to mint tokens.
    /// </summary>
    public static IServiceCollection AddTatkalJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Jwt");
        services.Configure<JwtOptions>(section);
        var options = section.Get<JwtOptions>() ?? new JwtOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorization(o =>
        {
            o.AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin));
            o.AddPolicy(Policies.SystemOrAdmin, p => p.RequireRole(Roles.System, Roles.Admin));
        });

        return services;
    }

    public static IServiceCollection AddTatkalJwtIssuing(this IServiceCollection services)
    {
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        return services;
    }
}
