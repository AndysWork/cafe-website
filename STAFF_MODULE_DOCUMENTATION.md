# Staff Management Module Documentation

## Overview
The Staff Management Module provides comprehensive functionality for managing cafe staff members. This module allows administrators to track employee information, manage assignments, monitor performance, and handle all aspects of staff administration.

## Implementation Summary

### Files Created/Modified

1. **Models/Staff.cs** - Staff data model with comprehensive employee information
2. **Services/MongoService.cs** - Added Staff collection and CRUD operations
3. **Functions/StaffFunction.cs** - API endpoints for staff management

---

## Staff Model Structure

### Core Staff Information
- **Employee ID**: Unique identifier for each staff member
- **Personal Details**: First name, last name, email, phone number, alternate phone
- **Demographics**: Date of birth, gender
- **Address**: Complete address details (street, city, state, postal code, country)
- **Emergency Contact**: Name, relationship, phone numbers

### Employment Details
- **Position**: Job role (Manager, Cashier, Barista, Chef, Waiter, etc.)
- **Department**: Work department (Kitchen, Service, Management, etc.)
- **Employment Type**: Full-Time, Part-Time, or Contract
- **Hire Date**: Date of joining
- **Probation End Date**: End of probation period
- **Termination Date**: If applicable
- **Active Status**: Whether staff member is currently active

### Compensation
- **Salary**: Compensation amount
- **Salary Type**: Monthly, Daily, or Hourly
- **Bank Details**: Account holder name, account number, bank name, IFSC code, branch

### Outlet Assignment
- **Primary Outlet**: Main assigned outlet
- **Multiple Outlets**: Can work at multiple locations flag
- **Additional Outlets**: List of other outlets where staff can work

### Work Schedule
- **Working Days**: List of days (Monday, Tuesday, etc.)
- **Shift Start Time**: e.g., "09:00"
- **Shift End Time**: e.g., "18:00"

### Documents
- **Document Type**: Aadhar, PAN, Resume, etc.
- **Document Number**: Identification number
- **Document URL**: Link to stored document
- **Upload Date**: When document was added
- **Expiry Date**: If applicable
- **Verification Status**: Whether document is verified

### Performance & Skills
- **Performance Rating**: 0-5 scale rating
- **Notes**: General notes about the staff member
- **Skills**: List of skills/competencies

### Leave Management
- **Annual Leave Balance**: Days of annual leave remaining
- **Sick Leave Balance**: Days of sick leave remaining
- **Casual Leave Balance**: Days of casual leave remaining

### Audit Fields
- **Created At/By**: When and who created the record
- **Updated At/By**: When and who last updated the record

---

## API Endpoints

All endpoints require **Admin authentication** via Bearer JWT token.

### 1. Get All Staff Members
**GET** `/api/staff`

Query Parameters:
- `activeOnly` (optional, boolean): Filter to show only active staff

Response:
```json
{
  "success": true,
  "data": [/* array of staff objects */]
}
```

---

### 2. Get Staff Member by ID
**GET** `/api/staff/{staffId}`

Response:
```json
{
  "success": true,
  "data": {/* staff object */}
}
```

---

### 3. Get Staff by Outlet
**GET** `/api/staff/outlet/{outletId}`

Returns all active staff members assigned to the specified outlet.

---

### 4. Search Staff
**GET** `/api/staff/search?q={searchTerm}`

Searches staff by name, email, or employee ID.

---

### 5. Get Staff Statistics
**GET** `/api/staff/statistics`

Returns comprehensive statistics including:
- Total staff count
- Active/Inactive counts
- Full-time/Part-time/Contract counts
- Staff distribution by position
- Staff distribution by department

Response:
```json
{
  "success": true,
  "data": {
    "totalStaff": 25,
    "activeStaff": 23,
    "inactiveStaff": 2,
    "fullTimeStaff": 18,
    "partTimeStaff": 4,
    "contractStaff": 1,
    "staffByPosition": {
      "Manager": 3,
      "Barista": 8,
      "Chef": 6,
      "Waiter": 6
    },
    "staffByDepartment": {
      "Kitchen": 8,
      "Service": 12,
      "Management": 3
    }
  }
}
```

---

### 6. Create Staff Member
**POST** `/api/staff`

Request Body:
```json
{
  "employeeId": "EMP001",
  "firstName": "John",
  "lastName": "Doe",
  "email": "john.doe@example.com",
  "phoneNumber": "+91-9876543210",
  "position": "Barista",
  "department": "Service",
  "employmentType": "Full-Time",
  "hireDate": "2025-01-14T00:00:00Z",
  "salary": 25000,
  "salaryType": "Monthly",
  "assignedOutletId": "outlet123",
  "workingDays": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
  "shiftStartTime": "09:00",
  "shiftEndTime": "18:00",
  "address": {
    "street": "123 Main St",
    "city": "Mumbai",
    "state": "Maharashtra",
    "postalCode": "400001",
    "country": "India"
  },
  "emergencyContact": {
    "name": "Jane Doe",
    "relationship": "Spouse",
    "phoneNumber": "+91-9876543211"
  }
}
```

Response:
```json
{
  "success": true,
  "data": {/* created staff object with ID */}
}
```

---

### 7. Update Staff Member
**PUT** `/api/staff/{staffId}`

Request Body: Complete staff object with updated fields

---

### 8. Deactivate Staff Member
**POST** `/api/staff/{staffId}/deactivate`

Soft deletes the staff member (sets isActive to false).

---

### 9. Activate Staff Member
**POST** `/api/staff/{staffId}/activate`

Re-activates a previously deactivated staff member.

---

### 10. Delete Staff Member
**DELETE** `/api/staff/{staffId}`

Permanently deletes a staff member (use with caution).

---

### 11. Update Staff Salary
**PATCH** `/api/staff/{staffId}/salary`

Request Body:
```json
{
  "salary": 30000
}
```

---

### 12. Update Performance Rating
**PATCH** `/api/staff/{staffId}/performance`

Request Body:
```json
{
  "rating": 4.5
}
```

Rating must be between 0 and 5.

---

### 13. Update Leave Balances
**PATCH** `/api/staff/{staffId}/leave-balances`

Request Body:
```json
{
  "annualLeave": 15,
  "sickLeave": 7,
  "casualLeave": 5
}
```

---

## MongoDB Service Methods

The following methods are available in MongoService for staff operations:

### Query Methods
- `GetAllStaffAsync()` - Get all staff members
- `GetActiveStaffAsync()` - Get only active staff
- `GetStaffByIdAsync(staffId)` - Get staff by ID
- `GetStaffByEmployeeIdAsync(employeeId)` - Get staff by employee ID
- `GetStaffByEmailAsync(email)` - Get staff by email
- `GetStaffByOutletAsync(outletId)` - Get staff assigned to outlet
- `GetStaffByPositionAsync(position)` - Get staff by position
- `GetStaffByDepartmentAsync(department)` - Get staff by department
- `SearchStaffAsync(searchTerm)` - Search staff by name/email/ID
- `GetStaffCountByOutletAsync(outletId)` - Count staff at outlet
- `GetStaffStatisticsAsync()` - Get comprehensive statistics

### Modification Methods
- `CreateStaffAsync(staff)` - Create new staff member
- `UpdateStaffAsync(staffId, updatedStaff)` - Update staff member
- `UpdateStaffActiveStatusAsync(staffId, isActive, updatedBy)` - Change active status
- `UpdateStaffSalaryAsync(staffId, newSalary, updatedBy)` - Update salary
- `UpdateStaffOutletAsync(staffId, outletId, updatedBy)` - Change outlet assignment
- `UpdateStaffPositionAsync(staffId, position, updatedBy)` - Update position
- `UpdateStaffPerformanceRatingAsync(staffId, rating, updatedBy)` - Update rating
- `AddStaffDocumentAsync(staffId, document)` - Add document to staff
- `UpdateStaffLeaveBalancesAsync(staffId, annual, sick, casual, updatedBy)` - Update leave balances
- `DeleteStaffAsync(staffId, deletedBy)` - Soft delete (deactivate)
- `HardDeleteStaffAsync(staffId)` - Permanent delete

---

## Security Features

1. **Admin-Only Access**: All endpoints require admin role authentication
2. **Audit Logging**: All staff operations are logged with:
   - Admin user ID who performed the action
   - Action type (CREATE_STAFF, UPDATE_STAFF, etc.)
   - Target staff member ID
   - Details of the operation
3. **Validation**: Required fields are validated before creation
4. **Duplicate Prevention**: Employee ID and email must be unique
5. **Soft Delete**: Default deletion is soft (deactivation) to preserve data

---

## Usage Examples

### Example 1: Add a New Staff Member
```http
POST /api/staff
Authorization: Bearer {admin_jwt_token}
Content-Type: application/json

{
  "employeeId": "EMP001",
  "firstName": "Rajesh",
  "lastName": "Kumar",
  "email": "rajesh.kumar@cafe.com",
  "phoneNumber": "+91-9876543210",
  "position": "Barista",
  "department": "Service",
  "employmentType": "Full-Time",
  "salary": 22000,
  "salaryType": "Monthly",
  "assignedOutletId": "outlet_mumbai_01"
}
```

### Example 2: Search for Staff
```http
GET /api/staff/search?q=rajesh
Authorization: Bearer {admin_jwt_token}
```

### Example 3: Update Staff Performance
```http
PATCH /api/staff/67xxx/performance
Authorization: Bearer {admin_jwt_token}
Content-Type: application/json

{
  "rating": 4.5
}
```

### Example 4: Get Staff Statistics
```http
GET /api/staff/statistics
Authorization: Bearer {admin_jwt_token}
```

---

## Database Collection

**Collection Name**: `Staff`

**Indexes** (Recommended to add):
- Unique index on `employeeId`
- Unique index on `email`
- Index on `assignedOutletId`
- Index on `position`
- Index on `isActive`
- Compound index on `firstName` and `lastName`

To create indexes, run in MongoDB:
```javascript
db.Staff.createIndex({ "employeeId": 1 }, { unique: true })
db.Staff.createIndex({ "email": 1 }, { unique: true })
db.Staff.createIndex({ "assignedOutletId": 1 })
db.Staff.createIndex({ "position": 1 })
db.Staff.createIndex({ "isActive": 1 })
db.Staff.createIndex({ "firstName": 1, "lastName": 1 })
```

---

## Next Steps & Enhancements

Consider implementing these additional features:

1. **Attendance Module**
   - Clock in/out functionality
   - Daily attendance records
   - Attendance reports

2. **Leave Management Module**
   - Leave application system
   - Leave approval workflow
   - Leave history tracking

3. **Shift Management**
   - Shift scheduling
   - Shift swapping
   - Shift roster generation

4. **Payroll Integration**
   - Salary calculation
   - Payslip generation
   - Deductions and allowances

5. **Performance Management**
   - Performance review system
   - Goal setting and tracking
   - Feedback management

6. **Training & Certifications**
   - Training records
   - Certification tracking
   - Skill development plans

7. **Staff Portal**
   - Self-service portal for staff
   - View personal information
   - Apply for leaves
   - View payslips

---

## Testing

To test the implementation:

1. **Build the project**: Run the build task in VS Code
2. **Start the API**: Run the Azure Functions locally
3. **Authenticate as Admin**: Use the login endpoint to get an admin JWT token
4. **Test Endpoints**: Use Postman, Thunder Client, or the Swagger UI to test each endpoint

### Sample Test Sequence:
1. Create a staff member
2. Get all staff members
3. Get staff by ID
4. Update staff details
5. Update salary
6. Update performance rating
7. Search for staff
8. Get statistics
9. Deactivate staff
10. Activate staff

---

## Support & Maintenance

For issues or questions:
1. Check audit logs for operation history
2. Verify MongoDB connection and collection
3. Ensure admin authentication is working
4. Check application logs for errors

---

## Frontend Implementation

### Files Created

1. **[models/staff.model.ts](frontend/src/app/models/staff.model.ts)** - TypeScript interfaces and constants
   - Staff interface with complete type definitions
   - Helper interfaces (StaffAddress, EmergencyContact, BankDetails, etc.)
   - Constants for dropdowns (positions, departments, employment types, etc.)

2. **[services/staff.service.ts](frontend/src/app/services/staff.service.ts)** - Service for API calls
   - All CRUD operations
   - Search and filter methods
   - Statistics retrieval
   - Salary, performance, and leave balance updates

3. **[components/staff-management/](frontend/src/app/components/staff-management/)** - Main component
   - **staff-management.component.ts** - Component logic with full CRUD functionality
   - **staff-management.component.html** - Complete UI with modal forms
   - **staff-management.component.scss** - Responsive styling

### Features Implemented

#### 1. Staff List View
- **Statistics Dashboard**: Display total staff, active/inactive counts, employment type breakdown
- **Search Functionality**: Search by name, email, or employee ID
- **Filters**: Active/Inactive status filter
- **Data Table**: Displays all staff with key information
- **Action Buttons**: View, Edit, Activate/Deactivate, Delete
- **Export to CSV**: Download staff data as CSV file

#### 2. Staff Details Modal (Tabbed Interface)
The modal has multiple tabs for organized data entry:

**Basic Info Tab:**
- Personal details (name, email, phone, DOB, gender)
- Address information
- Emergency contact details

**Employment Tab:**
- Position and department
- Employment type (Full-Time, Part-Time, Contract)
- Hire date and probation period
- Assigned outlet
- Active status

**Compensation Tab:**
- Salary amount and type (Monthly/Daily/Hourly)
- Bank account details (account number, IFSC, bank name, etc.)

**Schedule Tab:**
- Shift timings (start and end time)
- Working days selection (checkbox for each day)
- Leave balances (Annual, Sick, Casual)

**Performance Tab:**
- Performance rating (0-5 scale)
- Notes and comments

#### 3. Modal Modes
- **Create Mode**: Add new staff member with validation
- **Edit Mode**: Update existing staff member
- **View Mode**: Read-only view of staff details

#### 4. Validation
- Required field validation
- Email format validation
- Duplicate employee ID and email prevention
- Rating range validation (0-5)
- Inline error messages

#### 5. User Experience
- Responsive design for mobile, tablet, and desktop
- Loading spinners for async operations
- Success and error messages
- Confirmation dialogs for destructive actions
- Smooth transitions and hover effects
- Color-coded badges for status and employment type

### Navigation

The Staff Management module is accessible from the admin panel:
- **Route**: `/admin/staff`
- **Navigation**: Admin Panel → Staff (in main navigation)
- **Access**: Admin users only (protected by adminGuard)

### UI Components

#### Statistics Cards
```
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│  Total Staff    │ │  Active Staff   │ │  Full-Time      │ │  Part-Time      │
│      25         │ │      23         │ │      18         │ │      4          │
└─────────────────┘ └─────────────────┘ └─────────────────┘ └─────────────────┘
```

#### Search and Filters
```
┌────────────────────────────────────────────────────────────────────┐
│ [Search by name, email, or ID...] [Search] [Clear]  ☐ Active Only │
└────────────────────────────────────────────────────────────────────┘
```

#### Staff Table
```
┌──────────┬─────────────┬──────────┬────────────┬────────────┬──────────┐
│ Emp ID   │ Name        │ Position │ Department │ Status     │ Actions  │
├──────────┼─────────────┼──────────┼────────────┼────────────┼──────────┤
│ EMP001   │ John Doe    │ Barista  │ Service    │ [Active]   │ 👁️ ✏️ ❌ │
│ EMP002   │ Jane Smith  │ Manager  │ Management │ [Active]   │ 👁️ ✏️ ❌ │
└──────────┴─────────────┴──────────┴────────────┴────────────┴──────────┘
```

### Styling Features

- **Modern Card Design**: Statistics cards with gradient icons
- **Responsive Grid**: Adapts to screen size
- **Color-Coded Badges**:
  - Green for Active status
  - Red for Inactive status
  - Blue for Full-Time
  - Yellow for Part-Time
  - Gray for Contract
- **Hover Effects**: Table rows and buttons
- **Modal Animations**: Smooth fade-in/fade-out
- **Tab Navigation**: Clear active state indicators
- **Mobile-Friendly**: Collapsible navigation, scrollable tables

### Color Scheme

```scss
Primary: #007bff (Blue)
Success: #155724 (Green)
Warning: #856404 (Yellow/Orange)
Danger: #721c24 (Red)
Secondary: #6c757d (Gray)
Background: #f8f9fa (Light Gray)
```

### Responsive Breakpoints

- **Desktop**: Full grid layout (1400px max-width)
- **Tablet**: Adjusted grid (768px - 1399px)
- **Mobile**: Single column, scrollable table (< 768px)

---

## Usage Guide

### Adding a New Staff Member

1. Click "Add Staff Member" button in the header
2. Fill in the required fields in the Basic Info tab:
   - Employee ID (unique)
   - First Name
   - Last Name
   - Email (unique)
   - Phone Number
   - Position
3. Navigate through tabs to add additional information:
   - Employment details (tab 2)
   - Compensation (tab 3)
   - Work schedule (tab 4)
   - Performance notes (tab 5)
4. Click "Create" to save

### Editing Staff Information

1. Click the edit icon (✏️) on the staff member row
2. Modify the desired fields in any tab
3. Click "Update" to save changes

### Viewing Staff Details

1. Click the view icon (👁️) on the staff member row
2. Navigate through tabs to see all information
3. Click "Close" when done

### Deactivating/Activating Staff

1. Click the status toggle icon (🚫/✓) on the staff member row
2. Confirm the action in the dialog
3. Staff status will be updated immediately

### Deleting Staff (Permanent)

1. Click the delete icon (🗑️) on the staff member row
2. Confirm the permanent deletion in the dialog
3. **Warning**: This action cannot be undone

### Searching Staff

1. Enter search term in the search bar (name, email, or employee ID)
2. Press Enter or click "Search" button
3. Click "Clear" to reset search

### Exporting Data

1. Click "Export CSV" button in the header
2. CSV file will download with current staff data
3. File name: `staff-report.csv`

---

## Integration with Backend

The frontend communicates with the backend API through the `StaffService`:

```typescript
// Example: Get all staff
this.staffService.getAllStaff(true).subscribe(staff => {
  console.log(staff);
});

// Example: Create staff
this.staffService.createStaff(staffData).subscribe(
  created => console.log('Created:', created),
  error => console.error('Error:', error)
);
```

All API calls include:
- Automatic JWT token authentication (via interceptor)
- Error handling
- Type-safe responses
- Observable-based async operations

---

## Testing the UI

1. **Start the Frontend**:
   ```bash
   cd frontend
   npm install
   npm start
   ```

2. **Login as Admin**:
   - Navigate to `/login`
   - Enter admin credentials
   - You'll be redirected to `/admin/dashboard`

3. **Access Staff Management**:
   - Click on "Staff" (👥) in the navigation
   - Or navigate directly to `/admin/staff`

4. **Test CRUD Operations**:
   - ✅ Create a new staff member
   - ✅ View staff details
   - ✅ Edit staff information
   - ✅ Search for staff
   - ✅ Toggle active status
   - ✅ Export to CSV
   - ✅ Delete staff (use with caution)

---

## Troubleshooting

### Common Issues

**1. Staff list not loading**
- Check if backend API is running
- Verify JWT token is valid
- Check browser console for errors
- Ensure admin authentication is working

**2. Cannot create staff**
- Verify all required fields are filled
- Check for duplicate Employee ID or Email
- Ensure backend validation passes
- Check network tab for API response

**3. Modal not displaying correctly**
- Clear browser cache
- Check for CSS conflicts
- Verify Angular Material or Bootstrap imports

**4. Search not working**
- Ensure search term is at least 1 character
- Check if API endpoint `/staff/search` is accessible
- Verify query parameter encoding

### Browser Console Commands

```javascript
// Check current user
localStorage.getItem('currentUser');

// Check auth token
localStorage.getItem('authToken');

// Check selected outlet
localStorage.getItem('selectedOutletId');
```

---

*Generated: January 14, 2026*
*Module Version: 1.0*
*Backend + Frontend Implementation Complete*
