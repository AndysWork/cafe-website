# Bonus Configuration & Staff Performance UI Documentation

## Overview
The frontend UI for the bonus configuration and staff performance tracking system has been successfully implemented. This system allows administrators to:

1. **Configure Bonus Rules** - Set up flexible performance-based bonuses and deductions
2. **Track Staff Performance** - Record and monitor staff performance metrics
3. **Calculate Bonuses** - Automatically calculate bonuses based on configured rules

---

## Features Implemented

### 1. Bonus Configuration Component (`/admin/bonus-configuration`)

**Purpose:** Configure bonus and deduction rules for different staff positions.

**Key Features:**
- ✅ Create, edit, view, and delete bonus configurations
- ✅ Support for 6 rule types:
  - **Overtime Hours** - Bonus for working beyond scheduled hours
  - **Undertime Hours** - Deduction for working less than scheduled hours
  - **Snacks Preparation** - Bonus for preparing extra snacks
  - **Bad Orders** - Deduction based on bad order count
  - **Good Ratings** - Bonus based on customer ratings
  - **Refund Deduction** - Deduction for orders requiring refunds
- ✅ Multiple calculation types per rule:
  - Per Unit
  - Per Hour
  - Percentage
  - Fixed Amount
- ✅ Position-based applicability (Chef, Waiter, Cashier, etc.)
- ✅ Configurable thresholds and maximum amounts
- ✅ Monthly, Quarterly, or Yearly calculation periods
- ✅ Active/Inactive status toggle
- ✅ Search and filter functionality

**Usage:**
1. Navigate to `/admin/bonus-configuration`
2. Click "Create Configuration" button
3. Fill in configuration details:
   - Configuration name
   - Select applicable positions
   - Set calculation period
   - Add bonus/deduction rules
4. Save configuration

**Example Configuration:**
```json
{
  "configurationName": "Kitchen Staff Bonus - January 2026",
  "applicablePositions": ["Chef", "Assistant Chef"],
  "calculationPeriod": "Monthly",
  "rules": [
    {
      "ruleType": "OvertimeHours",
      "isBonus": true,
      "calculationType": "PerHour",
      "rateAmount": 100,
      "threshold": 5
    },
    {
      "ruleType": "BadOrders",
      "isBonus": false,
      "calculationType": "PerUnit",
      "rateAmount": 50
    }
  ]
}
```

---

### 2. Staff Performance Component (`/admin/staff-performance`)

**Purpose:** Track staff performance metrics and calculate bonuses.

**Key Features:**
- ✅ Record performance metrics:
  - Scheduled hours vs. Actual hours (auto-calculates overtime/undertime)
  - Snacks prepared
  - Bad orders count
  - Good ratings count
  - Missing item refunds (₹)
- ✅ View modes:
  - **All Staff** - See all staff performance for selected period
  - **Individual Staff** - View specific staff member's history
- ✅ Period filtering (last 12 months)
- ✅ Detailed bonus breakdown showing:
  - Each rule applied
  - Metric values
  - Calculated amounts
  - Total bonuses and deductions
  - Net bonus amount
- ✅ Manual bonus recalculation
- ✅ Export to CSV functionality
- ✅ Visual indicators for positive/negative amounts

**Usage:**
1. Navigate to `/admin/staff-performance`
2. Click "Add Performance Record" button
3. Fill in performance data:
   - Select staff member
   - Select period (month-year)
   - Enter scheduled hours and actual hours
   - Enter performance metrics
   - Add optional notes
4. Click "Create" - bonus will be automatically calculated
5. View detailed breakdown by clicking the "View Details" button

---

## Navigation

The new components have been added to the admin navigation menu:

**Staff Menu (Dropdown):**
- 👔 Staff Management - Manage staff members
- ⚙️ Bonus Configuration - Configure bonus rules
- 📊 Staff Performance - Track performance & bonuses

---

## API Endpoints

### Bonus Configuration API
- `GET /api/bonusconfigurations` - Get all configurations
- `GET /api/bonusconfigurations/{id}` - Get configuration by ID
- `GET /api/bonusconfigurations/staff/{staffId}` - Get configurations for staff
- `POST /api/bonusconfigurations` - Create new configuration
- `PUT /api/bonusconfigurations/{id}` - Update configuration
- `DELETE /api/bonusconfigurations/{id}` - Delete configuration
- `PATCH /api/bonusconfigurations/{id}/toggle-active` - Toggle active status

### Staff Performance API
- `GET /api/staffperformance/staff/{staffId}` - Get staff performance records
- `GET /api/staffperformance/outlet` - Get all outlet performance records
- `POST /api/staffperformance` - Create/update performance record
- `POST /api/staffperformance/calculate-bonus` - Calculate bonus for period

---

## Services Created

### 1. BonusConfigurationService
**Location:** `frontend/src/app/services/bonus-configuration.service.ts`

**Methods:**
- `getBonusConfigurations()` - Get all configurations
- `getBonusConfigurationById(id)` - Get single configuration
- `getBonusConfigurationsForStaff(staffId)` - Get applicable configurations
- `createBonusConfiguration(request)` - Create new configuration
- `updateBonusConfiguration(id, request)` - Update configuration
- `deleteBonusConfiguration(id)` - Delete configuration
- `toggleActiveStatus(id)` - Toggle active status

### 2. StaffPerformanceService
**Location:** `frontend/src/app/services/staff-performance.service.ts`

**Methods:**
- `getStaffPerformanceRecords(staffId, period?)` - Get records for staff
- `getOutletPerformanceRecords(period?)` - Get all outlet records
- `upsertStaffPerformanceRecord(request)` - Create/update record
- `calculateStaffBonus(staffId, period)` - Trigger bonus calculation

---

## Components Structure

### Files Created:

**Bonus Configuration:**
- `components/bonus-configuration/bonus-configuration.component.ts` (410 lines)
- `components/bonus-configuration/bonus-configuration.component.html` (300+ lines)
- `components/bonus-configuration/bonus-configuration.component.scss` (450+ lines)

**Staff Performance:**
- `components/staff-performance/staff-performance.component.ts` (300+ lines)
- `components/staff-performance/staff-performance.component.html` (350+ lines)
- `components/staff-performance/staff-performance.component.scss` (500+ lines)

**Services:**
- `services/bonus-configuration.service.ts` (100 lines)
- `services/staff-performance.service.ts` (90 lines)

---

## Styling & UI

Both components feature:
- ✅ Modern card-based layouts
- ✅ Purple gradient theme (#667eea to #764ba2)
- ✅ Responsive design
- ✅ Color-coded indicators:
  - Green for bonuses/positive amounts
  - Red for deductions/negative amounts
  - Yellow for warnings
- ✅ Interactive modals for create/edit/view operations
- ✅ Real-time filtering and search
- ✅ Loading states and error handling
- ✅ Success/error message alerts

---

## Calculation Logic

The bonus calculation follows this process:

1. **Fetch applicable configurations** - Get active configurations for staff position and outlet
2. **Apply each rule:**
   - Check if threshold is met
   - Calculate amount based on calculation type:
     - **PerUnit**: `metricValue × rateAmount`
     - **PerHour**: `hours × rateAmount`
     - **Percentage**: `baseValue × (percentage / 100)`
     - **Fixed**: `rateAmount`
   - Apply maximum amount cap if configured
3. **Sum bonuses and deductions separately**
4. **Calculate net bonus amount** = Total Bonuses - Total Deductions

---

## Example Workflow

### Scenario: Configure and Calculate Chef Bonus

1. **Configure Bonus Rules:**
   - Go to Bonus Configuration
   - Create "January 2026 Chef Bonus"
   - Add rules:
     - Overtime: ₹100/hour (threshold: 5 hours)
     - Bad Orders: -₹50/order
     - Good Ratings: ₹25/rating

2. **Record Performance:**
   - Go to Staff Performance
   - Select chef: "Rajesh Kumar"
   - Period: "2026-01"
   - Scheduled: 160 hours
   - Actual: 175 hours (15 overtime)
   - Bad Orders: 2
   - Good Ratings: 30

3. **Automatic Calculation:**
   - Overtime Bonus: 15 × ₹100 = ₹1,500
   - Bad Orders Deduction: 2 × ₹50 = -₹100
   - Good Ratings Bonus: 30 × ₹25 = ₹750
   - **Net Bonus: ₹2,150**

4. **View Breakdown:**
   - Click "View Details" to see complete calculation
   - Export report to CSV if needed

---

## Testing Checklist

- [ ] Create bonus configuration
- [ ] Add multiple rules to configuration
- [ ] Toggle configuration active/inactive
- [ ] Delete configuration
- [ ] Add staff performance record
- [ ] Edit existing performance record
- [ ] View detailed breakdown
- [ ] Filter by staff member
- [ ] Filter by period
- [ ] Export to CSV
- [ ] Verify bonus calculations are correct
- [ ] Test with different rule types
- [ ] Test threshold and max amount limits

---

## Notes

- All monetary values are in Indian Rupees (₹)
- Period format: YYYY-MM (e.g., "2026-01")
- Overtime and undertime are automatically calculated from scheduled vs. actual hours
- Multiple configurations can be active simultaneously for the same position
- Bonus calculations are additive - all applicable rules are summed

---

## Support

For issues or questions:
1. Check browser console for error messages
2. Verify API endpoints are accessible
3. Ensure MongoDB collections are properly initialized
4. Check that staff member has assigned position and outlet
