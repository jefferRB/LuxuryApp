using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.Reservas
{
    /// <summary>
    /// Datos públicos y seguros para renderizar la página /reservar/{slug}.
    /// No expone TenantId, ids internos sensibles ni datos financieros.
    /// </summary>
    public sealed class PublicBookingPageViewModel
    {
        public string Slug { get; set; } = string.Empty;
        public string NombreNegocio { get; set; } = string.Empty;
        public string? MensajeBienvenida { get; set; }
        public bool PermiteElegirFuncionario { get; set; }
        public bool PermiteCualquierFuncionario { get; set; }
        public bool MostrarWhatsApp { get; set; }
        public int MinAdvanceMinutes { get; set; }
        public int MaxDaysAhead { get; set; }

        /// <summary>Fecha mínima seleccionable (hoy en hora del negocio), formato yyyy-MM-dd.</summary>
        public string MinDateIso { get; set; } = string.Empty;

        /// <summary>Fecha máxima seleccionable, formato yyyy-MM-dd.</summary>
        public string MaxDateIso { get; set; } = string.Empty;

        public IReadOnlyList<PublicBookingServiceOption> Servicios { get; set; } =
            Array.Empty<PublicBookingServiceOption>();

        public IReadOnlyList<PublicBookingEmployeeOption> Funcionarios { get; set; } =
            Array.Empty<PublicBookingEmployeeOption>();
    }

    public sealed class PublicBookingServiceOption
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int DuracionMinutos { get; set; }
    }

    public sealed class PublicBookingEmployeeOption
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }

    /// <summary>Resultado JSON del endpoint de disponibilidad. Solo expone horas, nada interno.</summary>
    public sealed class BookingAvailabilityResult
    {
        public string Fecha { get; set; } = string.Empty;
        public IReadOnlyList<string> Horas { get; set; } = Array.Empty<string>();
        public string? Mensaje { get; set; }
    }

    /// <summary>Payload del formulario público de solicitud (/reservar/{slug}/solicitar).</summary>
    public sealed class PublicBookingRequestInput
    {
        public int ServicioId { get; set; }

        public int? FuncionarioId { get; set; }

        /// <summary>Fecha solicitada, formato yyyy-MM-dd.</summary>
        public string? Fecha { get; set; }

        /// <summary>Hora de inicio solicitada, formato HH:mm.</summary>
        public string? Hora { get; set; }

        [MaxLength(120)]
        public string? Nombre { get; set; }

        [MaxLength(30)]
        public string? Telefono { get; set; }

        public bool AceptaWhatsApp { get; set; }

        /// <summary>Honeypot anti-bot: debe llegar vacío. Si trae valor, se descarta la solicitud.</summary>
        public string? Website { get; set; }
    }
}
