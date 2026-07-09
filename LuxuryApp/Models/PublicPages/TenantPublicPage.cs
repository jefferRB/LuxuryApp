using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Common;
using LuxuryApp.Models.SaaS;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LuxuryApp.Models.PublicPages
{
    /// <summary>
    /// Configuracion publica de la mini landing del negocio. Separada de Tenant para
    /// mantener la ficha publica fuera de los datos internos del SaaS.
    /// </summary>
    public sealed class TenantPublicPage : ITenantEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [BindNever]
        public Guid TenantId { get; set; }

        public Tenant? Tenant { get; set; }

        public bool IsPublished { get; set; }

        [MaxLength(120)]
        public string? HeroTitle { get; set; }

        [MaxLength(180)]
        public string? HeroSubtitle { get; set; }

        [MaxLength(80)]
        public string? HeroEyebrow { get; set; }

        [MaxLength(1500)]
        public string? Description { get; set; }

        [MaxLength(400)]
        public string? LogoUrl { get; set; }

        [MaxLength(400)]
        public string? CoverImageUrl { get; set; }

        [MaxLength(30)]
        public string? Phone { get; set; }

        [MaxLength(30)]
        public string? WhatsAppPhone { get; set; }

        [MaxLength(256)]
        public string? Email { get; set; }

        [MaxLength(300)]
        public string? Address { get; set; }

        [MaxLength(500)]
        public string? GoogleMapsUrl { get; set; }

        [MaxLength(500)]
        public string? WazeUrl { get; set; }

        [MaxLength(300)]
        public string? InstagramUrl { get; set; }

        [MaxLength(300)]
        public string? FacebookUrl { get; set; }

        [MaxLength(300)]
        public string? TikTokUrl { get; set; }

        public bool ShowServices { get; set; } = true;

        public bool ShowPrices { get; set; } = true;

        public bool ShowTeam { get; set; }

        public bool ShowLocation { get; set; } = true;

        public bool ShowWhatsAppButton { get; set; } = true;

        [MaxLength(70)]
        public string? SeoTitle { get; set; }

        [MaxLength(180)]
        public string? SeoDescription { get; set; }

        [MaxLength(500)]
        public string? BusinessHours { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
