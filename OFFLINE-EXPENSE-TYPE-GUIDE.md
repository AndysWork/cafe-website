# Offline Expense Type System - User Guide

## Overview
The OfflineExpenseType system standardizes expense categories across manual entry and Excel uploads, ensuring data consistency and preventing typos.

## Features
- **21 Predefined Expense Types** based on your historical data
- **Database-driven** expense type selection
- **Validation** for Excel uploads (rejects invalid types)
- **Easy initialization** with one-click setup
- **Admin-only** management for security

## Expense Types Available
1. Milk
2. Cup
3. Cigarete
4. Biscuit
5. Rent
6. Grocerry
7. Misc
8. Tea
9. Water
10. Chicken
11. Cold Drinks
12. Packaging
13. Utensils
14. Kitkat/Oreo
15. Egg
16. Veggie
17. Sugar
18. Paneer
19. Bread
20. Fund (Save)
21. Ice cream

## First-Time Setup

### 1. Initialize Expense Types
When you first access the Expenses page and no expense types are loaded:

1. Log in as **Admin**
2. Navigate to **Admin → Expenses**
3. Click the **🔧 Initialize Types** button (appears when no types exist)
4. Confirm the initialization
5. Wait for success message
6. The dropdown will now show all 21 expense types

### 2. Verify Initialization
After initialization:
- The expense type dropdown should show all 21 categories
- The initialize button should disappear
- You can start adding expenses immediately

## Using Manual Expense Entry

1. Click **➕ Add Expense** button
2. Fill in the form:
   - **Date**: Select expense date
   - **Expense Type**: Choose from dropdown (all 21 types available)
   - **Amount**: Enter expense amount
   - **Vendor**: (Optional) Vendor name
   - **Description**: Brief description
   - **Payment Method**: Cash/Online/Card
   - **Invoice Number**: (Optional) Reference number
   - **Notes**: (Optional) Additional notes
3. Click **Add Expense**

## Using Excel Upload

### Step 1: Download Template
1. Click **📥 Download Template** button
2. The CSV file will contain:
   - Comment with all valid expense types
   - Format instructions
   - 4 example rows

### Step 2: Prepare Your Data
Open the template and:
- **Keep the header row** (Date, ExpenseType, Amount, Vendor, Description, InvoiceNumber, PaymentMethod, Notes)
- **Delete example rows** and add your data
- **Use exact expense type names** from the comment (case-sensitive)
- **Format dates** as YYYY-MM-DD (e.g., 2024-01-15)
- **Enter amounts** as numbers (e.g., 500, not ₹500)

Example valid row:
```csv
2024-01-15,Milk,500,Local Vendor,Daily milk supply,INV-001,Cash,Morning purchase
```

### Step 3: Upload File
1. Click **📂 Choose File**
2. Select your prepared CSV file
3. Click **📤 Upload Expenses**
4. Wait for processing

### Step 4: Review Results
After upload, you'll see:
- **Success**: Number of records processed
- **Warning** (if any): Invalid expense types that were skipped
- **Total Amount**: Sum of all uploaded expenses

## Validation Rules

### Excel Upload Validation
The system will **automatically skip** rows with:
- Invalid expense types (not in the 21 predefined list)
- Missing required fields (Date, ExpenseType, Amount)
- Invalid date formats
- Invalid amount formats (non-numeric)

### Warning Messages
If invalid expense types are detected:
```
Upload completed with warnings!

5 records processed successfully.
2 records skipped due to invalid expense types: Salary, Utilities

Valid expense types are: Milk, Cup, Cigarete, Biscuit, Rent, Grocerry, Misc, Tea, Water, Chicken, Cold Drinks, Packaging, Utensils, Kitkat/Oreo, Egg, Veggie, Sugar, Paneer, Bread, Fund (Save), Ice cream
```

## Common Issues & Solutions

### Issue: Expense type dropdown is empty
**Solution**: Click the "🔧 Initialize Types" button to populate the database

### Issue: Excel upload rejects all rows
**Solution**: 
1. Download a fresh template
2. Check that expense types exactly match the valid list (case-sensitive)
3. Ensure dates are in YYYY-MM-DD format
4. Ensure amounts are numeric only

### Issue: "Grocerry" vs "Grocery" spelling
**Solution**: The system uses "Grocerry" (with double 'r') to match your historical data. Use this exact spelling.

### Issue: Some records skipped during upload
**Solution**: 
1. Check the warning message for invalid expense types
2. Fix the invalid types in your CSV file
3. Re-upload the corrected file

### Issue: Can't see initialize button
**Solution**: 
1. Make sure you're logged in as Admin
2. If expense types already exist, the button won't appear
3. Check browser console for any errors

## API Endpoints (Admin Only)

All endpoints require admin authentication:

- `GET /api/offlineexpensetypes` - Get all expense types
- `GET /api/offlineexpensetypes/active` - Get active expense types only
- `POST /api/offlineexpensetypes` - Create new expense type
- `PUT /api/offlineexpensetypes/{id}` - Update expense type
- `DELETE /api/offlineexpensetypes/{id}` - Delete expense type
- `POST /api/offlineexpensetypes/initialize` - Initialize default 21 types

## Benefits

1. **Data Consistency**: No more typos or variations (e.g., "Grocery" vs "Grocerry")
2. **Easy Reporting**: Group expenses by standardized categories
3. **Validation**: Prevents invalid entries during Excel uploads
4. **Flexibility**: Admin can add new expense types as needed
5. **Historical Accuracy**: Based on your actual expense data (21 unique types from 480+ entries)

## Technical Details

### Database Collection
- **Name**: `OfflineExpenseTypes`
- **Fields**: 
  - `Id` (ObjectId)
  - `ExpenseType` (string)
  - `IsActive` (boolean)
  - `CreatedAt` (DateTime)
  - `UpdatedAt` (DateTime)

### Backend
- **Model**: `api/Models/OfflineExpenseType.cs`
- **Service**: `api/Services/MongoService.cs`
- **API**: `api/Functions/OfflineExpenseTypeFunction.cs`

### Frontend
- **Service**: `frontend/src/app/services/offline-expense-type.service.ts`
- **Component**: `frontend/src/app/components/admin-expenses/`

## Future Enhancements
- Add new expense types through admin UI
- Deactivate/reactivate expense types
- Expense type usage analytics
- Bulk expense type management
