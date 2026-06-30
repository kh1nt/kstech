namespace kstech.Services
{
    /// <summary>
    /// Centralised role name constants to eliminate magic strings across the codebase.
    /// These must match the values stored in the Users.Role column exactly.
    /// </summary>
    public static class Roles
    {
        public const string SuperAdmin       = "SuperAdmin";
        public const string Owner            = "Owner";
        public const string Admin            = "Admin";
        public const string InventoryManager = "Inventory Manager";
        public const string SalesStaff       = "Sales Staff";
        public const string Customer         = "Customer";

        // Composite strings for [Authorize(Roles = "...")] attributes
        public const string SuperAdminOnly        = SuperAdmin;
        public const string SuperAdminAndOwner    = $"{SuperAdmin},{Owner}";
        public const string AllInternal           = $"{SuperAdmin},{Owner},{Admin},{InventoryManager},{SalesStaff}";
        public const string InventoryAndAbove     = $"{SuperAdmin},{Owner},{Admin},{InventoryManager}";
        public const string SalesAndAbove         = $"{SuperAdmin},{Owner},{Admin},{SalesStaff}";
    }
}
