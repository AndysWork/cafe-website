# Database Indexing Strategy

> Auto-generated documentation for CafeDB (MongoDB Atlas) indexing strategy.
> All indexes are created at application startup via `MongoService.EnsureIndexesAsync()`.
> All indexes use `Background = true` for non-blocking creation.

---

## Index Inventory

### Users Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `username_1` | `Username` ASC | **Unique** | Login lookup |
| `email_1` | `Email` ASC | **Unique** | Email lookup, password reset |
| `phoneNumber_1` | `PhoneNumber` ASC | Single | Phone search |
| `role_1` | `Role` ASC | Single | Admin/user filtering |

### Orders Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `userId_1_createdAt_-1` | `UserId` ASC, `CreatedAt` DESC | Compound | "My Orders" sorted by date |
| `userId_1_status_1` | `UserId` ASC, `Status` ASC | Compound | User orders filtered by status |
| `outletId_1_createdAt_-1` | `OutletId` ASC, `CreatedAt` DESC | Compound | Admin order list per outlet |
| `outletId_1_status_1` | `OutletId` ASC, `Status` ASC | Compound | Kitchen display, status filtering |
| `status_1` | `Status` ASC | Single | Global status queries |
| `createdAt_-1` | `CreatedAt` DESC | Single | Date-range queries |
| `paymentStatus_1` | `PaymentStatus` ASC | Single | Payment reconciliation |

### CafeMenu Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1` | `OutletId` ASC | Single | Menu per outlet |
| `outletId_1_categoryId_1` | `OutletId` ASC, `CategoryId` ASC | Compound | Menu items by outlet + category |
| `categoryId_1` | `CategoryId` ASC | Single | Category filter |
| `subCategoryId_1` | `SubCategoryId` ASC | Single | SubCategory filter |
| `name_text_description_text` | `Name`, `Description` | **Text** | Full-text search |
| `onlinePrice_1` | `OnlinePrice` ASC | Single | Price sorting |

### Categories Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1` | `OutletId` ASC | Single | Categories per outlet |

### SubCategories Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_categoryId_1` | `OutletId` ASC, `CategoryId` ASC | Compound | SubCategories per outlet+category |

### Sales Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_date_-1` | `OutletId` ASC, `Date` DESC | Compound | Sales by outlet sorted by date |
| `date_-1` | `Date` DESC | Single | Date-range queries |
| `recordedBy_1` | `RecordedBy` ASC | Single | Audit trail |
| `paymentMethod_1` | `PaymentMethod` ASC | Single | Payment breakdown |

### Expenses Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_date_-1` | `OutletId` ASC, `Date` DESC | Compound | Expenses by outlet sorted by date |
| `date_-1` | `Date` DESC | Single | Date-range queries |
| `expenseType_1` | `ExpenseType` ASC | Single | Type filtering |
| `expenseSource_1` | `ExpenseSource` ASC | Single | Source filtering |
| `recordedBy_1` | `RecordedBy` ASC | Single | Audit trail |

### Inventory Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_isActive_1` | `OutletId` ASC, `IsActive` ASC | Compound | Active inventory per outlet |
| `outletId_1_status_1` | `OutletId` ASC, `Status` ASC | Compound | Status-filtered inventory per outlet |
| `category_1` | `Category` ASC | Single | Category filter |
| `status_1` | `Status` ASC | Single | Stock status (low/out/ok) |
| `ingredientId_1` | `IngredientId` ASC | Single | Link to ingredient |

### InventoryTransactions Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_transactionDate_-1` | `OutletId` ASC, `TransactionDate` DESC | Compound | Transaction history per outlet |
| `inventoryId_1` | `InventoryId` ASC | Single | Transactions for a specific item |

### StockAlerts Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `isResolved_1` | `IsResolved` ASC | Single | Active vs resolved filter |
| `inventoryId_1` | `InventoryId` ASC | Single | Alerts for specific item |

### Ingredients Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_isActive_1` | `OutletId` ASC, `IsActive` ASC | Compound | Active ingredients per outlet |
| `outletId_1_category_1` | `OutletId` ASC, `Category` ASC | Compound | Ingredients by outlet+category |

### Recipes Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `menuItemId_1` | `MenuItemId` ASC | Single | Recipe lookup by menu item |
| `outletId_1` | `OutletId` ASC | Single | Recipes per outlet |

### IngredientPriceHistory Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `ingredientId_1_recordedAt_-1` | `IngredientId` ASC, `RecordedAt` DESC | Compound | Price history timeline |

### Staff Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `isActive_1` | `IsActive` ASC | Single | Active staff filter |
| `outletIds_1` | `OutletIds` ASC | Single (multikey) | Staff by outlet assignment |

### DailyPerformanceEntries Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_date_1` | `OutletId` ASC, `Date` ASC | Compound | Performance by outlet+date |
| `staffId_1_date_1` | `StaffId` ASC, `Date` ASC | Compound | Performance by staff+date |
| `staffId_1_date_1_outletId_1` | `StaffId` ASC, `Date` ASC, `OutletId` ASC | Compound (3) | Covering index for staff queries |

### StaffPerformanceRecords Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `staffId_1_recordDate_-1` | `StaffId` ASC, `RecordDate` DESC | Compound | Performance records per staff |

### Attendance Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_date_1` | `OutletId` ASC, `Date` ASC | Compound | Daily attendance per outlet |
| `staffId_1_date_1` | `StaffId` ASC, `Date` ASC | Compound | Attendance per staff member |

### LeaveRequests Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_status_1` | `OutletId` ASC, `Status` ASC | Compound | Pending leave requests per outlet |
| `staffId_1` | `StaffId` ASC | Single | Leave requests by staff |

### BonusConfigurations Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1` | `OutletId` ASC | Single | Config per outlet |

### LoyaltyAccounts Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `userId_1` | `UserId` ASC | **Unique** | Account lookup by user |
| `tier_1` | `Tier` ASC | Single | Tier-based queries |
| `currentPoints_-1` | `CurrentPoints` DESC | Single | Leaderboard |

### PointsTransactions Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `userId_1_createdAt_-1` | `UserId` ASC, `CreatedAt` DESC | Compound | Transaction history |
| `type_1` | `Type` ASC | Single | Type filtering |

### Rewards Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `isActive_1` | `IsActive` ASC | Single | Active rewards listing |

### Offers Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `code_1` | `Code` ASC | **Unique** | Coupon code lookup |
| `isActive_1_validTill_1` | `IsActive` ASC, `ValidTill` ASC | Compound | Active offers query |
| `validFrom_1_validTill_1` | `ValidFrom` ASC, `ValidTill` ASC | Compound | Date-range validity |

### CustomerWallets Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `userId_1` | `UserId` ASC | **Unique** | Wallet lookup by user |

### WalletTransactions Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `userId_1_createdAt_-1` | `UserId` ASC, `CreatedAt` DESC | Compound | Transaction history |

### CustomerReviews Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `orderId_1` | `OrderId` ASC | Single | Review by order |
| `outletId_1_createdAt_-1` | `OutletId` ASC, `CreatedAt` DESC | Compound | Reviews per outlet |

### ExternalOrderClaims Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `userId_1` | `UserId` ASC | Single | Claims by user |
| `status_1` | `Status` ASC | Single | Pending/approved filter |

### Notifications Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `userId_1_createdAt_-1` | `UserId` ASC, `CreatedAt` DESC | Compound | User notification feed |
| `userId_1_isRead_1` | `UserId` ASC, `IsRead` ASC | Compound | Unread count |
| `createdAt_ttl` | `CreatedAt` ASC | **TTL (90 days)** | Auto-cleanup old notifications |

### OnlineSales Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `platform_1` | `Platform` ASC | Single | Zomato/Swiggy filter |
| `orderAt_-1` | `OrderAt` DESC | Single | Date sort |
| `platform_1_orderAt_-1` | `Platform` ASC, `OrderAt` DESC | Compound | Platform + date filter |
| `orderId_1` | `OrderId` ASC | Single | Order lookup |

### CashReconciliations Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_date_-1` | `OutletId` ASC, `Date` DESC | Compound | Daily reconciliation per outlet |

### OperationalExpenses Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_year_-1_month_-1` | `OutletId` ASC, `Year` DESC, `Month` DESC | Compound (3) | Monthly expenses per outlet |

### OverheadCosts Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_isActive_1` | `OutletId` ASC, `IsActive` ASC | Compound | Active costs per outlet |

### FrozenItems Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_isActive_1` | `OutletId` ASC, `IsActive` ASC | Compound | Active frozen items per outlet |

### PriceForecasts Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `menuItemId_1_createdDate_-1` | `MenuItemId` ASC, `CreatedDate` DESC | Compound | Latest forecast per item |

### TableReservations Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_reservationDate_1` | `OutletId` ASC, `ReservationDate` ASC | Compound | Reservations per outlet+date |
| `userId_1` | `UserId` ASC | Single | User's reservations |

### WastageRecords Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1_date_-1` | `OutletId` ASC, `Date` DESC | Compound | Wastage per outlet+date |

### ComboMeals Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1` | `OutletId` ASC | Single | Combos per outlet |

### HappyHourRules Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1` | `OutletId` ASC | Single | Rules per outlet |

### DeliveryZones Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `outletId_1` | `OutletId` ASC | Single | Zones per outlet |

### PasswordResetTokens Collection
| Index Name | Fields | Type | Notes |
|---|---|---|---|
| `token_1` | `Token` ASC | **Unique** | Token lookup |
| `userId_1` | `UserId` ASC | Single | Tokens by user |
| `expiresAt_1` | `ExpiresAt` ASC | Single | Expired token cleanup |

---

## Index Design Principles

1. **Multi-tenant first**: All outlet-scoped collections lead compound indexes with `OutletId` to ensure queries are scoped efficiently
2. **Compound index prefix rule**: The most-filtered field is first (typically `OutletId`), followed by sort/range fields
3. **Background creation**: All indexes use `Background = true` to avoid blocking the application during startup
4. **Unique constraints**: Applied to natural keys (Username, Email, Offer Code, Token) and 1:1 relationships (UserId on LoyaltyAccount, CustomerWallet)
5. **TTL index**: Notifications auto-expire after 90 days using MongoDB's TTL feature
6. **Text index**: CafeMenu supports full-text search on Name and Description
7. **Collections without indexes**: Small reference data collections (SalesItemType, OfflineExpenseType, OnlineExpenseType, PlatformCharge, DiscountCoupon, SubscriptionPlan, CustomerSubscription, DeliveryPartner, CustomerSegment, PriceUpdateSettings) do not have custom indexes as they are small and fully cached or rarely queried at scale
