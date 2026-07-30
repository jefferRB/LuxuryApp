using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Common;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LuxuryApp.Models.Inversionistas
{
    /// <summary>
    /// Inversionista del negocio. NO es un usuario del sistema: es un contacto al que se le
    /// envían estados de cuenta. No tiene portal ni credenciales en esta fase.
    /// </summary>
    public class TenantInvestor : ITenantEntity
    {
        [BindNever]
        public Guid TenantId { get; set; }

        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Indicá el nombre del inversionista.")]
        [MaxLength(150)]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "Indicá el correo del inversionista.")]
        [MaxLength(256)]
        [Display(Name = "Correo electrónico")]
        public string Email { get; set; } = string.Empty;

        [MaxLength(30)]
        [Display(Name = "Teléfono")]
        public string? Telefono { get; set; }

        [Display(Name = "Activo")]
        public bool Activo { get; set; } = true;

        /// <summary>Notas internas del negocio. NUNCA se incluyen en el correo al inversionista.</summary>
        [MaxLength(1000)]
        [Display(Name = "Notas internas")]
        public string? NotasInternas { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(450)]
        public string? CreatedByUserId { get; set; }

        [MaxLength(450)]
        public string? UpdatedByUserId { get; set; }

        public ICollection<InvestorAgreement> Acuerdos { get; set; } = new List<InvestorAgreement>();

        public ICollection<InvestorStatement> Estados { get; set; } = new List<InvestorStatement>();
    }
}
