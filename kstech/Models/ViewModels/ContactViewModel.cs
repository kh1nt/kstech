using System.ComponentModel.DataAnnotations;

namespace kstech.Models.ViewModels
{
    public class ContactViewModel
    {
        [Required(ErrorMessage = "Please let us know what this is about.")]
        [StringLength(100, ErrorMessage = "The subject must be at most 100 characters.")]
        public string Subject { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please provide the details of your inquiry.")]
        [StringLength(1000, ErrorMessage = "The message must be at most 1000 characters.")]
        [Display(Name = "Your Message")]
        public string Message { get; set; } = string.Empty;
    }
}
