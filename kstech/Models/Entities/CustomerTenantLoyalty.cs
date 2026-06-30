using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace kstech.Models.Entities
{
    public class CustomerTenantLoyalty
    {
        [Key]
        public int ID { get; set; }

        public int CustomerID { get; set; }
        [ForeignKey("CustomerID")]
        public Customer? Customer { get; set; }

        public int TenantOwnerUserID { get; set; }

        public int LoyaltyPoints { get; set; }
        public int LifetimePointsEarned { get; set; }
        public int LifetimePointsRedeemed { get; set; }
        public DateTime? LastLoyaltyActivityUtc { get; set; }
    }
}
