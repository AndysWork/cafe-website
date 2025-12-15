# Admin Access & User Management Guide

## Default Admin Credentials

The system automatically creates a default admin user on first startup:

**Username**: `admin`  
**Password**: `Admin@123`  
**Email**: `admin@cafemaatara.com`

> ⚠️ **IMPORTANT**: Change the default password immediately after first login!

## Admin Login

### Endpoint
```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "admin",
  "password": "Admin@123"
}
```

### Response
```json
{
  "success": true,
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "username": "admin",
    "email": "admin@cafemaatara.com",
    "role": "admin",
    "firstName": "System",
    "lastName": "Administrator"
  },
  "csrfToken": "..."
}
```

**Important**: The `role` field in the response must be `"admin"` to access the admin dashboard.

---

## User Management Endpoints (Admin Only)

All user management endpoints require admin authentication. Include the JWT token in the Authorization header:

```
Authorization: Bearer {your_jwt_token}
```

### 1. Get All Users

**GET** `/api/users`

Lists all registered users.

**Response**:
```json
{
  "success": true,
  "data": [
    {
      "id": "674a123...",
      "username": "john_doe",
      "email": "john@example.com",
      "role": "user",
      "firstName": "John",
      "lastName": "Doe",
      "phoneNumber": "+919876543210",
      "isActive": true,
      "createdAt": "2024-12-15T10:30:00Z",
      "lastLoginAt": "2024-12-15T14:20:00Z"
    }
  ]
}
```

---

### 2. Promote User to Admin

**POST** `/api/users/{userId}/promote`

Promotes a regular user to admin role.

**Example**:
```http
POST /api/users/674a123456789/promote
Authorization: Bearer {admin_jwt_token}
```

**Response**:
```json
{
  "success": true,
  "message": "User 'john_doe' has been promoted to admin",
  "data": {
    "userId": "674a123...",
    "username": "john_doe",
    "role": "admin"
  }
}
```

**Notes**:
- User must exist and have role "user"
- Action is logged in audit logs
- User can immediately access admin endpoints after promotion

---

### 3. Demote Admin to User

**POST** `/api/users/{userId}/demote`

Demotes an admin to regular user role.

**Example**:
```http
POST /api/users/674a123456789/demote
Authorization: Bearer {admin_jwt_token}
```

**Response**:
```json
{
  "success": true,
  "message": "User 'john_doe' has been demoted to regular user",
  "data": {
    "userId": "674a123...",
    "username": "john_doe",
    "role": "user"
  }
}
```

**Notes**:
- Cannot demote yourself (prevents lockout)
- User must exist and have role "admin"
- Action is logged in audit logs
- User loses admin access immediately

---

### 4. Toggle User Active Status

**POST** `/api/users/{userId}/toggle-status`

Activates or deactivates a user account.

**Example**:
```http
POST /api/users/674a123456789/toggle-status
Authorization: Bearer {admin_jwt_token}
```

**Response**:
```json
{
  "success": true,
  "message": "User 'john_doe' has been deactivated",
  "data": {
    "userId": "674a123...",
    "username": "john_doe",
    "isActive": false
  }
}
```

**Notes**:
- Cannot deactivate yourself (prevents lockout)
- Toggles between active/inactive
- Inactive users cannot log in
- Action is logged in audit logs

---

## Quick Start Guide

### Option 1: Use Default Admin (Recommended)

1. **Login with default credentials**:
   ```bash
   curl -X POST http://localhost:7071/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{"username":"admin","password":"Admin@123"}'
   ```

2. **Save the JWT token** from the response

3. **Access admin dashboard** using the token

### Option 2: Promote Existing User

If you already registered a user and want to make them admin:

1. **Login as default admin** (see Option 1)

2. **Get all users** to find your user ID:
   ```bash
   curl http://localhost:7071/api/users \
     -H "Authorization: Bearer {admin_token}"
   ```

3. **Promote your user to admin**:
   ```bash
   curl -X POST http://localhost:7071/api/users/{your_user_id}/promote \
     -H "Authorization: Bearer {admin_token}"
   ```

4. **Logout and login with your credentials**

---

## Troubleshooting

### Issue: "Admin access required" error

**Cause**: Your user account has role "user" instead of "admin"

**Solution**: 
1. Login as default admin (username: `admin`, password: `Admin@123`)
2. Use the "Promote User to Admin" endpoint with your user ID
3. Logout and login again with your credentials

### Issue: Default admin login not working

**Cause**: Admin user may not have been created on startup

**Solution**:
1. Check console logs for "Default admin user created successfully"
2. Restart the Azure Functions app
3. If issue persists, check MongoDB connection

### Issue: Cannot see admin dashboard after login

**Symptoms**: Login succeeds but dashboard shows "Access Denied"

**Cause**: Frontend is checking the `role` field in the login response

**Solution**:
1. Verify the login response contains `"role": "admin"`
2. Check browser localStorage/sessionStorage for the user role
3. Ensure frontend is reading the role from the correct location

### Issue: "Cannot demote yourself" error

**Cause**: You're trying to demote or deactivate your own account

**Solution**: 
1. Create another admin user first
2. Login as the new admin
3. Then demote/deactivate the other account

---

## Security Notes

1. **Password Security**: 
   - Default password is `Admin@123` - **change it immediately!**
   - Use strong passwords for admin accounts
   - Password must be at least 8 characters

2. **Access Control**:
   - All user management endpoints require admin role
   - Self-modification is prevented for safety
   - All admin actions are logged in audit logs

3. **Audit Logging**:
   - User promotions/demotions are logged
   - Status changes are tracked
   - View audit logs at `/api/security/audit/logs`

4. **Rate Limiting**:
   - Login endpoint has brute force protection (5 attempts/hour)
   - General rate limits apply (60 req/min, 1000 req/hour)

---

## API Summary

| Endpoint | Method | Description | Auth Required |
|----------|--------|-------------|---------------|
| `/api/users` | GET | List all users | Admin |
| `/api/users/{id}/promote` | POST | Promote to admin | Admin |
| `/api/users/{id}/demote` | POST | Demote to user | Admin |
| `/api/users/{id}/toggle-status` | POST | Activate/deactivate | Admin |

---

## Environment Variables

You can customize the default admin credentials using these environment variables:

```json
{
  "DefaultAdmin__Username": "admin",
  "DefaultAdmin__Password": "Admin@123",
  "DefaultAdmin__Email": "admin@cafemaatara.com"
}
```

Add these to `local.settings.json` in the Values section for local development, or configure in Azure App Settings for production.

---

## Next Steps

1. ✅ Login with default admin credentials
2. ✅ Verify you can access admin endpoints
3. ✅ (Optional) Promote your personal account to admin
4. ✅ Change the default admin password
5. ✅ Test admin dashboard access in frontend
