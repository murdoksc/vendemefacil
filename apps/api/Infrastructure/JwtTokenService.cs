using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using VendemeFacil.Api.Contracts;
using VendemeFacil.Api.Domain;

namespace VendemeFacil.Api.Infrastructure;

public sealed class JwtTokenService(IConfiguration configuration)
{
    public AuthResponse Create(Tenant tenant, AppUser user)
    {
        var expires = DateTimeOffset.UtcNow.AddHours(8);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("tenant_id", tenant.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString())
            ,new Claim("can_view_costs", user.CanViewCosts.ToString().ToLowerInvariant())
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.")));
        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expires,
            new UserSession(user.Id, tenant.Id, tenant.Name, tenant.Slug, user.DisplayName, user.Email, user.Role.ToString(), user.CanViewCosts));
    }
}
