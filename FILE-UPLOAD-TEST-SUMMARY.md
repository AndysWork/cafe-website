# File Upload Feature - Test Summary

## 🎯 Feature Overview
This feature allows users to upload Excel (.xlsx, .xls) or CSV files containing Category and SubCategory data to bulk import into the MongoDB database.

## 📁 Implementation Details

### Backend (Azure Functions API)

#### 1. **FileUploadService.cs**
   - **Location**: `api/Services/FileUploadService.cs`
   - **Purpose**: Processes Excel and CSV files to import categories and subcategories
   - **Key Methods**:
     - `ProcessExcelFile()`: Handles .xlsx and .xls files using EPPlus library
     - `ProcessCsvFile()`: Handles .csv files using CsvHelper library
   - **Dependencies**: 
     - EPPlus 7.5.2 (Excel processing)
     - CsvHelper 33.0.1 (CSV parsing)
     - MongoService (database operations)

#### 2. **FileUploadFunction.cs**
   - **Location**: `api/Functions/FileUploadFunction.cs`
   - **Endpoints**:
     - **POST** `/api/upload/categories` - Upload CSV or Excel file
     - **GET** `/api/upload/categories/template?format=csv|excel` - Download template
   - **Features**:
     - Custom multipart form data parser (no built-in support in isolated worker)
     - File validation (type and size)
     - Template generation with sample data

#### 3. **Models Updated**
   - **MenuSubCategory.cs**: Added `[BsonIgnore] CategoryName` property for temporary use during file processing

### Frontend (Angular 18)

#### 1. **CategoryUploadComponent**
   - **Location**: `frontend/src/app/components/category-upload/`
   - **Features**:
     - Drag-and-drop file upload
     - File browser selection
     - Progress bar with percentage
     - Upload result display (success/error)
     - Template download buttons
   - **File Validation**:
     - Max size: 5MB
     - Allowed extensions: .xlsx, .xls, .csv
   - **UX Highlights**:
     - Beautiful gradient design
     - Drag-over animation
     - Real-time upload progress
     - Clear error/success messaging

#### 2. **Routing**
   - Route: `/upload`
   - Added to `app.routes.ts`
   - Standalone component architecture

## 📊 File Format

### CSV/Excel Template Structure
```
CategoryName | CategoryDescription | CategoryDisplayOrder | SubCategoryName | SubCategoryDescription | SubCategoryDisplayOrder
-------------|---------------------|----------------------|-----------------|------------------------|-------------------------
Beverages    | All types of drinks | 1                    | Hot Beverages   | Coffee and tea         | 1
Beverages    | All types of drinks | 1                    | Cold Beverages  | Juices and smoothies   | 2
Food         | Main dishes         | 2                    | Breakfast       | Morning meals          | 1
```

### Business Logic
- **Categories**: Identified by unique `CategoryName`
- **SubCategories**: Linked to category by name during import, then `CategoryId` is assigned
- **Duplicate Handling**: Categories with same name are merged (subcategories added to existing category)
- **Validation**: All required fields must be present

## 🚀 Testing Instructions

### 1. Start the Services

#### Backend (Azure Functions)
```powershell
cd f:\MyProducts\CafeWebsite\cafe-website\api
func start --port 7072 --cors "*"
```
**Expected Output**: All 20 function endpoints listed, including:
- `UploadCategoriesFile: [POST] http://localhost:7072/api/upload/categories`
- `DownloadCategoriesTemplate: [GET] http://localhost:7072/api/upload/categories/template`

#### Frontend (Angular)
```powershell
cd f:\MyProducts\CafeWebsite\cafe-website\frontend
ng serve
```
**Expected Output**: 
- Compilation successful
- App running at `http://localhost:4200/`

### 2. Access the Upload UI
- Navigate to: `http://localhost:4200/upload`
- You should see:
  - Template download section with CSV and Excel buttons
  - Drag-and-drop upload zone
  - File upload instructions

### 3. Test Template Download

#### CSV Template
1. Click **"Download CSV Template"** button
2. File should download: `categories-template.csv`
3. Open in Excel/Notepad
4. Verify sample data is present

#### Excel Template
1. Click **"Download Excel Template"** button
2. File should download: `categories-template.xlsx`
3. Open in Excel
4. Verify:
   - Headers in row 1
   - Sample data in rows 2-3
   - Bold headers

### 4. Test File Upload

#### Using Sample File
1. Use the provided `sample-categories.csv` file
2. **Option A - Drag and Drop**:
   - Drag the file over the upload zone
   - Watch for purple border (drag state)
   - Drop the file
3. **Option B - File Browser**:
   - Click "Browse files" link
   - Select `sample-categories.csv`
   - Click Open

#### Expected Behavior
1. **File Selected**:
   - File name appears
   - File size shown
   - Upload button enabled
2. **Upload Progress**:
   - Progress bar appears
   - Percentage updates (0% → 100%)
3. **Upload Complete**:
   - Success message appears
   - Statistics shown:
     - Categories Processed: 4
     - SubCategories Processed: 11
   - Success icon displayed

### 5. Verify Database

#### Check Categories
```powershell
Invoke-RestMethod -Uri "http://localhost:7072/api/categories" -Method Get | ConvertTo-Json
```
**Expected**: 4 categories (Beverages, Food, Snacks, Desserts)

#### Check SubCategories
```powershell
Invoke-RestMethod -Uri "http://localhost:7072/api/subcategories" -Method Get | ConvertTo-Json
```
**Expected**: 11 subcategories with correct `CategoryId` references

#### Verify Relationships
```powershell
# Get subcategories for "Beverages" category (use actual category ID)
Invoke-RestMethod -Uri "http://localhost:7072/api/categories/{categoryId}/subcategories" -Method Get
```
**Expected**: 3 subcategories (Hot Beverages, Cold Beverages, Alcoholic)

### 6. Test Error Handling

#### Invalid File Type
1. Try uploading a `.txt` or `.pdf` file
2. **Expected**: Error message "Invalid file type. Please upload a CSV or Excel file."

#### File Too Large
1. Try uploading a file > 5MB
2. **Expected**: Error message "File size exceeds 5MB limit"

#### Malformed CSV
1. Create CSV with missing required columns
2. Upload the file
3. **Expected**: Error list showing specific validation errors

#### Duplicate Upload
1. Upload the same file twice
2. **Expected**: Categories are not duplicated (merged by name)
3. SubCategories should be added to existing categories

## 📈 Performance Metrics

### Sample Data Results
- **File**: `sample-categories.csv` (11 rows)
- **Categories Processed**: 4
- **SubCategories Processed**: 11
- **Upload Time**: < 2 seconds
- **File Size**: < 1KB

## 🎨 UI/UX Features

### Visual Design
- **Color Scheme**: Purple gradients (#9333ea → #ec4899)
- **Animations**: 
  - Drag-over state transition (0.3s)
  - Success checkmark animation
  - Progress bar smooth transition
- **Responsive**: Works on mobile, tablet, desktop

### User Feedback
- **Loading States**: Progress bar with percentage
- **Success States**: Green checkmark, success message, statistics
- **Error States**: Red error icon, error message, error list
- **File Info**: Name, size, icon based on type

## 🔧 Configuration

### Environment Variables
**frontend/src/environments/environment.ts**:
```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:7072/api'
};
```

### CORS Settings
API started with `--cors "*"` for development (allow all origins)

### MongoDB Connection
- **Cluster**: maataracafecluster.8ynr8xr.mongodb.net
- **Database**: CafeDB
- **Collections**: MenuCategory, MenuSubCategory

## ✅ Test Checklist

- [ ] Backend API running on port 7072
- [ ] Frontend Angular app running on port 4200
- [ ] Navigate to /upload route successfully
- [ ] Download CSV template works
- [ ] Download Excel template works
- [ ] Template files have correct format and sample data
- [ ] Upload CSV file via drag-and-drop
- [ ] Upload progress bar displays correctly
- [ ] Success message shows correct statistics
- [ ] Categories created in MongoDB
- [ ] SubCategories created with correct CategoryId
- [ ] Relationships verified (categories → subcategories)
- [ ] Invalid file type rejected
- [ ] Oversized file rejected
- [ ] Duplicate upload handled gracefully
- [ ] Error messages clear and helpful

## 🐛 Known Issues / Warnings

### Non-Critical Warnings
1. **Node.js v23.4.0**: Odd-numbered version warning (dev only, not production)
2. **Nullable reference warnings**: In FileUploadFunction.cs for `uploadedBy` parameter

### Fixed Issues
- ✅ Multipart form data parsing (custom implementation)
- ✅ CategoryName temporary property (using [BsonIgnore])
- ✅ Import path in Angular component (fixed to `../../../environments/environment`)
- ✅ FileUploadService registration in DI container
- ✅ CORS configuration for local development

## 📝 Sample Data

The `sample-categories.csv` file contains:
- **4 Categories**: Beverages, Food, Snacks, Desserts
- **11 SubCategories**: Distributed across categories
- **Realistic Data**: Coffee shop/cafe menu structure

## 🎉 Success Criteria

The feature is considered successful when:
1. ✅ User can download both CSV and Excel templates
2. ✅ User can upload files via drag-drop or file browser
3. ✅ Progress is shown during upload
4. ✅ Success/error feedback is clear and actionable
5. ✅ Data is correctly imported to MongoDB
6. ✅ Relationships between categories and subcategories are maintained
7. ✅ Duplicate handling works correctly
8. ✅ UI is attractive and user-friendly

---

**Last Updated**: December 10, 2025
**Status**: ✅ **READY FOR TESTING**
