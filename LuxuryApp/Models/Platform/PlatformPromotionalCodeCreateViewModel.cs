using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.Platform
{
    public sealed class PlatformPromotionalCodeCreateViewModel
    {
        [Required]
        [StringLength(100)]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        public Guid PlanId { get; set; }

        [Range(1, 365)]
        public int DiasGratis { get; set; } = 30;

        [Range(1, 100000)]
        public int? MaxUsos { get; set; } = 1;

        public DateTime? FechaExpiracionUtc { get; set; }

        public bool SoloPrimerRegistro { get; set; } = true;

        [EmailAddress]
        [StringLength(256)]
        public string? EmailObjetivo { get; set; }

        [StringLength(2000)]
        public string? NotasInternas { get; set; }

        public bool Activo { get; set; } = true;
    }
}
