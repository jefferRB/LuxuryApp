using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.Reservas
{
    /// <summary>Página del panel privado de solicitudes de reserva.</summary>
    public sealed class BookingRequestsPageViewModel
    {
        public string EstadoFiltro { get; set; } = BookingRequestStates.Pending;
        public string RangoFiltro { get; set; } = "mes";

        public int PendientesCount { get; set; }
        public int ConfirmadasCount { get; set; }
        public int RechazadasCount { get; set; }

        public bool ReservasActivas { get; set; }
        public string? Slug { get; set; }
        public string? LinkPublico { get; set; }

        public IReadOnlyList<BookingRequestListItemViewModel> Solicitudes { get; set; } =
            Array.Empty<BookingRequestListItemViewModel>();
    }

    public sealed class BookingRequestListItemViewModel
    {
        public int Id { get; set; }
        public string NombreCliente { get; set; } = string.Empty;
        public string TelefonoCliente { get; set; } = string.Empty;
        public string? CorreoCliente { get; set; }
        public string ServicioNombre { get; set; } = string.Empty;
        public string FuncionarioNombre { get; set; } = "Cualquier funcionario";
        public bool SolicitoCualquierFuncionario { get; set; }
        public DateTime FechaHoraInicioSolicitada { get; set; }
        public int DuracionMinutos { get; set; }
        public string? NotasCliente { get; set; }
        public string Estado { get; set; } = BookingRequestStates.Pending;
        public DateTime CreatedAtUtc { get; set; }
        public string? RejectedReason { get; set; }
        public int? ConvertedCitaId { get; set; }
        public bool AceptaWhatsApp { get; set; }
    }

    /// <summary>Configuración "Reservas online" (vista privada del negocio).</summary>
    public sealed class BookingSettingsViewModel
    {
        public bool PublicBookingEnabled { get; set; }

        [MaxLength(80)]
        public string? PublicBookingSlug { get; set; }

        public bool PublicBookingAllowEmployeeSelection { get; set; }

        public bool PublicBookingAllowAnyEmployee { get; set; } = true;

        [Range(0, 43200)]
        public int PublicBookingMinAdvanceMinutes { get; set; } = TenantBookingSettings.DefaultMinAdvanceMinutes;

        [Range(1, 365)]
        public int PublicBookingMaxDaysAhead { get; set; } = TenantBookingSettings.DefaultMaxDaysAhead;

        [MaxLength(500)]
        public string? PublicBookingWelcomeMessage { get; set; }

        [MaxLength(500)]
        public string? PublicBookingConfirmationMessage { get; set; }

        // Jornada del negocio
        public TimeOnly OpenTime { get; set; } = TenantBookingSettings.DefaultOpenTime;
        public TimeOnly CloseTime { get; set; } = TenantBookingSettings.DefaultCloseTime;

        [Range(5, 240)]
        public int SlotIntervalMinutes { get; set; } = TenantBookingSettings.DefaultSlotIntervalMinutes;

        /// <summary>Días laborales: índice 0=Domingo .. 6=Sábado.</summary>
        public bool[] DiasLaborales { get; set; } = new bool[7];

        // Solo lectura, para la UI
        public string NombreNegocio { get; set; } = string.Empty;
        public string? LinkPublico { get; set; }
    }
}
