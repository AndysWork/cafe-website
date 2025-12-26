using System.Net;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public class AuthFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly IEmailService _emailService;
    private readonly ILogger _log;

    public AuthFunction(MongoService mongo, AuthService auth, IEmailService emailService, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _emailService = emailService;
        _log = loggerFactory.CreateLogger<AuthFunction>();
    }

    [Function("Login")]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequestData req)
    {
        var auditLogger = new AuditLogger(_log);
        var ipAddress = GetClientIp(req);
        var userAgent = req.Headers.TryGetValues("User-Agent", out var ua) ? ua.First() : null;

        try
        {
            var loginRequest = await req.ReadFromJsonAsync<LoginRequest>();
            if (loginRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request" });
                return badRequest;
            }

            // Sanitize inputs
            loginRequest.Username = InputSanitizer.SanitizeUsername(loginRequest.Username);

            // Check for dangerous content
            if (InputSanitizer.IsPotentiallyDangerous(loginRequest.Username) ||
                InputSanitizer.IsPotentiallyDangerous(loginRequest.Password))
            {
                auditLogger.LogSecurityEvent("XSS Attempt in Login", null, ipAddress, 
                    $"Username: {loginRequest.Username}", SecuritySeverity.High);
                
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid input detected" });
                return badRequest;
            }

            // Validate request
            if (!ValidationHelper.TryValidate(loginRequest, out var validationError))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(validationError!.Value);
                return badRequest;
            }

            // Check for brute force attempts
            var failedAttempts = auditLogger.GetFailedLoginAttempts(loginRequest.Username, 1);
            if (failedAttempts >= 5)
            {
                auditLogger.LogSecurityEvent("Brute Force Attempt", loginRequest.Username, ipAddress,
                    $"Failed attempts: {failedAttempts}", SecuritySeverity.High);
                
                var tooMany = req.CreateResponse(HttpStatusCode.TooManyRequests);
                await tooMany.WriteAsJsonAsync(new { success = false, error = "Too many failed attempts. Please try again later." });
                return tooMany;
            }

            // Get user from database
            var user = await _mongo.GetUserByUsernameAsync(loginRequest.Username);
            if (user == null)
            {
                auditLogger.LogAuthentication(loginRequest.Username, "Login Failed", false, ipAddress, userAgent, "User not found");
                
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { success = false, error = "Invalid username or password" });
                return unauthorized;
            }

            // Verify password
            if (!_auth.VerifyPassword(loginRequest.Password, user.PasswordHash))
            {
                auditLogger.LogAuthentication(user.Id!, "Login Failed", false, ipAddress, userAgent, "Invalid password");
                
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { success = false, error = "Invalid username or password" });
                return unauthorized;
            }

            // Check if user is active
            if (!user.IsActive)
            {
                auditLogger.LogAuthentication(user.Id!, "Login Failed", false, ipAddress, userAgent, "Account deactivated");
                
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { success = false, error = "Account is deactivated" });
                return forbidden;
            }

            // Update last login
            await _mongo.UpdateUserLastLoginAsync(user.Id!);

            // Generate JWT token
            var token = _auth.GenerateJwtToken(user.Id!, user.Username, user.Role);

            // Generate CSRF token
            var csrfToken = CsrfTokenManager.GenerateToken(user.Id!);

            // Log successful login
            auditLogger.LogAuthentication(user.Id!, "Login Success", true, ipAddress, userAgent);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                data = new LoginResponse
                {
                    Token = token,
                    Username = user.Username,
                    Email = user.Email,
                    Role = user.Role,
                    FirstName = user.FirstName,
                    LastName = user.LastName
                },
                csrfToken = csrfToken
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
        var auditLogger = new AuditLogger(_log);
        var ipAddress = GetClientIp(req);
        var userAgent = req.Headers.TryGetValues("User-Agent", out var ua) ? ua.First() : null;

        try
        {
            var registerRequest = await req.ReadFromJsonAsync<RegisterRequest>();
            if (registerRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request" });
                return badRequest;
            }

            // Sanitize inputs
            registerRequest.Username = InputSanitizer.SanitizeUsername(registerRequest.Username);
            registerRequest.Email = InputSanitizer.SanitizeEmail(registerRequest.Email);
            registerRequest.FirstName = InputSanitizer.Sanitize(registerRequest.FirstName);
            registerRequest.LastName = InputSanitizer.Sanitize(registerRequest.LastName);
            registerRequest.PhoneNumber = InputSanitizer.SanitizePhoneNumber(registerRequest.PhoneNumber);

            // Check for dangerous content
            if (InputSanitizer.IsPotentiallyDangerous(registerRequest.Username) ||
                InputSanitizer.IsPotentiallyDangerous(registerRequest.Email) ||
                InputSanitizer.IsPotentiallyDangerous(registerRequest.FirstName) ||
                InputSanitizer.IsPotentiallyDangerous(registerRequest.LastName))
            {
                auditLogger.LogSecurityEvent("XSS Attempt in Registration", null, ipAddress,
                    $"Username: {registerRequest.Username}, Email: {registerRequest.Email}", SecuritySeverity.High);
                
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid input detected" });
                return badRequest;
            }

            // Validate request
            if (!ValidationHelper.TryValidate(registerRequest, out var validationError))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(validationError!.Value);
                return badRequest;
            }

            // Check if username already exists
            var existingUser = await _mongo.GetUserByUsernameAsync(registerRequest.Username);
            if (existingUser != null)
            {
                auditLogger.LogAuthentication(registerRequest.Username, "Registration Failed", false, ipAddress, userAgent, "Username already exists");
                
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { success = false, error = "Username already exists" });
                return conflict;
            }

            // Check if email already exists
            var existingEmail = await _mongo.GetUserByEmailAsync(registerRequest.Email);
            if (existingEmail != null)
            {
                auditLogger.LogAuthentication(registerRequest.Email, "Registration Failed", false, ipAddress, userAgent, "Email already registered");
                
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { success = false, error = "Email already registered" });
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

            // Log successful registration
            auditLogger.LogAuthentication(user.Id!, "Registration Success", true, ipAddress, userAgent);

            // Send welcome email
            var userName = user.FirstName ?? user.Username;
            await _emailService.SendWelcomeEmailAsync(user.Email!, userName);

            _log.LogInformation($"New user registered: {user.Username}");

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new { 
                success = true,
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

    [Function("UpdateProfile")]
    public async Task<HttpResponseData> UpdateProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "auth/profile")] HttpRequestData req)
    {
        var auditLogger = new AuditLogger(_log);
        var ipAddress = GetClientIp(req);

        try
        {
            // Verify authorization
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized || errorResponse != null)
            {
                return errorResponse!;
            }

            var updateRequest = await req.ReadFromJsonAsync<UpdateProfileRequest>();
            if (updateRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request" });
                return badRequest;
            }

            // Sanitize inputs
            if (updateRequest.FirstName != null)
                updateRequest.FirstName = InputSanitizer.Sanitize(updateRequest.FirstName);
            if (updateRequest.LastName != null)
                updateRequest.LastName = InputSanitizer.Sanitize(updateRequest.LastName);
            if (updateRequest.Email != null)
                updateRequest.Email = InputSanitizer.SanitizeEmail(updateRequest.Email);
            if (updateRequest.PhoneNumber != null)
                updateRequest.PhoneNumber = InputSanitizer.SanitizePhoneNumber(updateRequest.PhoneNumber);

            // Validate request
            if (!ValidationHelper.TryValidate(updateRequest, out var validationError))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(validationError!.Value);
                return badRequest;
            }

            // Check if email is already taken by another user
            if (!string.IsNullOrWhiteSpace(updateRequest.Email))
            {
                var existingUser = await _mongo.GetUserByEmailAsync(updateRequest.Email);
                if (existingUser != null && existingUser.Id != userId)
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { success = false, error = "Email is already in use" });
                    return conflict;
                }
            }

            // Update profile
            var updated = await _mongo.UpdateUserProfileAsync(userId!, updateRequest);
            if (!updated)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "No changes made to profile" });
                return badRequest;
            }

            auditLogger.LogDataAccess(userId!, "Users", userId!, "Profile Updated", true);

            // Get updated user data
            var user = await _mongo.GetUserByIdAsync(userId!);
            if (user == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { success = false, error = "User not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "Profile updated successfully",
                data = new
                {
                    user.FirstName,
                    user.LastName,
                    user.Email,
                    PhoneNumber = user.PhoneNumber ?? string.Empty
                }
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating profile");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while updating profile" });
            return res;
        }
    }

    [Function("ChangePassword")]
    public async Task<HttpResponseData> ChangePassword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/password/change")] HttpRequestData req)
    {
        var auditLogger = new AuditLogger(_log);
        var ipAddress = GetClientIp(req);

        try
        {
            // Verify authorization
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized || errorResponse != null)
            {
                return errorResponse!;
            }

            var changePasswordRequest = await req.ReadFromJsonAsync<ChangePasswordRequest>();
            if (changePasswordRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request" });
                return badRequest;
            }

            // Validate request
            if (!ValidationHelper.TryValidate(changePasswordRequest, out var validationError))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(validationError!.Value);
                return badRequest;
            }

            // Check if new password matches confirm password
            if (changePasswordRequest.NewPassword != changePasswordRequest.ConfirmPassword)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "New password and confirm password do not match" });
                return badRequest;
            }

            // Get user
            var user = await _mongo.GetUserByIdAsync(userId!);
            if (user == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { success = false, error = "User not found" });
                return notFound;
            }

            // Verify current password
            if (!_auth.VerifyPassword(changePasswordRequest.CurrentPassword, user.PasswordHash))
            {
                auditLogger.LogSecurityEvent("Failed Password Change", userId!, ipAddress, "Invalid current password", SecuritySeverity.Medium);
                
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { success = false, error = "Current password is incorrect" });
                return unauthorized;
            }

            // Hash new password
            var newPasswordHash = _auth.HashPassword(changePasswordRequest.NewPassword);

            // Update password
            await _mongo.UpdateUserPasswordAsync(userId!, newPasswordHash);

            auditLogger.LogSecurityEvent("Password Changed", userId!, ipAddress, "Password changed successfully", SecuritySeverity.Low);

            // Send password changed notification email
            var userName = user.FirstName ?? user.Username;
            await _emailService.SendPasswordChangedNotificationAsync(user.Email!, userName);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "Password changed successfully"
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error changing password");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while changing password" });
            return res;
        }
    }

    [Function("ForgotPassword")]
    public async Task<HttpResponseData> ForgotPassword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/password/forgot")] HttpRequestData req)
    {
        var auditLogger = new AuditLogger(_log);
        var ipAddress = GetClientIp(req);

        try
        {
            var forgotPasswordRequest = await req.ReadFromJsonAsync<ForgotPasswordRequest>();
            if (forgotPasswordRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request" });
                return badRequest;
            }

            // Sanitize email
            forgotPasswordRequest.Email = InputSanitizer.SanitizeEmail(forgotPasswordRequest.Email);

            // Validate request
            if (!ValidationHelper.TryValidate(forgotPasswordRequest, out var validationError))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(validationError!.Value);
                return badRequest;
            }

            // Get user by email
            var user = await _mongo.GetUserByEmailAsync(forgotPasswordRequest.Email);
            
            // Always return success to prevent email enumeration
            // But only create token if user exists
            if (user != null && user.IsActive)
            {
                // Create password reset token
                var resetToken = await _mongo.CreatePasswordResetTokenAsync(user.Id!);

                auditLogger.LogSecurityEvent("Password Reset Requested", user.Id!, ipAddress, $"Reset token generated", SecuritySeverity.Low);

                // Send password reset email
                var userName = user.FirstName ?? user.Username;
                await _emailService.SendPasswordResetEmailAsync(user.Email!, userName, resetToken.Token);
            }
            else
            {
                auditLogger.LogSecurityEvent("Password Reset Attempt", "unknown", ipAddress, $"Email: {forgotPasswordRequest.Email} (not found)", SecuritySeverity.Low);
            }

            // Always return success message
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "If an account with that email exists, a password reset link has been sent."
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error processing forgot password request");
            auditLogger.LogSecurityEvent("Password Reset Error", "unknown", ipAddress, ex.Message, SecuritySeverity.Medium);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while processing the request" });
            return res;
        }
    }

    [Function("ResetPassword")]
    public async Task<HttpResponseData> ResetPassword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/password/reset")] HttpRequestData req)
    {
        var auditLogger = new AuditLogger(_log);
        var ipAddress = GetClientIp(req);

        try
        {
            var resetPasswordRequest = await req.ReadFromJsonAsync<ResetPasswordRequest>();
            if (resetPasswordRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request" });
                return badRequest;
            }

            // Validate request
            if (!ValidationHelper.TryValidate(resetPasswordRequest, out var validationError))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(validationError!.Value);
                return badRequest;
            }

            // Check if new password matches confirm password
            if (resetPasswordRequest.NewPassword != resetPasswordRequest.ConfirmPassword)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "New password and confirm password do not match" });
                return badRequest;
            }

            // Get password reset token
            var token = await _mongo.GetPasswordResetTokenAsync(resetPasswordRequest.ResetToken);
            if (token == null)
            {
                auditLogger.LogSecurityEvent("Invalid Reset Token", "unknown", ipAddress, $"Token: {resetPasswordRequest.ResetToken}", SecuritySeverity.Medium);
                
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid or expired reset token" });
                return badRequest;
            }

            // Get user
            var user = await _mongo.GetUserByIdAsync(token.UserId);
            if (user == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { success = false, error = "User not found" });
                return notFound;
            }

            // Hash new password
            var newPasswordHash = _auth.HashPassword(resetPasswordRequest.NewPassword);

            // Update password
            await _mongo.UpdateUserPasswordAsync(user.Id!, newPasswordHash);

            // Mark token as used
            await _mongo.MarkPasswordResetTokenAsUsedAsync(token.Id!);

            auditLogger.LogSecurityEvent("Password Reset Successful", user.Id!, ipAddress, "Password reset using token", SecuritySeverity.Low);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "Password has been reset successfully"
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error resetting password");
            auditLogger.LogSecurityEvent("Password Reset Error", "unknown", ipAddress, ex.Message, SecuritySeverity.High);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while resetting password" });
            return res;
        }
    }

    private string GetClientIp(HttpRequestData request)
    {
        // Try to get IP from X-Forwarded-For header (for proxy/load balancer)
        if (request.Headers.TryGetValues("X-Forwarded-For", out var forwardedFor))
        {
            return forwardedFor.First().Split(',')[0].Trim();
        }

        // Try to get IP from X-Real-IP header
        if (request.Headers.TryGetValues("X-Real-IP", out var realIp))
        {
            return realIp.First();
        }

        return "unknown";
    }
}
