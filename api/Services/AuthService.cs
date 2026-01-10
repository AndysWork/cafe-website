using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using BCrypt.Net;

namespace Cafe.Api.Services;

public class AuthService
{
    private readonly string _jwtSecret;
    private readonly int _jwtExpiryMinutes;

    public AuthService()
    {
        // Get JWT settings from environment or use defaults
        _jwtSecret = Environment.GetEnvironmentVariable("Jwt__Secret") 
            ?? "CafeWebsite_SuperSecretKey_2024_MinimumLength32Characters_Required!";
        _jwtExpiryMinutes = int.TryParse(Environment.GetEnvironmentVariable("Jwt__ExpiryMinutes"), out var expiry) 
            ? expiry : 1440; // 24 hours default
    }

    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt());
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        return BCrypt.Net.BCrypt.Verify(password, passwordHash);
    }

    public string GenerateJwtToken(string userId, string username, string role, string? defaultOutletId = null, List<string>? assignedOutlets = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_jwtSecret);
        
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role)
        };
        
        // Add outlet claims if provided
        if (!string.IsNullOrEmpty(defaultOutletId))
        {
            claims.Add(new Claim("DefaultOutletId", defaultOutletId));
        }
        
        if (assignedOutlets != null && assignedOutlets.Count > 0)
        {
            claims.Add(new Claim("AssignedOutlets", string.Join(",", assignedOutlets)));
        }
        
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = MongoService.GetIstNow().AddMinutes(_jwtExpiryMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key), 
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_jwtSecret);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }

    public string? GetUserIdFromToken(string token)
    {
        var principal = ValidateToken(token);
        return principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    public string? GetRoleFromToken(string token)
    {
        var principal = ValidateToken(token);
        return principal?.FindFirst(ClaimTypes.Role)?.Value;
    }
}
