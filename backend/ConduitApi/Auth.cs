using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ConduitApi;

public class TokenService
{
    private const string Issuer = "conduit-api";
    private readonly SymmetricSecurityKey _key;
    private readonly TimeSpan _lifetime;

    public TokenService(IConfiguration config)
    {
        var secret = config["Jwt:Key"];
        if (string.IsNullOrEmpty(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Jwt:Key must be set to a secret of at least 32 bytes.");
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _lifetime = TimeSpan.FromDays(config.GetValue("Jwt:ExpiryDays", 7));
    }

    public string Create(User user)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Issuer,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim("username", user.Username)],
            expires: DateTime.UtcNow.Add(_lifetime),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public int? Validate(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudience = Issuer,
                IssuerSigningKey = _key,
                ClockSkew = TimeSpan.FromMinutes(1),
            }, out _);
            return int.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? ReadToken(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        var parts = header.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return null;
        return parts[0].Equals("Token", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : null;
    }
}
