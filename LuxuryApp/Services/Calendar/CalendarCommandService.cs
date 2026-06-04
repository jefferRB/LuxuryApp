using System.Data;
using System.Globalization;
using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.DataBase;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.WhatsApp;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Calendar
{
    public sealed class CalendarCommandService : ICalendarCommandService
    {
        internal const int DefaultDurationMinutes = 30;
        private const int MinDescansoDurationMinutes = 5;
        private const int MaxDescansoDurationMinutes = 180;
        private static readonly string[] SupportedTipos = ["CITA", "DESCANSO"];
        private readonly ApplicationDbContext _context;
        private readonly ICalendarWhatsAppNotificationService _notificationService;
        private readonly VisitasAutomaticasService _visitasAutomaticasService;
        private readonly ILogger<CalendarCommandService> _logger;

        public CalendarCommandService(
            ApplicationDbContext context,
            ICalendarWhatsAppNotificationService notificationService,
            VisitasAutomaticasService visitasAutomaticasService,
            ILogger<CalendarCommandService> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _visitasAutomaticasService = visitasAutomaticasService;
            _logger = logger;
        }

        public async Task<CalendarAppointmentResponse> CreateAsync(
            CalendarUpsertRequest request,
            CancellationToken cancellationToken = default)
        {
            var normalizedRequest = NormalizeRequest(request);
            var appointmentIdsToQueue = new List<int>();
            CalendarAppointmentResponse? response = null;

            var executionStrategy = _context.Database.CreateExecutionStrategy();

            try
            {
                await executionStrategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);

                    var funcionario = await EnsureFuncionarioActivoAsync(normalizedRequest.FuncionarioId, cancellationToken);
                    var servicio = await ResolveServicioAsync(normalizedRequest, cancellationToken);
                    var duracion = ResolveDuracion(normalizedRequest.Tipo, normalizedRequest.DuracionMinutos, servicio);
                    var resolvedAppointment = await ResolveAppointmentDataAsync(normalizedRequest, cancellationToken);

                    await ValidateRequestAsync(normalizedRequest, resolvedAppointment, servicio, duracion, cancellationToken);

                    var targets = BuildCreationTargets(normalizedRequest);
                    EnsureNoDuplicateTargets(targets);

                    foreach (var target in targets)
                    {
                        await EnsureNoOverlapAsync(
                            funcionario.IdFuncionario,
                            target,
                            duracion,
                            excludeCitaId: null,
                            cancellationToken);
                    }

                    var persistedAppointments = new List<Cita>(targets.Count);
                    foreach (var target in targets)
                    {
                        var cita = new Cita
                        {
                            NombreCliente = resolvedAppointment.NombreCliente,
                            TelefonoCliente = resolvedAppointment.TelefonoCliente,
                            ClienteId = resolvedAppointment.ClienteId,
                            ServicioId = normalizedRequest.Tipo == "DESCANSO" ? null : servicio!.Id,
                            FechaHoraCita = target,
                            FuncionarioId = funcionario.IdFuncionario,
                            Tipo = normalizedRequest.Tipo,
                            DuracionMinutos = normalizedRequest.Tipo == "DESCANSO" ? duracion : null,
                            WhatsAppConsentAtCreation = resolvedAppointment.WhatsAppConsentAtCreation,
                            WhatsAppConsentSource = resolvedAppointment.WhatsAppConsentSource,
                            WhatsAppConsentCapturedAtUtc = resolvedAppointment.WhatsAppConsentCapturedAtUtc,
                            ConfirmacionEnviada = false,
                            Recordatorio24hEnviado = false,
                            Recordatorio3hEnviado = false,
                            VisitaProcesada = false
                        };

                        _context.Citas.Add(cita);
                        persistedAppointments.Add(cita);
                    }

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    var primaryAppointment = persistedAppointments[0];
                    response = BuildAppointmentResponse(
                        primaryAppointment,
                        duracion,
                        funcionario.Nombre,
                        funcionario.ColorCalendario,
                        servicio?.Nombre);

                    appointmentIdsToQueue.AddRange(
                        persistedAppointments
                            .Where(appointment =>
                                string.Equals(appointment.Tipo, "CITA", StringComparison.Ordinal) &&
                                !string.IsNullOrWhiteSpace(appointment.TelefonoCliente))
                            .Select(appointment => appointment.Id));

                    _logger.LogInformation(
                        "Se registraron {CantidadCitas} entradas de agenda para funcionario {FuncionarioId} el {FechaHora:yyyy-MM-dd HH:mm}.",
                        persistedAppointments.Count,
                        funcionario.IdFuncionario,
                        primaryAppointment.FechaHoraCita);
                });

                foreach (var appointmentId in appointmentIdsToQueue)
                {
                    try
                    {
                        await _notificationService.QueueAppointmentConfirmationAsync(appointmentId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "La cita {CitaId} se creo correctamente, pero fallo la cola de confirmacion de WhatsApp.",
                            appointmentId);
                    }
                }

                return response ?? throw new InvalidOperationException("No fue posible construir la respuesta de la cita creada.");
            }
            catch (CalendarValidationException)
            {
                throw;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al registrar una cita para funcionario {FuncionarioId}.", normalizedRequest.FuncionarioId);
                throw new InvalidOperationException("No fue posible registrar la cita.");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Operacion invalida al registrar una cita para funcionario {FuncionarioId}.", normalizedRequest.FuncionarioId);
                throw;
            }
        }

        public async Task<CalendarAppointmentResponse> UpdateAsync(
            int id,
            CalendarUpsertRequest request,
            CancellationToken cancellationToken = default)
        {
            var normalizedRequest = NormalizeRequest(request);
            CalendarAppointmentResponse? response = null;

            var executionStrategy = _context.Database.CreateExecutionStrategy();

            try
            {
                await executionStrategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);

                    var cita = await _context.Citas
                        .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

                    if (cita is null)
                    {
                        throw new InvalidOperationException("La cita indicada no existe o no pertenece al tenant actual.");
                    }

                    var funcionario = await EnsureFuncionarioActivoAsync(normalizedRequest.FuncionarioId, cancellationToken);
                    var servicio = await ResolveServicioAsync(normalizedRequest, cancellationToken);
                    var duracion = ResolveDuracion(normalizedRequest.Tipo, normalizedRequest.DuracionMinutos, servicio);
                    var resolvedAppointment = await ResolveAppointmentDataAsync(normalizedRequest, cancellationToken);

                    await ValidateRequestAsync(normalizedRequest, resolvedAppointment, servicio, duracion, cancellationToken);
                    await EnsureNoOverlapAsync(
                        funcionario.IdFuncionario,
                        normalizedRequest.FechaHoraCita,
                        duracion,
                        excludeCitaId: cita.Id,
                        cancellationToken);

                    cita.NombreCliente = resolvedAppointment.NombreCliente;
                    cita.TelefonoCliente = resolvedAppointment.TelefonoCliente;
                    cita.ClienteId = resolvedAppointment.ClienteId;
                    cita.ServicioId = normalizedRequest.Tipo == "DESCANSO" ? null : servicio!.Id;
                    cita.FechaHoraCita = normalizedRequest.FechaHoraCita;
                    cita.FuncionarioId = funcionario.IdFuncionario;
                    cita.Tipo = normalizedRequest.Tipo;
                    cita.DuracionMinutos = normalizedRequest.Tipo == "DESCANSO" ? duracion : null;
                    cita.WhatsAppConsentAtCreation = resolvedAppointment.WhatsAppConsentAtCreation;
                    cita.WhatsAppConsentSource = resolvedAppointment.WhatsAppConsentSource;
                    cita.WhatsAppConsentCapturedAtUtc = resolvedAppointment.WhatsAppConsentCapturedAtUtc;

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    response = BuildAppointmentResponse(
                        cita,
                        duracion,
                        funcionario.Nombre,
                        funcionario.ColorCalendario,
                        servicio?.Nombre);

                    _logger.LogInformation(
                        "Se actualizo la cita {CitaId} del funcionario {FuncionarioId}.",
                        cita.Id,
                        funcionario.IdFuncionario);
                });

                return response ?? throw new InvalidOperationException("No fue posible construir la respuesta de la cita actualizada.");
            }
            catch (CalendarValidationException)
            {
                throw;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al actualizar la cita {CitaId}.", id);
                throw new InvalidOperationException("No fue posible actualizar la cita.");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Operacion invalida al actualizar la cita {CitaId}.", id);
                throw;
            }
        }

        public async Task MoveAsync(int id, CalendarMoveRequest request, CancellationToken cancellationToken = default)
        {
            var normalizedRequest = NormalizeMoveRequest(request);
            var executionStrategy = _context.Database.CreateExecutionStrategy();

            try
            {
                await executionStrategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);

                    var cita = await _context.Citas
                        .Select(c => new
                        {
                            Entity = c,
                            Duracion = c.Tipo == "DESCANSO"
                                ? (c.DuracionMinutos ?? DefaultDurationMinutes)
                                : ((c.Servicio != null ? c.Servicio.DuracionMinutos : null) ?? DefaultDurationMinutes)
                        })
                        .FirstOrDefaultAsync(c => c.Entity.Id == id, cancellationToken);

                    if (cita is null)
                    {
                        throw new InvalidOperationException("La cita indicada no existe o no pertenece al tenant actual.");
                    }

                    var funcionarioId = normalizedRequest.FuncionarioId ?? cita.Entity.FuncionarioId;
                    var funcionario = await EnsureFuncionarioActivoAsync(funcionarioId, cancellationToken);

                    await EnsureNoOverlapAsync(
                        funcionario.IdFuncionario,
                        normalizedRequest.FechaHoraCita,
                        cita.Duracion,
                        excludeCitaId: cita.Entity.Id,
                        cancellationToken);

                    cita.Entity.FechaHoraCita = normalizedRequest.FechaHoraCita;
                    cita.Entity.FuncionarioId = funcionario.IdFuncionario;

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    _logger.LogInformation(
                        "Se movio la cita {CitaId} al funcionario {FuncionarioId} para {FechaHoraCita:yyyy-MM-dd HH:mm}.",
                        cita.Entity.Id,
                        funcionario.IdFuncionario,
                        normalizedRequest.FechaHoraCita);
                });
            }
            catch (CalendarValidationException)
            {
                throw;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al mover la cita {CitaId}.", id);
                throw new InvalidOperationException("No fue posible mover la cita.");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Operacion invalida al mover la cita {CitaId}.", id);
                throw;
            }
        }

        public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            var cita = await _context.Citas
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (cita is null)
            {
                throw new InvalidOperationException("La cita indicada no existe o no pertenece al tenant actual.");
            }

            _context.Citas.Remove(cita);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Se elimino la cita {CitaId}.", id);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al eliminar la cita {CitaId}.", id);
                throw new InvalidOperationException("No fue posible eliminar la cita.");
            }
        }

        public Task ProcessVisitsAsync(CancellationToken cancellationToken = default) =>
            _visitasAutomaticasService.ProcesarCitasFinalizadas(cancellationToken);

        private async Task ValidateRequestAsync(
            CalendarUpsertRequest request,
            ResolvedAppointmentData resolvedAppointment,
            Servicio? servicio,
            int duracion,
            CancellationToken cancellationToken)
        {
            if (request.FechaHoraCita == default)
            {
                throw new CalendarValidationException("Debe indicar una fecha y hora valida.", nameof(CitaCreateVM.FechaHoraCita));
            }

            if (!SupportedTipos.Contains(request.Tipo, StringComparer.Ordinal))
            {
                throw new CalendarValidationException("El tipo de cita indicado no es valido.", nameof(CitaCreateVM.Tipo));
            }

            if (request.FuncionarioId <= 0)
            {
                throw new CalendarValidationException("Debe seleccionar un funcionario valido.", nameof(CitaCreateVM.FuncionarioId));
            }

            if (request.Tipo == "CITA")
            {
                if (string.IsNullOrWhiteSpace(resolvedAppointment.NombreCliente))
                {
                    throw new CalendarValidationException("Debe indicar el nombre del cliente.", nameof(CitaCreateVM.NombreCliente));
                }

                if (!request.ServicioId.HasValue || request.ServicioId.Value <= 0)
                {
                    throw new CalendarValidationException("Debe seleccionar un servicio.", nameof(CitaCreateVM.ServicioId));
                }

                if (servicio is null)
                {
                    throw new CalendarValidationException("El servicio seleccionado no existe, no esta activo o no pertenece al tenant actual.", nameof(CitaCreateVM.ServicioId));
                }
            }
            else
            {
                if (!request.DuracionMinutos.HasValue)
                {
                    throw new CalendarValidationException("Debe indicar la duracion del descanso.", nameof(CitaCreateVM.DuracionMinutos));
                }

                if (request.DuracionMinutos.Value < MinDescansoDurationMinutes ||
                    request.DuracionMinutos.Value > MaxDescansoDurationMinutes)
                {
                    throw new CalendarValidationException(
                        $"La duracion del descanso debe estar entre {MinDescansoDurationMinutes} y {MaxDescansoDurationMinutes} minutos.",
                        nameof(CitaCreateVM.DuracionMinutos));
                }
            }

            if (duracion <= 0)
            {
                throw new CalendarValidationException("La duracion de la cita debe ser mayor a cero.");
            }

            if (!string.IsNullOrWhiteSpace(resolvedAppointment.TelefonoCliente) && resolvedAppointment.TelefonoCliente.Length > 20)
            {
                throw new CalendarValidationException("El telefono no puede exceder 20 caracteres.", nameof(CitaCreateVM.TelefonoCliente));
            }

            if (!string.IsNullOrWhiteSpace(resolvedAppointment.NombreCliente) && resolvedAppointment.NombreCliente.Length > 100)
            {
                throw new CalendarValidationException("El nombre del cliente no puede exceder 100 caracteres.", nameof(CitaCreateVM.NombreCliente));
            }

            if (request.Duplicar && request.Tipo != "CITA")
            {
                throw new CalendarValidationException("Solo las citas pueden duplicarse.", nameof(CitaCreateVM.Duplicar));
            }

            if (request.Duplicar)
            {
                foreach (var fecha in request.FechasDuplicadas)
                {
                    if (!TryParseDuplicateDate(fecha, out _))
                    {
                        throw new CalendarValidationException("Una de las fechas duplicadas no es valida.", nameof(CitaCreateVM.FechasDuplicadas));
                    }
                }
            }

            await Task.CompletedTask;
        }

        private async Task<FuncionarioSnapshot> EnsureFuncionarioActivoAsync(int funcionarioId, CancellationToken cancellationToken)
        {
            var funcionario = await _context.Funcionarios
                .AsNoTracking()
                .Where(f => f.IdFuncionario == funcionarioId && f.Activo)
                .Select(f => new FuncionarioSnapshot
                {
                    IdFuncionario = f.IdFuncionario,
                    Nombre = f.Nombre,
                    ColorCalendario = f.ColorCalendario
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (funcionario is null)
            {
                throw new CalendarValidationException(
                    "El funcionario seleccionado no existe, esta inactivo o no pertenece al tenant actual.",
                    nameof(CitaCreateVM.FuncionarioId));
            }

            return funcionario;
        }

        private async Task<Servicio?> ResolveServicioAsync(
            CalendarUpsertRequest request,
            CancellationToken cancellationToken)
        {
            if (request.Tipo != "CITA" || !request.ServicioId.HasValue)
            {
                return null;
            }

            return await _context.Servicios
                .AsNoTracking()
                .Where(s => s.Id == request.ServicioId.Value && s.Activo)
                .Select(s => new Servicio
                {
                    Id = s.Id,
                    Nombre = s.Nombre,
                    DuracionMinutos = s.DuracionMinutos,
                    Precio = s.Precio,
                    Activo = s.Activo
                })
                .SingleOrDefaultAsync(cancellationToken);
        }

        private async Task<ResolvedAppointmentData> ResolveAppointmentDataAsync(
            CalendarUpsertRequest request,
            CancellationToken cancellationToken)
        {
            if (request.Tipo == "DESCANSO")
            {
                return new ResolvedAppointmentData(
                    NombreCliente: "DESCANSO",
                    TelefonoCliente: null,
                    ClienteId: null,
                    WhatsAppConsentAtCreation: false,
                    WhatsAppConsentSource: null,
                    WhatsAppConsentCapturedAtUtc: null);
            }

            if (request.ClienteId.HasValue)
            {
                var cliente = await _context.Clientes
                    .AsNoTracking()
                    .Where(current => current.Id == request.ClienteId.Value)
                    .Select(current => new ClienteSnapshot
                    {
                        Id = current.Id,
                        Nombre = current.Nombre,
                        NumeroTelefono = current.NumeroTelefono,
                        AceptaMensajesWhatsApp = current.AceptaMensajesWhatsApp
                    })
                    .SingleOrDefaultAsync(cancellationToken);

                if (cliente is null)
                {
                    throw new CalendarValidationException(
                        "El cliente seleccionado no existe o no pertenece al tenant actual.",
                        nameof(CitaCreateVM.ClienteId));
                }

                return new ResolvedAppointmentData(
                    NombreCliente: cliente.Nombre,
                    TelefonoCliente: cliente.NumeroTelefono,
                    ClienteId: cliente.Id,
                    WhatsAppConsentAtCreation: cliente.AceptaMensajesWhatsApp,
                    WhatsAppConsentSource: WhatsAppConsentSources.ClienteRegistrado,
                    WhatsAppConsentCapturedAtUtc: DateTime.UtcNow);
            }

            var consentGranted = request.WhatsAppConsentAtCreation;
            return new ResolvedAppointmentData(
                NombreCliente: request.NombreCliente,
                TelefonoCliente: request.TelefonoCliente,
                ClienteId: null,
                WhatsAppConsentAtCreation: consentGranted,
                WhatsAppConsentSource: consentGranted
                    ? WhatsAppConsentSources.CitaManual
                    : WhatsAppConsentSources.SinConsentimiento,
                WhatsAppConsentCapturedAtUtc: consentGranted
                    ? request.WhatsAppConsentCapturedAtUtc ?? DateTime.UtcNow
                    : null);
        }

        private async Task EnsureNoOverlapAsync(
            int funcionarioId,
            DateTime inicio,
            int duracionMinutos,
            int? excludeCitaId,
            CancellationToken cancellationToken)
        {
            var fin = inicio.AddMinutes(duracionMinutos);
            var candidateRangeStart = inicio.Date.AddDays(-1);

            var candidates = await _context.Citas
                .AsNoTracking()
                .Where(c =>
                    c.FuncionarioId == funcionarioId &&
                    (!excludeCitaId.HasValue || c.Id != excludeCitaId.Value) &&
                    c.FechaHoraCita < fin &&
                    c.FechaHoraCita >= candidateRangeStart)
                .Select(c => new OverlapCandidate
                {
                    FechaHoraCita = c.FechaHoraCita,
                    DuracionMinutos = c.Tipo == "DESCANSO"
                        ? (c.DuracionMinutos ?? DefaultDurationMinutes)
                        : ((c.Servicio != null ? c.Servicio.DuracionMinutos : null) ?? DefaultDurationMinutes)
                })
                .ToListAsync(cancellationToken);

            foreach (var candidate in candidates)
            {
                var candidateEnd = candidate.FechaHoraCita.AddMinutes(candidate.DuracionMinutos);
                if (inicio < candidateEnd && fin > candidate.FechaHoraCita)
                {
                    throw new CalendarValidationException("Ya existe una cita o descanso en ese horario.");
                }
            }
        }

        private static int ResolveDuracion(string tipo, int? duracionDescanso, Servicio? servicio) =>
            tipo == "DESCANSO"
                ? duracionDescanso ?? DefaultDurationMinutes
                : servicio?.DuracionMinutos ?? DefaultDurationMinutes;

        private static List<DateTime> BuildCreationTargets(CalendarUpsertRequest request)
        {
            var targets = new List<DateTime>
            {
                request.FechaHoraCita
            };

            if (!request.Duplicar || request.FechasDuplicadas.Count == 0)
            {
                return targets;
            }

            foreach (var fecha in request.FechasDuplicadas)
            {
                if (!TryParseDuplicateDate(fecha, out var parsedDate))
                {
                    throw new CalendarValidationException("Una de las fechas duplicadas no es valida.", nameof(CitaCreateVM.FechasDuplicadas));
                }

                targets.Add(new DateTime(
                    parsedDate.Year,
                    parsedDate.Month,
                    parsedDate.Day,
                    request.FechaHoraCita.Hour,
                    request.FechaHoraCita.Minute,
                    0));
            }

            return targets;
        }

        private static void EnsureNoDuplicateTargets(IReadOnlyList<DateTime> targets)
        {
            var duplicatedTarget = targets
                .GroupBy(target => target)
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicatedTarget is not null)
            {
                throw new CalendarValidationException(
                    $"La fecha duplicada {duplicatedTarget.Key:yyyy-MM-dd} genera un horario repetido en la solicitud.",
                    nameof(CitaCreateVM.FechasDuplicadas));
            }
        }

        private static CalendarAppointmentResponse BuildAppointmentResponse(
            Cita cita,
            int duracion,
            string funcionarioNombre,
            string colorCalendario,
            string? servicioNombre) =>
            new()
            {
                Id = cita.Id,
                Tipo = cita.Tipo,
                NombreCliente = cita.NombreCliente,
                TelefonoCliente = cita.TelefonoCliente,
                ClienteId = cita.ClienteId,
                FechaHoraCita = cita.FechaHoraCita,
                DuracionMinutos = duracion,
                FuncionarioId = cita.FuncionarioId,
                FuncionarioNombre = funcionarioNombre,
                ColorCalendario = colorCalendario,
                ServicioId = cita.ServicioId,
                ServicioNombre = servicioNombre,
                WhatsAppConsentAtCreation = cita.WhatsAppConsentAtCreation,
                WhatsAppConsentSource = cita.WhatsAppConsentSource,
                WhatsAppConsentCapturedAtUtc = cita.WhatsAppConsentCapturedAtUtc,
                EstadoConfirmacionWhatsApp = cita.EstadoConfirmacionWhatsApp,
                ConfirmacionWhatsAppEnviadaUtc = cita.ConfirmacionWhatsAppEnviadaUtc,
                RecordatorioWhatsAppTresHorasEnviadoUtc = cita.RecordatorioWhatsAppTresHorasEnviadoUtc
            };

        private static CalendarUpsertRequest NormalizeRequest(CalendarUpsertRequest request) =>
            new()
            {
                NombreCliente = NormalizeOptionalText(request.NombreCliente),
                TelefonoCliente = NormalizeOptionalPhone(request.TelefonoCliente),
                ClienteId = request.ClienteId.HasValue && request.ClienteId.Value > 0
                    ? request.ClienteId.Value
                    : null,
                ServicioId = request.ServicioId,
                FechaHoraCita = request.FechaHoraCita == default
                    ? default
                    : NormalizeToMinute(request.FechaHoraCita),
                FuncionarioId = request.FuncionarioId,
                Tipo = NormalizeTipo(request.Tipo),
                DuracionMinutos = request.DuracionMinutos,
                WhatsAppConsentAtCreation = request.WhatsAppConsentAtCreation,
                WhatsAppConsentSource = NormalizeOptionalText(request.WhatsAppConsentSource),
                WhatsAppConsentCapturedAtUtc = NormalizeUtcTimestamp(request.WhatsAppConsentCapturedAtUtc),
                Duplicar = request.Duplicar,
                FechasDuplicadas = request.FechasDuplicadas
                    .Where(fecha => !string.IsNullOrWhiteSpace(fecha))
                    .Select(fecha => fecha.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            };

        private static CalendarMoveRequest NormalizeMoveRequest(CalendarMoveRequest request)
        {
            if (request.FechaHoraCita == default)
            {
                throw new CalendarValidationException("Debe indicar una fecha y hora valida.");
            }

            if (request.FuncionarioId.HasValue && request.FuncionarioId.Value <= 0)
            {
                throw new CalendarValidationException("Debe seleccionar un funcionario valido.");
            }

            return new CalendarMoveRequest
            {
                FechaHoraCita = NormalizeToMinute(request.FechaHoraCita),
                FuncionarioId = request.FuncionarioId
            };
        }

        private static string NormalizeTipo(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "CITA";
            }

            return value.Trim().ToUpperInvariant();
        }

        private static string? NormalizeOptionalText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return string.Join(
                ' ',
                value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        private static string? NormalizeOptionalPhone(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static DateTime? NormalizeUtcTimestamp(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            return value.Value.Kind switch
            {
                DateTimeKind.Utc => value.Value,
                DateTimeKind.Local => value.Value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            };
        }

        private static DateTime NormalizeToMinute(DateTime value) =>
            new(
                value.Year,
                value.Month,
                value.Day,
                value.Hour,
                value.Minute,
                0);

        private static bool TryParseDuplicateDate(string value, out DateTime parsedDate) =>
            DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsedDate);

        private sealed class FuncionarioSnapshot
        {
            public int IdFuncionario { get; init; }

            public string Nombre { get; init; } = string.Empty;

            public string ColorCalendario { get; init; } = string.Empty;
        }

        private sealed class ClienteSnapshot
        {
            public int Id { get; init; }

            public string Nombre { get; init; } = string.Empty;

            public string NumeroTelefono { get; init; } = string.Empty;

            public bool AceptaMensajesWhatsApp { get; init; }
        }

        private sealed record ResolvedAppointmentData(
            string? NombreCliente,
            string? TelefonoCliente,
            int? ClienteId,
            bool WhatsAppConsentAtCreation,
            string? WhatsAppConsentSource,
            DateTime? WhatsAppConsentCapturedAtUtc);

        private sealed class OverlapCandidate
        {
            public DateTime FechaHoraCita { get; init; }

            public int DuracionMinutos { get; init; }
        }

    }
}
