# Data Dictionary

## Users table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| UserID | int | N/A | Primary key. Unique user ID. |
| Email | nvarchar | 100 | Required. User login email address. |
| PasswordHash | nvarchar | MAX | Required. Hashed password value. |
| FullName | nvarchar | 100 | User full name. |
| Role | nvarchar | 50 | User role (for example: SuperAdmin, Owner, Inventory Manager, Sales Staff, Customer). |
| UserType | nvarchar | 20 | User type (Internal or Customer). |
| OwnerUserID | int | N/A | Optional owner workspace scope ID. |
| AllowSuperAdminWorkspaceEdits | bit | 1 | Indicates if SuperAdmin can edit this owner workspace. |
| IsActive | bit | 1 | Indicates if account is active. |
| IsEmailVerified | bit | 1 | Indicates if email has been verified. |
| EmailVerificationToken | nvarchar | 100 | Optional email verification token. |
| DateCreated | datetime2 | N/A | UTC timestamp when account was created. |
| FailedLoginAttempts | int | N/A | Number of failed login attempts. |
| LockoutEnd | datetime2 | N/A | Optional UTC lockout end timestamp. |
| LastFailedLogin | datetime2 | N/A | Optional UTC timestamp of last failed login. |

## Employees table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| EmpID | int | N/A | Primary key. Unique employee ID. |
| UserID | int | N/A | Foreign key to `Users.UserID`. |
| OwnerUserID | int | N/A | Optional owner workspace ID. |
| Position | nvarchar | 30 | Employee position/title. |
| FullName | nvarchar | 100 | Employee full name. |
| ContactNumber | nvarchar | 15 | Employee contact number. |
| IsArchived | bit | 1 | Indicates if employee is archived. |
| HireDate | datetime2 | N/A | Employee hire date. |

## Customers table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| CustomerID | int | N/A | Primary key. Unique customer ID. |
| UserID | int | N/A | Foreign key to `Users.UserID`. |
| FullName | nvarchar | 50 | Required. Customer full name. |
| Email | nvarchar | 50 | Required. Customer email address. |
| Phone | nvarchar | 20 | Customer phone number. |
| Address | nvarchar | 200 | Customer address. |
| City | nvarchar | 100 | Customer city. |
| RegistrationDate | datetime2 | N/A | Customer registration timestamp. |
| MarketingOptIn | bit | 1 | Indicates if customer opted in for marketing. |
| SteamId | nvarchar | 100 | Optional linked Steam ID. |
| LoyaltyPoints | int | N/A | Current loyalty points balance. |
| LifetimePointsEarned | int | N/A | Total lifetime points earned. |
| LifetimePointsRedeemed | int | N/A | Total lifetime points redeemed. |
| LastLoyaltyActivityUtc | datetime2 | N/A | Optional UTC timestamp of last loyalty activity. |

## Products table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| ProductID | int | N/A | Primary key. Unique product ID. |
| CategoryName | nvarchar | 30 | Required. Product category name. |
| OwnerUserID | int | N/A | Optional owner workspace ID. |
| ProductName | nvarchar | 50 | Required. Product name. |
| CostPrice | decimal | 10,2 | Product cost price. |
| SellingPrice | decimal | 10,2 | Product selling price. |
| MarketPrice | decimal | 10,2 | Market reference price. |
| MarketPriceSource | nvarchar | 50 | Source label for market price data. |
| LastMarketPriceSyncUtc | datetime2 | N/A | Optional UTC timestamp of last market price sync. |
| StockQuantity | int | N/A | Available stock quantity. |
| DamagedQuantity | int | N/A | Damaged stock quantity. |
| ConditionStatus | nvarchar | 30 | Product condition status. |
| ConditionNotes | nvarchar | 300 | Product condition notes. |
| LastConditionCheckUtc | datetime2 | N/A | Optional UTC timestamp of last condition check. |
| Description | nvarchar | 500 | Product description. |
| Sku | nvarchar | MAX | Product SKU/code. |
| Brand | nvarchar | MAX | Product brand. |
| ImageUrl | nvarchar | MAX | Product image URL. |

## Orders table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| OrderID | int | N/A | Primary key. Unique order ID. |
| CustomerID | int | N/A | Foreign key to `Customers.CustomerID`. |
| OwnerUserID | int | N/A | Optional owner workspace ID. |
| OrderDate | datetime2 | N/A | UTC timestamp when order was placed. |
| TotalAmount | decimal | 10,2 | Final order total amount. |
| OrderStatus | nvarchar | 20 | Order lifecycle status (Pending, Processing, Completed, Cancelled). |
| PaymentStatus | nvarchar | 20 | Payment status (Pending, Paid, Refunded). |
| LoyaltyPointsEarned | int | N/A | Loyalty points earned from the order. |
| LoyaltyPointsRedeemed | int | N/A | Loyalty points redeemed in the order. |
| LoyaltyDiscountAmount | decimal | 10,2 | Discount amount from redeemed loyalty points. |

## OrderDetails table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| DetailID | int | N/A | Primary key. Unique order line item ID. |
| OrderID | int | N/A | Foreign key to `Orders.OrderID`. |
| ProductID | int | N/A | Foreign key to `Products.ProductID`. |
| Quantity | int | N/A | Quantity sold for this line item. |
| UnitPriceAtSale | decimal | 10,2 | Unit price recorded at time of sale. |
| SubTotal | decimal | 10,2 | Line subtotal (`Quantity x UnitPriceAtSale`). |

## Payments table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| PaymentID | int | N/A | Primary key. Unique payment ID. |
| OrderID | int | N/A | Foreign key to `Orders.OrderID`. |
| OwnerUserID | int | N/A | Optional owner workspace ID. |
| PaymentMethod | nvarchar | 25 | Payment method (for example: Card, GCash, BankTransfer). |
| PaymentDateUtc | datetime2 | N/A | UTC timestamp when payment was recorded. |
| AmountPaid | decimal | 10,2 | Amount paid. |

## TechnicalInquiries table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| InquiryID | int | N/A | Primary key. Unique inquiry ID. |
| CustomerID | int | N/A | Foreign key to `Customers.CustomerID`. |
| OwnerUserID | int | N/A | Optional owner workspace ID assigned to handle inquiry. |
| Subject | nvarchar | 100 | Required. Inquiry subject. |
| InquiryMessage | nvarchar | 1000 | Required. Inquiry content/message. |
| DateSubmittedUtc | datetime2 | N/A | UTC timestamp when inquiry was submitted. |
| IsResolved | bit | 1 | Indicates if inquiry is resolved. |
| DateResolvedUtc | datetime2 | N/A | Optional UTC timestamp when inquiry was resolved. |
| ResolutionNotes | nvarchar | MAX | Optional notes for inquiry resolution. |

## EmailNotifications table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| NotifID | int | N/A | Primary key. Unique email notification ID. |
| CustomerID | int | N/A | Foreign key to `Customers.CustomerID`. |
| OwnerUserID | int | N/A | Optional owner workspace ID. |
| CampaignID | int | N/A | Optional foreign key to `Campaigns.CampaignID`. |
| Subject | nvarchar | 50 | Required. Email subject line. |
| DeliveryStatus | nvarchar | 25 | Delivery status (Queued, Accepted, Delivered, Failed, etc.). |
| ExternalMessageId | nvarchar | 100 | Provider message ID reference. |
| DateSent | datetime2 | N/A | UTC timestamp when message was sent/queued. |

## EmailOutbox table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| OutboxID | int | N/A | Primary key. Unique outbox queue ID. |
| OwnerUserID | int | N/A | Optional owner workspace ID. |
| NotifID | int | N/A | Optional link to `EmailNotifications.NotifID`. |
| RecipientEmail | nvarchar | 255 | Required. Recipient email address. |
| Subject | nvarchar | 255 | Required. Email subject line. |
| HtmlBody | nvarchar | MAX | Required. Email HTML body payload. |
| CreatedAtUtc | datetime2 | N/A | UTC timestamp when outbox item was created. |
| RetryCount | int | N/A | Number of retry attempts. |
| ErrorMessage | nvarchar | 500 | Optional latest delivery error details. |

## SystemLogs table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| LogID | int | N/A | Primary key. Unique log record ID. |
| UserID | int | N/A | Foreign key to `Users.UserID`. |
| OwnerUserID | int | N/A | Optional owner workspace ID. |
| Action | nvarchar | 100 | Required. Activity/action description. |
| Timestamp | datetime2 | N/A | UTC timestamp when action was logged. |

## CartItems table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| CartItemID | int | N/A | Primary key. Unique cart item ID. |
| SessionId | nvarchar | MAX | Guest cart session identifier. |
| UserID | int | N/A | Optional foreign key to `Users.UserID` for signed-in carts. |
| ProductID | int | N/A | Foreign key to `Products.ProductID`. |
| Quantity | int | N/A | Quantity in cart. |
| DateCreated | datetime2 | N/A | UTC timestamp when cart item was created. |

## Campaigns table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| CampaignID | int | N/A | Primary key. Unique campaign ID. |
| OwnerUserID | int | N/A | Optional owner workspace ID. |
| Name | nvarchar | 100 | Required. Campaign name. |
| Description | nvarchar | 255 | Optional campaign description. |
| TargetAudience | nvarchar | 50 | Audience segment label (for example: General, GPU Owners, New Customers). |
| Status | nvarchar | 20 | Campaign status (Draft, Scheduled, Completed, CompletedWithErrors, etc.). |
| ScheduledForUtc | datetime2 | N/A | Optional UTC schedule timestamp. |
| StartDate | datetime2 | N/A | Campaign start date/time. |

## InventoryMovements table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| MovementID | int | N/A | Primary key. Unique inventory movement ID. |
| ProductID | int | N/A | Foreign key to `Products.ProductID`. |
| OwnerUserID | int | N/A | Optional owner workspace ID. |
| MovementType | nvarchar | 20 | Movement type (StockIn, StockOut, Adjustment). |
| QuantityDelta | int | N/A | Quantity change applied to stock. |
| QuantityBefore | int | N/A | Stock quantity before movement. |
| QuantityAfter | int | N/A | Stock quantity after movement. |
| UnitCostAtMovement | decimal | 10,2 | Unit cost used at movement time. |
| PartnerName | nvarchar | 100 | Related partner/supplier/customer name. |
| Reason | nvarchar | 120 | Movement reason text. |
| ReferenceType | nvarchar | 30 | Source entity type (for example: Order, PurchaseOrder). |
| ReferenceId | nvarchar | 50 | Source entity ID/reference value. |
| PerformedByUserID | int | N/A | Optional foreign key to `Users.UserID`. |
| OccurredAtUtc | datetime2 | N/A | UTC timestamp when movement occurred. |

## FinancialBudgets table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| BudgetID | int | N/A | Primary key. Unique budget ID. |
| OwnerUserID | int | N/A | Optional owner workspace ID. |
| Status | nvarchar | 20 | Budget status (Active/Archived). |
| PeriodStartDateLocal | datetime2 | N/A | Budget period start date (local). |
| PeriodEndDateLocal | datetime2 | N/A | Budget period end date (local). |
| BudgetAmount | decimal | 10,2 | Planned budget amount for period. |
| CreatedAtUtc | datetime2 | N/A | UTC timestamp when budget was created. |
| UpdatedAtUtc | datetime2 | N/A | UTC timestamp when budget was last updated. |

## BudgetEvents table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| BudgetEventID | int | N/A | Primary key. Unique budget event ID. |
| OwnerUserID | int | N/A | Optional owner workspace ID. |
| BudgetID | int | N/A | Foreign key to `FinancialBudgets.BudgetID`. |
| EventType | nvarchar | 30 | Budget event type (Create, Adjust, Spend, Procurement, etc.). |
| Amount | decimal | 10,2 | Event amount/value. |
| BeforeAmount | decimal | 10,2 | Optional budget amount before event. |
| AfterAmount | decimal | 10,2 | Optional budget amount after event. |
| Reason | nvarchar | 250 | Event reason/notes. |
| ReferenceType | nvarchar | 30 | Related source type (for example: PurchaseOrder). |
| ReferenceId | nvarchar | 50 | Related source ID/reference value. |
| PerformedByUserID | int | N/A | Optional foreign key to `Users.UserID`. |
| OccurredAtUtc | datetime2 | N/A | UTC timestamp when event occurred. |

## PurchaseOrders table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| PurchaseOrderID | int | N/A | Primary key. Unique purchase order ID. |
| OwnerUserID | int | N/A | Optional owner workspace ID. |
| BudgetID | int | N/A | Optional foreign key to `FinancialBudgets.BudgetID`. |
| PurchaseOrderNumber | nvarchar | 30 | Purchase order number/code. |
| SupplierName | nvarchar | 100 | Supplier name. |
| Status | nvarchar | 20 | PO status (`Draft`, `Approved`, `PartiallyReceived`, `Received`, `Cancelled`). |
| TotalAmount | decimal | 10,2 | Total purchase order amount. |
| CreatedAtUtc | datetime2 | N/A | UTC timestamp when PO was created. |
| UpdatedAtUtc | datetime2 | N/A | UTC timestamp when PO was last updated. |
| ApprovedAtUtc | datetime2 | N/A | Optional UTC timestamp when PO was approved. |
| FullyReceivedAtUtc | datetime2 | N/A | Optional UTC timestamp when PO was fully received. |
| Notes | nvarchar | 500 | Additional PO notes. |

## PurchaseOrderLines table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| PurchaseOrderLineID | int | N/A | Primary key. Unique purchase order line ID. |
| PurchaseOrderID | int | N/A | Foreign key to `PurchaseOrders.PurchaseOrderID`. |
| ProductID | int | N/A | Foreign key to `Products.ProductID`. |
| QuantityOrdered | int | N/A | Quantity ordered. |
| QuantityReceived | int | N/A | Quantity received. |
| UnitCost | decimal | 10,2 | Unit cost for this line. |
| LineTotal | decimal | 10,2 | Line total amount. |
| CreatedAtUtc | datetime2 | N/A | UTC timestamp when line was created. |
| UpdatedAtUtc | datetime2 | N/A | UTC timestamp when line was last updated. |

## PasswordResetTokens table
| Field Names | Datatype | Length | Description |
|---|---|---|---|
| ResetTokenID | int | N/A | Primary key. Unique reset token ID. |
| UserID | int | N/A | Foreign key to `Users.UserID`. |
| TokenHash | nvarchar | 64 | Required. Hashed reset token value (unique index). |
| Audience | nvarchar | 20 | Required. Token audience (`Customer` or `Admin`). |
| CreatedAtUtc | datetime2 | N/A | UTC timestamp when token was created. |
| ExpiresAtUtc | datetime2 | N/A | UTC expiration timestamp. |
| ConsumedAtUtc | datetime2 | N/A | Optional UTC timestamp when token was used. |

