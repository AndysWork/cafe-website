# User Profile Management Documentation

## Overview

This document provides comprehensive documentation for the user profile management features implemented in the Cafe Website application, including profile updates, password changes, and password reset functionality.

## Features Implemented

### 1. **Update Profile** ✅
- Users can update their profile information
- Fields: First Name, Last Name, Email, Phone Number
- Email uniqueness validation
- Input sanitization and validation
- Audit logging for security tracking

### 2. **Change Password** ✅
- Authenticated users can change their password
- Requires current password verification
- Password confirmation validation
- BCrypt password hashing
- Security event logging

### 3. **Forgot Password** ✅
- Password reset via secure token
- Email enumeration protection
- 1-hour token expiration
- Token-based reset flow
- **Note**: Email sending functionality pending (see Email Integration section)

### 4. **Reset Password** ✅
- Token-based password reset
- Token validation and expiration check
- One-time use tokens
- Password confirmation validation
- Security event logging

---

## API Endpoints

### 1. Update Profile

**Endpoint**: `PUT /api/auth/profile`

**Authentication**: Required (Bearer Token)

**Request Headers**:
```
Authorization: Bearer <jwt-token>
Content-Type: application/json
```

**Request Body**:
```json
{
  "firstName": "John",
  "lastName": "Doe",
  "email": "john.doe@example.com",
  "phoneNumber": "9876543210"
}
```

**Validation Rules**:
- `firstName`: Optional, max 50 characters
- `lastName`: Optional, max 50 characters
- `email`: Optional, must be valid email format, max 100 characters
- `phoneNumber`: Optional, must match Indian phone number format (10 digits)

**Success Response** (200 OK):
```json
{
  "success": true,
  "message": "Profile updated successfully",
  "data": {
    "firstName": "John",
    "lastName": "Doe",
    "email": "john.doe@example.com",
    "phoneNumber": "9876543210"
  }
}
```

**Error Responses**:

- **400 Bad Request** (Validation Error):
```json
{
  "success": false,
  "error": "Invalid request"
}
```

- **401 Unauthorized** (No token or invalid token):
```json
{
  "error": "Authorization header missing or invalid"
}
```

- **409 Conflict** (Email already in use):
```json
{
  "success": false,
  "error": "Email is already in use"
}
```

**Security Features**:
- Input sanitization (XSS prevention)
- Email uniqueness validation
- Audit logging for data access tracking
- IP address logging

---

### 2. Change Password

**Endpoint**: `POST /api/auth/password/change`

**Authentication**: Required (Bearer Token)

**Request Headers**:
```
Authorization: Bearer <jwt-token>
Content-Type: application/json
```

**Request Body**:
```json
{
  "currentPassword": "OldPassword@123",
  "newPassword": "NewPassword@456",
  "confirmPassword": "NewPassword@456"
}
```

**Validation Rules**:
- `currentPassword`: Required, min 6 characters, max 100 characters
- `newPassword`: Required, min 8 characters, max 100 characters
- `confirmPassword`: Required, must match `newPassword`

**Success Response** (200 OK):
```json
{
  "success": true,
  "message": "Password changed successfully"
}
```

**Error Responses**:

- **400 Bad Request** (Password mismatch):
```json
{
  "success": false,
  "error": "New password and confirm password do not match"
}
```

- **401 Unauthorized** (Invalid current password):
```json
{
  "success": false,
  "error": "Current password is incorrect"
}
```

- **404 Not Found** (User not found):
```json
{
  "success": false,
  "error": "User not found"
}
```

**Security Features**:
- Current password verification
- BCrypt password hashing
- Password confirmation validation
- Security event logging (success & failures)
- IP address tracking

---

### 3. Forgot Password

**Endpoint**: `POST /api/auth/password/forgot`

**Authentication**: Not required (Anonymous)

**Request Body**:
```json
{
  "email": "user@example.com"
}
```

**Validation Rules**:
- `email`: Required, must be valid email format

**Success Response** (200 OK):
```json
{
  "success": true,
  "message": "If an account with that email exists, a password reset link has been sent."
}
```

**Security Features**:
- Email enumeration protection (always returns success message)
- Input sanitization
- 64-character secure token generation (two GUIDs)
- 1-hour token expiration
- Security event logging
- IP address tracking

**Token Generation**:
- Token format: `{GUID1}{GUID2}` (64 characters, alphanumeric)
- Expiration: 1 hour from creation
- Single-use tokens (marked as used after reset)
- Stored in `PasswordResetTokens` collection

**Email Integration** (Pending):
Currently, the reset token is logged to the console for development purposes:
```
Password reset token for user@example.com: a1b2c3d4...
Reset link: /reset-password?token=a1b2c3d4...
```

**Production Implementation**:
```csharp
// TODO: Replace console logging with email service
await _emailService.SendPasswordResetEmailAsync(user.Email, resetToken.Token);
```

---

### 4. Reset Password

**Endpoint**: `POST /api/auth/password/reset`

**Authentication**: Not required (Anonymous, uses token)

**Request Body**:
```json
{
  "resetToken": "a1b2c3d4e5f6...64-char-token",
  "newPassword": "NewPassword@789",
  "confirmPassword": "NewPassword@789"
}
```

**Validation Rules**:
- `resetToken`: Required
- `newPassword`: Required, min 8 characters, max 100 characters
- `confirmPassword`: Required, must match `newPassword`

**Success Response** (200 OK):
```json
{
  "success": true,
  "message": "Password has been reset successfully"
}
```

**Error Responses**:

- **400 Bad Request** (Invalid or expired token):
```json
{
  "success": false,
  "error": "Invalid or expired reset token"
}
```

- **400 Bad Request** (Password mismatch):
```json
{
  "success": false,
  "error": "New password and confirm password do not match"
}
```

- **404 Not Found** (User not found):
```json
{
  "success": false,
  "error": "User not found"
}
```

**Security Features**:
- Token validation (expiration check)
- One-time use tokens (marked as used)
- Password confirmation validation
- BCrypt password hashing
- Security event logging
- IP address tracking

**Token Validation**:
- Checks token exists
- Verifies not expired (`expiresAt > now`)
- Verifies not already used (`isUsed == false`)
- Marks token as used after successful reset

---

## Database Schema

### PasswordResetTokens Collection

```json
{
  "_id": "ObjectId",
  "userId": "ObjectId (references Users)",
  "token": "64-character alphanumeric string (unique)",
  "expiresAt": "ISODate (IST timezone)",
  "createdAt": "ISODate (IST timezone)",
  "isUsed": "boolean (default: false)"
}
```

**Indexes**:
1. `token_1` (Unique) - Fast token lookup
2. `userId_1` - User's reset tokens
3. `expiresAt_1` - Cleanup expired tokens

**Collection Statistics**:
- Auto-cleanup of expired tokens via `DeleteExpiredPasswordResetTokensAsync()`
- Recommended: Schedule periodic cleanup (e.g., daily cron job)

---

## MongoDB Service Methods

### Profile Management

**1. UpdateUserProfileAsync**
```csharp
public async Task<bool> UpdateUserProfileAsync(string userId, UpdateProfileRequest profile)
```
- Updates user profile fields (firstName, lastName, email, phoneNumber)
- Only updates non-null/non-empty fields
- Returns `true` if at least one field was updated
- Returns `false` if no changes were made

**2. UpdateUserPasswordAsync**
```csharp
public async Task<bool> UpdateUserPasswordAsync(string userId, string newPasswordHash)
```
- Updates user's password hash
- Used by both ChangePassword and ResetPassword endpoints
- Returns `true` if password was updated

### Password Reset Token Management

**1. CreatePasswordResetTokenAsync**
```csharp
public async Task<PasswordResetToken> CreatePasswordResetTokenAsync(string userId)
```
- Generates 64-character secure token
- Sets 1-hour expiration
- Stores in PasswordResetTokens collection
- Returns created token

**2. GetPasswordResetTokenAsync**
```csharp
public async Task<PasswordResetToken?> GetPasswordResetTokenAsync(string token)
```
- Retrieves valid token (not expired, not used)
- Returns `null` if token invalid or expired
- Filters: `token == token && !isUsed && expiresAt > now`

**3. MarkPasswordResetTokenAsUsedAsync**
```csharp
public async Task<bool> MarkPasswordResetTokenAsUsedAsync(string tokenId)
```
- Marks token as used (one-time use)
- Prevents token reuse attacks
- Returns `true` if token was marked

**4. DeleteExpiredPasswordResetTokensAsync**
```csharp
public async Task DeleteExpiredPasswordResetTokensAsync()
```
- Deletes expired or used tokens
- Recommended: Run daily via scheduled job
- Query: `expiresAt < now || isUsed == true`

---

## Security Features

### Input Sanitization

All user inputs are sanitized using `InputSanitizer`:

```csharp
// Profile Update
updateRequest.FirstName = InputSanitizer.Sanitize(updateRequest.FirstName);
updateRequest.LastName = InputSanitizer.Sanitize(updateRequest.LastName);
updateRequest.Email = InputSanitizer.SanitizeEmail(updateRequest.Email);
updateRequest.PhoneNumber = InputSanitizer.SanitizePhoneNumber(updateRequest.PhoneNumber);

// Forgot Password
forgotPasswordRequest.Email = InputSanitizer.SanitizeEmail(forgotPasswordRequest.Email);
```

### Audit Logging

All profile and password operations are logged:

**Data Access Logging**:
```csharp
auditLogger.LogDataAccess(userId, "Users", userId, "Profile Updated", true);
```

**Security Event Logging**:
```csharp
// Password Change Success
auditLogger.LogSecurityEvent("Password Changed", userId, ipAddress, 
    "Password changed successfully", SecuritySeverity.Low);

// Failed Password Change
auditLogger.LogSecurityEvent("Failed Password Change", userId, ipAddress, 
    "Invalid current password", SecuritySeverity.Medium);

// Password Reset Requested
auditLogger.LogSecurityEvent("Password Reset Requested", userId, ipAddress, 
    "Reset token generated", SecuritySeverity.Low);

// Invalid Reset Token
auditLogger.LogSecurityEvent("Invalid Reset Token", "unknown", ipAddress, 
    $"Token: {resetToken}", SecuritySeverity.Medium);
```

### Authorization

**Protected Endpoints** (Require JWT token):
- `PUT /api/auth/profile` - Update Profile
- `POST /api/auth/password/change` - Change Password

**Public Endpoints** (Anonymous access):
- `POST /api/auth/password/forgot` - Forgot Password
- `POST /api/auth/password/reset` - Reset Password

**Authorization Validation**:
```csharp
var (isAuthorized, userId, _, errorResponse) = 
    await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
if (!isAuthorized || errorResponse != null)
{
    return errorResponse!;
}
```

### Password Hashing

- Algorithm: **BCrypt**
- Work factor: Configured in `AuthService`
- All passwords stored as hashed values
- Never log or transmit plain-text passwords

### Email Enumeration Protection

The forgot password endpoint always returns success:
```json
{
  "success": true,
  "message": "If an account with that email exists, a password reset link has been sent."
}
```

This prevents attackers from:
- Discovering valid user emails
- Enumerating registered accounts
- Gathering user information

---

## Email Integration (Pending Implementation)

### Current Status

Password reset tokens are currently logged to console:
```csharp
_log.LogWarning($"Password reset token for {user.Email}: {resetToken.Token}");
_log.LogWarning($"Reset link: /reset-password?token={resetToken.Token}");
```

### Production Implementation Required

**Email Service Interface**:
```csharp
public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string toEmail, string resetToken);
    Task SendPasswordChangedNotificationAsync(string toEmail);
    Task SendProfileUpdatedNotificationAsync(string toEmail);
}
```

**Email Template Example** (Password Reset):
```html
<!DOCTYPE html>
<html>
<head>
    <title>Password Reset Request</title>
</head>
<body>
    <h2>Password Reset Request</h2>
    <p>You requested to reset your password for Maa Tara Cafe.</p>
    <p>Click the link below to reset your password (valid for 1 hour):</p>
    <a href="https://cafe.example.com/reset-password?token={resetToken}">
        Reset Password
    </a>
    <p>If you didn't request this, please ignore this email.</p>
</body>
</html>
```

**Recommended Email Services**:
1. **SendGrid** - Popular, good free tier
2. **Amazon SES** - Cost-effective, scalable
3. **Mailgun** - Developer-friendly
4. **Azure Communication Services** - Integrates with Azure Functions

**Implementation Steps**:
1. Choose email service provider
2. Create email templates (HTML & text)
3. Implement `IEmailService` interface
4. Register service in `Program.cs`
5. Replace console logging with email sending
6. Add email configuration to `local.settings.json`
7. Test email delivery

**Environment Configuration**:
```json
{
  "Values": {
    "EmailService__Provider": "SendGrid",
    "EmailService__ApiKey": "your-api-key",
    "EmailService__FromEmail": "noreply@cafemaatara.com",
    "EmailService__FromName": "Maa Tara Cafe"
  }
}
```

---

## Frontend Integration

### Angular Service Methods (To Be Added)

**auth.service.ts**:
```typescript
// Update Profile
updateProfile(profile: UpdateProfileRequest): Observable<any> {
  return this.http.put('/api/auth/profile', profile, {
    headers: { 'Authorization': `Bearer ${this.getToken()}` }
  });
}

// Change Password
changePassword(passwords: ChangePasswordRequest): Observable<any> {
  return this.http.post('/api/auth/password/change', passwords, {
    headers: { 'Authorization': `Bearer ${this.getToken()}` }
  });
}

// Forgot Password
forgotPassword(email: string): Observable<any> {
  return this.http.post('/api/auth/password/forgot', { email });
}

// Reset Password
resetPassword(token: string, newPassword: string, confirmPassword: string): Observable<any> {
  return this.http.post('/api/auth/password/reset', {
    resetToken: token,
    newPassword,
    confirmPassword
  });
}
```

### Component Examples

**1. Profile Update Component**:
```typescript
export class ProfileComponent implements OnInit {
  profileForm: FormGroup;

  constructor(
    private authService: AuthService,
    private fb: FormBuilder
  ) {
    this.profileForm = this.fb.group({
      firstName: ['', [Validators.maxLength(50)]],
      lastName: ['', [Validators.maxLength(50)]],
      email: ['', [Validators.email, Validators.maxLength(100)]],
      phoneNumber: ['', [Validators.pattern(/^[0-9]{10}$/)]]
    });
  }

  ngOnInit() {
    this.loadUserProfile();
  }

  loadUserProfile() {
    this.authService.getUserProfile().subscribe(profile => {
      this.profileForm.patchValue(profile);
    });
  }

  updateProfile() {
    if (this.profileForm.valid) {
      this.authService.updateProfile(this.profileForm.value).subscribe({
        next: (response) => {
          console.log('Profile updated successfully');
          // Show success message
        },
        error: (error) => {
          console.error('Error updating profile:', error);
          // Show error message
        }
      });
    }
  }
}
```

**2. Change Password Component**:
```typescript
export class ChangePasswordComponent {
  passwordForm: FormGroup;

  constructor(
    private authService: AuthService,
    private fb: FormBuilder
  ) {
    this.passwordForm = this.fb.group({
      currentPassword: ['', [Validators.required, Validators.minLength(6)]],
      newPassword: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', [Validators.required]]
    }, {
      validators: this.passwordMatchValidator
    });
  }

  passwordMatchValidator(g: FormGroup) {
    return g.get('newPassword')?.value === g.get('confirmPassword')?.value
      ? null : { 'mismatch': true };
  }

  changePassword() {
    if (this.passwordForm.valid) {
      this.authService.changePassword(this.passwordForm.value).subscribe({
        next: (response) => {
          console.log('Password changed successfully');
          this.passwordForm.reset();
          // Show success message
        },
        error: (error) => {
          console.error('Error changing password:', error);
          // Show error message
        }
      });
    }
  }
}
```

**3. Forgot Password Component**:
```typescript
export class ForgotPasswordComponent {
  email: string = '';
  submitted: boolean = false;

  constructor(private authService: AuthService) {}

  forgotPassword() {
    if (this.email) {
      this.authService.forgotPassword(this.email).subscribe({
        next: (response) => {
          this.submitted = true;
          console.log('Reset email sent');
          // Show success message
        },
        error: (error) => {
          console.error('Error:', error);
          // Still show success to prevent email enumeration
          this.submitted = true;
        }
      });
    }
  }
}
```

**4. Reset Password Component**:
```typescript
export class ResetPasswordComponent implements OnInit {
  resetForm: FormGroup;
  token: string = '';

  constructor(
    private authService: AuthService,
    private route: ActivatedRoute,
    private router: Router,
    private fb: FormBuilder
  ) {
    this.resetForm = this.fb.group({
      newPassword: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', [Validators.required]]
    }, {
      validators: this.passwordMatchValidator
    });
  }

  ngOnInit() {
    this.token = this.route.snapshot.queryParams['token'] || '';
  }

  passwordMatchValidator(g: FormGroup) {
    return g.get('newPassword')?.value === g.get('confirmPassword')?.value
      ? null : { 'mismatch': true };
  }

  resetPassword() {
    if (this.resetForm.valid && this.token) {
      const { newPassword, confirmPassword } = this.resetForm.value;
      this.authService.resetPassword(this.token, newPassword, confirmPassword).subscribe({
        next: (response) => {
          console.log('Password reset successfully');
          this.router.navigate(['/login']);
          // Show success message
        },
        error: (error) => {
          console.error('Error resetting password:', error);
          // Show error message
        }
      });
    }
  }
}
```

---

## Testing Guidelines

### Manual Testing

**1. Update Profile Test**:
```bash
# Get JWT token from login
TOKEN="your-jwt-token"

# Update profile
curl -X PUT http://localhost:7071/api/auth/profile \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "firstName": "John",
    "lastName": "Doe",
    "email": "john.doe@example.com",
    "phoneNumber": "9876543210"
  }'
```

**2. Change Password Test**:
```bash
curl -X POST http://localhost:7071/api/auth/password/change \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "currentPassword": "OldPassword@123",
    "newPassword": "NewPassword@456",
    "confirmPassword": "NewPassword@456"
  }'
```

**3. Forgot Password Test**:
```bash
curl -X POST http://localhost:7071/api/auth/password/forgot \
  -H "Content-Type: application/json" \
  -d '{
    "email": "user@example.com"
  }'

# Check console logs for reset token
```

**4. Reset Password Test**:
```bash
# Use token from console logs
curl -X POST http://localhost:7071/api/auth/password/reset \
  -H "Content-Type: application/json" \
  -d '{
    "resetToken": "a1b2c3d4...from-console",
    "newPassword": "NewPassword@789",
    "confirmPassword": "NewPassword@789"
  }'
```

### Unit Testing (To Be Implemented)

**Recommended Test Cases**:

1. **Update Profile Tests**:
   - Valid profile update
   - Email uniqueness validation
   - Invalid email format
   - Phone number validation
   - Empty update request
   - Unauthorized access

2. **Change Password Tests**:
   - Valid password change
   - Invalid current password
   - Password mismatch
   - Weak password validation
   - Unauthorized access

3. **Forgot Password Tests**:
   - Valid email
   - Non-existent email
   - Invalid email format
   - Token generation
   - Token expiration

4. **Reset Password Tests**:
   - Valid token
   - Expired token
   - Used token
   - Invalid token
   - Password mismatch

---

## Error Handling

### Common Error Scenarios

**1. Invalid JWT Token**:
```json
{
  "error": "Invalid or expired token"
}
```

**2. Validation Errors**:
```json
{
  "success": false,
  "errors": {
    "Email": ["The Email field is not a valid e-mail address."],
    "PhoneNumber": ["Phone number must be a valid Indian number"]
  }
}
```

**3. Password Mismatch**:
```json
{
  "success": false,
  "error": "New password and confirm password do not match"
}
```

**4. Expired Reset Token**:
```json
{
  "success": false,
  "error": "Invalid or expired reset token"
}
```

### Error Logging

All errors are logged with context:
```csharp
_log.LogError(ex, "Error updating profile");
_log.LogError(ex, "Error changing password");
_log.LogError(ex, "Error processing forgot password request");
_log.LogError(ex, "Error resetting password");
```

---

## Performance Considerations

### Database Indexing

**Existing Indexes** (User Collection):
- `username_1` (Unique)
- `email_1` (Unique)
- `phoneNumber_1`
- `role_1`

**New Indexes** (PasswordResetTokens Collection):
- `token_1` (Unique) - Fast token lookup
- `userId_1` - User's reset tokens
- `expiresAt_1` - Cleanup expired tokens

### Token Cleanup

**Recommended**: Schedule periodic cleanup of expired tokens

**Implementation**:
```csharp
// Azure Function Timer Trigger (daily at 2 AM IST)
[Function("CleanupExpiredTokens")]
public async Task CleanupExpiredTokens(
    [TimerTrigger("0 0 2 * * *")] TimerInfo timer)
{
    await _mongo.DeleteExpiredPasswordResetTokensAsync();
    _log.LogInformation("Cleaned up expired password reset tokens");
}
```

---

## Security Best Practices

### ✅ Implemented

1. **Password Hashing**: BCrypt with salt
2. **Input Sanitization**: All user inputs sanitized
3. **Validation**: Comprehensive data validation
4. **Authorization**: JWT token verification
5. **Audit Logging**: All operations logged
6. **Email Enumeration Protection**: Always return success
7. **Token Expiration**: 1-hour expiration
8. **One-Time Tokens**: Tokens marked as used
9. **IP Tracking**: IP address logged for all operations
10. **HTTPS Only**: Enforce HTTPS in production

### ⚠️ Recommendations

1. **Rate Limiting**: Implement rate limiting on password reset endpoints
2. **CAPTCHA**: Add CAPTCHA to forgot password form
3. **2FA**: Consider two-factor authentication
4. **Password Policy**: Enforce strong passwords (uppercase, lowercase, numbers, symbols)
5. **Session Management**: Invalidate sessions on password change
6. **Email Verification**: Verify email ownership before allowing reset

---

## Production Checklist

- [x] Backend endpoints implemented
- [x] Input sanitization
- [x] Validation
- [x] Authorization
- [x] Audit logging
- [x] Database indexes
- [x] Password hashing
- [x] Token generation
- [ ] Email service integration ⚠️
- [ ] Frontend components
- [ ] Unit tests
- [ ] Integration tests
- [ ] Rate limiting
- [ ] CAPTCHA integration
- [ ] Production email templates
- [ ] Error monitoring
- [ ] Performance testing

---

## Conclusion

The user profile management system provides a secure, comprehensive solution for:
- User profile updates
- Password changes
- Password reset functionality

**Current Status**: Backend implementation complete, email integration pending

**Next Steps**:
1. Integrate email service for password reset emails
2. Implement frontend components
3. Add comprehensive testing
4. Deploy to production

For questions or issues, refer to the main project documentation or contact the development team.

---

**Last Updated**: December 2024
**Version**: 1.0
**Status**: Backend Complete, Email Integration Pending
