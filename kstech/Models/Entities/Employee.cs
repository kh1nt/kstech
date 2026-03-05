using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using kstech.Models;

namespace kstech.Models.Entities
{
    public class Employee
    {
        [Key]
        public int EmpID { get; set; }

        public int UserID { get; set; }
        [ForeignKey("UserID")]
        public User? User { get; set; }

        public int? OwnerUserID { get; set; }

        [StringLength(30)]
        public string Position { get; set; } = string.Empty;

        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(15)]
        public string ContactNumber { get; set; } = string.Empty;

        public bool IsArchived { get; set; }

        public DateTime HireDate { get; set; }
    }
}
