# Online Sales Tracker - Zomato & Swiggy Integration

## Platform Identification

All online sales data is **clearly identified** by the `platform` field in the database:
- Each record has a `Platform` property set to either **"Zomato"** or **"Swiggy"**
- This is stored in MongoDB with an index for fast filtering
- The platform is prominently displayed in the UI with color-coded badges

## Excel Format Differences

### Zomato Excel Format (21 columns)
The system expects Zomato reports with the following columns:
1. **ZomatoOrderId** - Order ID
2. **CustomerName** - Customer name (optional)
3. **OrderAt** - Order date (format: 01-Nov-25, dd-MM-yyyy, or dd/MM/yyyy)
4. **Distance** - Delivery distance in km
5. **OrderedItems** - Items (format: "1 x Chicken Red Sauce Pasta, 1 x Bread Egg Toast")
6. **Instructions** - Customer instructions
7. **DiscountCoupon** - Coupon code applied
8. **BillSubTotal** - Bill amount before charges
9. **PackagingCharges** - Packaging fees
10. **DiscountAmount** - Discount applied
11. **TotalCommissionable** - Commissionable amount
12. **Payout** - Amount paid out to restaurant
13. **ZomatoDeduction** - Zomato's commission/deduction
14. **Investment** - Investment amount
15. **MiscCharges** - Miscellaneous charges
16. **Rating** - Customer rating (0-5)
17. **Review** - Customer review text
18. **KPT** - KPT metric
19. **RWT** - RWT metric
20. **OrderMarking** - Order marking/status
21. **Complain** - Customer complaints

### Swiggy Excel Format (11 columns)
The system expects Swiggy reports with the following columns:
1. **SwiggyOrderId** - Order ID
2. **CustomerName** - Customer name (optional)
3. **OrderDate** - Order date (supports dd-MMM-yy, dd-MM-yyyy, dd/MM/yyyy, yyyy-MM-dd)
4. **Distance** - Delivery distance
5. **OrderedItems** - Items (same format: "1 x Item Name, 2 x Item Name")
6. **Instructions** - Customer instructions
7. **BillTotal** - Total bill amount
8. **Payout** - Amount paid to restaurant
9. **SwiggyDeduction** - Swiggy's commission
10. **Rating** - Customer rating
11. **Review** - Customer review

**Note:** Swiggy format is simplified with fewer fields. The parser sets default values for missing fields:
- PackagingCharges = 0
- DiscountAmount = 0
- Investment = 0
- MiscCharges = 0
- KPT, RWT, OrderMarking, Complain = null

## Database Indexes

Optimized indexes ensure fast queries:
1. **platform_1** - Filter by Zomato or Swiggy
2. **orderAt_-1** - Sort by date (newest first)
3. **platform_1_orderAt_-1** - Compound index for filtered date queries
4. **orderId_1** - Quick order lookups

## API Endpoints

All endpoints support platform filtering:

- `GET /api/online-sales?platform=Zomato` - Get only Zomato orders
- `GET /api/online-sales?platform=Swiggy` - Get only Swiggy orders
- `GET /api/online-sales/date-range?platform=Zomato&startDate=2024-01-01&endDate=2024-12-31`
- `GET /api/online-sales/daily-income?startDate=2024-01-01&endDate=2024-12-31` - Groups by platform automatically

## Upload Process

The upload endpoint automatically routes to the correct parser:

```
POST /api/upload/online-sales
FormData: {
  file: <excel-file>,
  platform: "Zomato" or "Swiggy"
}
```

### Upload Flow:
1. User selects Zomato or Swiggy in the UI
2. Uploads Excel file
3. Backend detects platform and routes to appropriate parser:
   - `ProcessZomatoExcel()` - Handles 21-column Zomato format
   - `ProcessSwiggyExcel()` - Handles 11-column Swiggy format
4. Parser validates format and extracts data
5. Items are automatically matched with menu items from CafeMenu collection
6. Data is saved with `Platform` field set to "Zomato" or "Swiggy"

## UI Features

### Platform Selection
- Radio buttons to switch between Zomato (🍔 red) and Swiggy (🍕 orange)
- Color-coded platform badges in tables
- Separate upload forms for each platform

### Data Display
- Daily Income table shows platform column with colored badges
- Order Details table includes platform identification
- Summary stats are calculated per platform
- Export to CSV includes platform column

## Item Matching

Both platforms use the same item parsing logic:
- Parses "1 x Item Name, 2 x Another Item" format
- Automatically searches CafeMenu collection for matching items
- Matches by exact name first, then by contains
- Stores matched `menuItemId` for inventory tracking

## Customizing Swiggy Format

To adjust the Swiggy parser for your actual Excel format:

1. Open `api/Services/FileUploadService.cs`
2. Find the `ProcessSwiggyExcel()` method
3. Update column mappings:
   ```csharp
   var orderId = worksheet.Cells[row, 1].Text.Trim(); // Column 1
   var customerName = worksheet.Cells[row, 2].Text.Trim(); // Column 2
   // ... adjust column numbers as needed
   ```
4. Add/remove fields based on your Swiggy report structure
5. Update field mappings in the `OnlineSale` object creation

## Testing

Sample Zomato row:
```
7438106357 | | 01-Nov-25 | 0.48 | 1 x Chicken Red Sauce Pasta, 1 x Bread Egg Toast | | | 158 | 0 | 0 | 158 | 154.4 | | 3.6 | | | 5 | Nice packaging and taste is yumm | 11.1 | 11 | Correctly | 
```

Sample Swiggy row (customize based on your format):
```
SWG12345 | John Doe | 01-Dec-24 | 2.5 | 2 x Pizza Margherita | Extra cheese | 450 | 425 | 25 | 4.5 | Great taste!
```

## Analytics

Daily income is calculated separately per platform:
- Total Payout by platform
- Platform deductions
- Average ratings
- Order counts

This allows you to compare Zomato vs Swiggy performance easily.
