namespace LuxuryApp.Services.Tilopay
{
    /// <summary>Un suscriptor recurrente devuelto por getSuscriptorRepeat, parseado de forma tolerante.</summary>
    public sealed record TilopaySubscriber
    {
        /// <summary>ID único del suscriptor en TiloPay (campo "id" o "id_suscriptor" del payload).</summary>
        public required string SubscriberId { get; init; }

        public string? Email { get; init; }

        /// <summary>Estado crudo reportado por TiloPay (1=Activo, 3=Pausado, 4=Eliminado, si viene).</summary>
        public string? Status { get; init; }

        /// <summary>Fecha de creación si el payload la incluye (para desempatar por recencia).</summary>
        public DateTime? CreatedAtUtc { get; init; }

        /// <summary>Plan id de TiloPay si viene en la fila.</summary>
        public int? TilopayPlanId { get; init; }
    }

    public enum SubscriberResolutionStatus
    {
        /// <summary>Exactamente un suscriptor válido para el email/plan.</summary>
        Found,

        /// <summary>Ningún suscriptor para el email/plan (aún no propagado o inexistente).</summary>
        NotFound,

        /// <summary>Más de un suscriptor válido: NO se elige a ciegas, requiere revisión.</summary>
        Ambiguous,

        /// <summary>La consulta al proveedor falló (red/timeout/credenciales/shape inesperado).</summary>
        Error
    }

    /// <summary>Resultado de resolver el id_suscriptor por (plan, email). Nunca elige a ciegas.</summary>
    public sealed record SubscriberResolutionResult
    {
        public required SubscriberResolutionStatus Status { get; init; }
        public TilopaySubscriber? Subscriber { get; init; }

        /// <summary>Cantidad de candidatos que coincidieron por email (para diagnóstico).</summary>
        public int MatchCount { get; init; }

        /// <summary>Detalle seguro para logs/auditoría (sin secretos ni email en claro).</summary>
        public string? Detail { get; init; }

        public bool IsFound => Status == SubscriberResolutionStatus.Found && Subscriber is not null;

        public static SubscriberResolutionResult Found(TilopaySubscriber subscriber, int matchCount) =>
            new() { Status = SubscriberResolutionStatus.Found, Subscriber = subscriber, MatchCount = matchCount };

        public static SubscriberResolutionResult NotFound(string? detail = null) =>
            new() { Status = SubscriberResolutionStatus.NotFound, MatchCount = 0, Detail = detail };

        public static SubscriberResolutionResult Ambiguous(int matchCount, string? detail = null) =>
            new() { Status = SubscriberResolutionStatus.Ambiguous, MatchCount = matchCount, Detail = detail };

        public static SubscriberResolutionResult Failed(string? detail = null) =>
            new() { Status = SubscriberResolutionStatus.Error, MatchCount = 0, Detail = detail };
    }

    /// <summary>Resultado de una operación admin sobre un suscriptor (pause/reactivate/delete/edit/url).</summary>
    public sealed record TilopayAdminOperationResult
    {
        public required bool Succeeded { get; init; }

        /// <summary>Para recurrentUrl: la URL devuelta por TiloPay.</summary>
        public string? Url { get; init; }

        /// <summary>Mensaje seguro para logs/UI (sin secretos).</summary>
        public string? Message { get; init; }

        /// <summary>
        /// True cuando TiloPay respondió éxito HTTP pero la verificación posterior
        /// (getSuscriptorRepeat) mostró al suscriptor todavía Activo o no pudo confirmarse.
        /// Un 200 sin verificación NUNCA cuenta como cancelación real.
        /// </summary>
        public bool VerificationFailed { get; init; }

        public static TilopayAdminOperationResult Ok(string? message = null, string? url = null) =>
            new() { Succeeded = true, Message = message, Url = url };

        public static TilopayAdminOperationResult Fail(string message) =>
            new() { Succeeded = false, Message = message };

        public static TilopayAdminOperationResult FailVerification(string message) =>
            new() { Succeeded = false, Message = message, VerificationFailed = true };
    }

    /// <summary>Estados admitidos por editSuscriptorRepeat.</summary>
    public enum TilopaySubscriberStatus
    {
        Active = 1,
        Paused = 3,
        Deleted = 4
    }
}
