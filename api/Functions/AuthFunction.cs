using System.Net;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public class AuthFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public AuthFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<AuthFunction>();
    }

    [Function("Login")]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequestData req)
    {
        try
        {
            var loginRequest = await req.ReadFromJsonAsync<LoginRequest>();
            if (loginRequest == null || string.IsNullOrWhiteSpace(loginRequest.Username) || string.IsNullOrWhiteSpace(loginRequest.Password))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Username and password are required" });
                return badRequest;
            }

            // Get user from database
            var user = await _mongo.GetUserByUsernameAsync(loginRequest.Username);
            if (user == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid username or password" });
                return unauthorized;
            }

            // Verify password
            if (!_auth.VerifyPassword(loginRequest.Password, user.PasswordHash))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid username or password" });
                return unauthorized;
            }

            // Check if user is active
            if (!user.IsActive)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Account is deactivated" });
                return forbidden;
            }

            // Update last login
            await _mongo.UpdateUserLastLoginAsync(user.Id!);

            // Generate JWT token
            var token = _auth.GenerateJwtToken(user.Id!, user.Username, user.Role);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new LoginResponse
            {
                Token = token,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role,
                FirstName = user.FirstName,
                LastName = user.LastName
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error during login");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred during login" });
            return res;
        }
    }

    [Function("Register")]
    public async Task<HttpResponseData> Register(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/register")] HttpRequestData req)
    {
        try
        {
            var registerRequest = await req.ReadFromJsonAsync<RegisterRequest>();
            if (registerRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request" });
                return badRequest;
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(registerRequest.Username) || 
                string.IsNullOrWhiteSpace(registerRequest.Email) || 
                string.IsNullOrWhiteSpace(registerRequest.Password))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Username, email, and password are required" });
                return badRequest;
            }

            // Validate username format (alphanumeric, 3-20 chars)
            if (registerRequest.Username.Length < 3 || registerRequest.Username.Length > 20)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Username must be between 3 and 20 characters" });
                return badRequest;
            }

            // Validate email format
            if (!registerRequest.Email.Contains("@"))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid email format" });
                return badRequest;
            }

            // Validate password strength (min 6 chars)
            if (registerRequest.Password.Length < 6)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Password must be at least 6 characters" });
                return badRequest;
            }

            // Check if username already exists
            var existingUser = await _mongo.GetUserByUsernameAsync(registerRequest.Username);
            if (existingUser != null)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Username already exists" });
                return conflict;
            }

            // Check if email already exists
            var existingEmail = await _mongo.GetUserByEmailAsync(registerRequest.Email);
            if (existingEmail != null)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Email already registered" });
                return conflict;
            }

            // Create new user (always as regular user, not admin)
            var user = new User
            {
                Username = registerRequest.Username,
                Email = registerRequest.Email,
                PasswordHash = _auth.HashPassword(registerRequest.Password),
                Role = "user", // Always create as regular user
                FirstName = registerRequest.FirstName,
                LastName = registerRequest.LastName,
                PhoneNumber = registerRequest.PhoneNumber,
                IsActive = true,
                CreatedAt = MongoService.GetIstNow()
            };

            await _mongo.CreateUserAsync(user);

            _log.LogInformation($"New user registered: {user.Username}");

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new { 
                message = "Registration successful",
                username = user.Username,
                email = user.Email
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error during registration");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred during registration" });
            return res;
        }
    }

    [Function("ValidateToken")]
    public async Task<HttpResponseData> ValidateToken(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/validate")] HttpRequestData req)
    {
        try
        {
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);

            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                return unauthorized;
            }

            var userId = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var username = principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            var role = principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                valid = true,
                userId,
                username,
                role
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error validating token");
            var res = req.CreateResponse(HttpStatusCode.Unauthorized);
            return res;
        }
    }

    [Function("VerifyAdminExists")]
    public async Task<HttpResponseData> VerifyAdminExists(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/admin/verify")] HttpRequestData req)
    {
        try
        {
            var defaultAdminUsername = Environment.GetEnvironmentVariable("DefaultAdmin__Username") ?? "admin";
            var admin = await _mongo.GetUserByUsernameAsync(defaultAdminUsername);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                exists = admin != null,
                username = defaultAdminUsername,
                isActive = admin?.IsActive ?? false,
                email = admin?.Email ?? "N/A",
                createdAt = admin?.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error verifying admin user");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while verifying admin user" });
            return res;
        }
    }
}
