using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.Identity;
using LuxuryApp.Services.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Funcionarios
{
    public sealed class FuncionarioPortalAccessService : IFuncionarioPortalAccessService
    {
        private static readonly Regex EmailRegex = new(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly ApplicationDbContext _context;
        private readonly UserManager<AppUsuario> _userManager;
        private readonly IFuncionarioPortalPermissionService _permissionService;
        private readonly ILogger<FuncionarioPortalAccessService> _logger;

        public FuncionarioPortalAccessService(
            ApplicationDbContext context,
            UserManager<AppUsuario> userManager,
            IFuncionarioPortalPermissionService permissionService,
            ILogger<FuncionarioPortalAccessService> logger)
        {
            _context = context;
            _userManager = userManager;
            _permissionService = permissionService;
            _logger = logger;
        }

        public async Task<FuncionarioAccesoViewModel> ObtenerEstadoAsync(
            int funcionarioId,
            CancellationToken cancellationToken = default)
        {
            // Consulta tenant-safe por el global query filter.
            var funcionario = await _context.Funcionarios
                .AsNoTracking()
                .Where(f => f.IdFuncionario == funcionarioId)
                .Select(f => new { f.IdFuncionario, f.Nombre, f.Activo, f.AppUsuarioId })
                .FirstOrDefaultAsync(cancellationToken);

            if (funcionario is null)
            {
                return new FuncionarioAccesoViewModel
                {
                    FuncionarioId = funcionarioId,
                    Estado = FuncionarioAccesoEstado.SinAcceso
                };
            }

            if (string.IsNullOrWhiteSpace(funcionario.AppUsuarioId))
            {
                return new FuncionarioAccesoViewModel
                {
                    FuncionarioId = funcionario.IdFuncionario,
                    FuncionarioNombre = funcionario.Nombre,
                    FuncionarioActivo = funcionario.Activo,
                    Estado = FuncionarioAccesoEstado.SinAcceso
                };
            }

            var cuenta = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == funcionario.AppUsuarioId)
                .Select(u => new { u.Email, u.State })
                .FirstOrDefaultAsync(cancellationToken);

            var estado = cuenta is null
                ? FuncionarioAccesoEstado.SinAcceso
                : cuenta.State
                    ? FuncionarioAccesoEstado.AccesoActivo
                    : FuncionarioAccesoEstado.AccesoBloqueado;

            return new FuncionarioAccesoViewModel
            {
                FuncionarioId = funcionario.IdFuncionario,
                FuncionarioNombre = funcionario.Nombre,
                FuncionarioActivo = funcionario.Activo,
                Estado = estado,
                Email = cuenta?.Email
            };
        }

        public async Task<FuncionarioAccesoResultado> ActivarAccesoAsync(
            int funcionarioId,
            string email,
            FuncionarioAccesoCredencialModo modo,
            string? contrasenaTemporal,
            CancellationToken cancellationToken = default)
        {
            email = (email ?? string.Empty).Trim();

            if (!EmailRegex.IsMatch(email))
            {
                return FuncionarioAccesoResultado.Falla("Ingresa un correo electrónico válido.");
            }

            // Funcionario debe existir, pertenecer al tenant actual y estar activo.
            var funcionario = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == funcionarioId, cancellationToken);

            if (funcionario is null)
            {
                return FuncionarioAccesoResultado.Falla("El funcionario no existe o no pertenece a tu negocio.");
            }

            if (!funcionario.Activo)
            {
                return FuncionarioAccesoResultado.Falla(
                    "No puedes habilitar acceso a un funcionario inactivo. Actívalo primero.");
            }

            if (!string.IsNullOrWhiteSpace(funcionario.AppUsuarioId))
            {
                return FuncionarioAccesoResultado.Falla(
                    "Este funcionario ya tiene una cuenta de acceso. Usa reenviar invitación o reactivar acceso.");
            }

            var correoEnUso = await _userManager.FindByEmailAsync(email);
            if (correoEnUso is not null)
            {
                return FuncionarioAccesoResultado.Falla(
                    "Ese correo ya está registrado en LuxuryCloud. Usa un correo diferente para el funcionario.");
            }

            string password;
            if (modo == FuncionarioAccesoCredencialModo.ContrasenaTemporal)
            {
                if (string.IsNullOrWhiteSpace(contrasenaTemporal))
                {
                    return FuncionarioAccesoResultado.Falla("Ingresa una contraseña temporal.");
                }

                password = contrasenaTemporal.Trim();
            }
            else
            {
                password = GenerarPasswordSegura();
            }

            var usuario = new AppUsuario
            {
                UserName = email,
                Email = email,
                Name = funcionario.Nombre,
                PhoneNumber = funcionario.Telefono,
                State = true,
                TenantId = funcionario.TenantId,
                FuncionarioId = funcionario.IdFuncionario
            };

            FuncionarioAccesoResultado? resultado = null;

            var executionStrategy = _context.Database.CreateExecutionStrategy();
            await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var creado = await _userManager.CreateAsync(usuario, password);
                    if (!creado.Succeeded)
                    {
                        resultado = FuncionarioAccesoResultado.Falla(
                            creado.Errors.Select(TraducirError).ToArray());
                        await transaction.RollbackAsync(cancellationToken);
                        return;
                    }

                    var rol = await _userManager.AddToRoleAsync(usuario, AppRoles.Funcionario);
                    if (!rol.Succeeded)
                    {
                        resultado = FuncionarioAccesoResultado.Falla(
                            rol.Errors.Select(error => error.Description).ToArray());
                        await transaction.RollbackAsync(cancellationToken);
                        return;
                    }

                    funcionario.AppUsuarioId = usuario.Id;
                    await _context.SaveChangesAsync(cancellationToken);

                    await transaction.CommitAsync(cancellationToken);

                    // Permisos por defecto (solo lectura) al habilitar el acceso por primera vez.
                    await _permissionService.CrearDefaultsAsync(funcionario.IdFuncionario, cancellationToken);

                    string? tokenCodificado = null;
                    if (modo == FuncionarioAccesoCredencialModo.Invitacion)
                    {
                        tokenCodificado = await GenerarTokenCodificadoAsync(usuario);
                    }

                    _logger.LogInformation(
                        "Acceso de funcionario habilitado. TenantId {TenantId}. FuncionarioId {FuncionarioId}. UserId {UserId}. Modo {Modo}.",
                        funcionario.TenantId,
                        funcionario.IdFuncionario,
                        usuario.Id,
                        modo);

                    resultado = new FuncionarioAccesoResultado
                    {
                        Exitoso = true,
                        UserId = usuario.Id,
                        Email = email,
                        NombreParaCorreo = funcionario.Nombre,
                        EnlaceTokenCodificado = tokenCodificado,
                        RequiereCorreoInvitacion = modo == FuncionarioAccesoCredencialModo.Invitacion
                    };
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            });

            return resultado
                ?? FuncionarioAccesoResultado.Falla("No fue posible habilitar el acceso. Intenta de nuevo.");
        }

        public async Task<FuncionarioAccesoResultado> DesactivarAccesoAsync(
            int funcionarioId,
            CancellationToken cancellationToken = default)
        {
            var (funcionario, usuario, error) = await ResolverCuentaAsync(funcionarioId, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            usuario!.State = false;
            var update = await _userManager.UpdateAsync(usuario);
            if (!update.Succeeded)
            {
                return FuncionarioAccesoResultado.Falla(update.Errors.Select(e => e.Description).ToArray());
            }

            // Invalida cualquier sesión activa de inmediato (SecurityStampValidator).
            await _userManager.UpdateSecurityStampAsync(usuario);

            _logger.LogInformation(
                "Acceso de funcionario desactivado. FuncionarioId {FuncionarioId}. UserId {UserId}.",
                funcionario!.IdFuncionario,
                usuario.Id);

            return new FuncionarioAccesoResultado { Exitoso = true, UserId = usuario.Id, Email = usuario.Email };
        }

        public async Task<FuncionarioAccesoResultado> ReactivarAccesoAsync(
            int funcionarioId,
            CancellationToken cancellationToken = default)
        {
            var (funcionario, usuario, error) = await ResolverCuentaAsync(funcionarioId, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            if (!funcionario!.Activo)
            {
                return FuncionarioAccesoResultado.Falla(
                    "No puedes reactivar el acceso de un funcionario inactivo. Actívalo primero.");
            }

            usuario!.State = true;
            await _userManager.SetLockoutEndDateAsync(usuario, null);
            var update = await _userManager.UpdateAsync(usuario);
            if (!update.Succeeded)
            {
                return FuncionarioAccesoResultado.Falla(update.Errors.Select(e => e.Description).ToArray());
            }

            await _userManager.UpdateSecurityStampAsync(usuario);

            _logger.LogInformation(
                "Acceso de funcionario reactivado. FuncionarioId {FuncionarioId}. UserId {UserId}.",
                funcionario.IdFuncionario,
                usuario.Id);

            return new FuncionarioAccesoResultado { Exitoso = true, UserId = usuario.Id, Email = usuario.Email };
        }

        public async Task<FuncionarioAccesoResultado> GenerarEnlaceInvitacionAsync(
            int funcionarioId,
            CancellationToken cancellationToken = default)
        {
            var (funcionario, usuario, error) = await ResolverCuentaAsync(funcionarioId, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            var tokenCodificado = await GenerarTokenCodificadoAsync(usuario!);

            _logger.LogInformation(
                "Invitación/enlace de contraseña regenerado para funcionario. FuncionarioId {FuncionarioId}. UserId {UserId}.",
                funcionario!.IdFuncionario,
                usuario!.Id);

            return new FuncionarioAccesoResultado
            {
                Exitoso = true,
                UserId = usuario.Id,
                Email = usuario.Email,
                NombreParaCorreo = funcionario.Nombre,
                EnlaceTokenCodificado = tokenCodificado,
                RequiereCorreoInvitacion = true
            };
        }

        public async Task<FuncionarioAccesoResultado> CambiarCorreoAsync(
            int funcionarioId,
            string nuevoEmail,
            CancellationToken cancellationToken = default)
        {
            nuevoEmail = (nuevoEmail ?? string.Empty).Trim();

            if (!EmailRegex.IsMatch(nuevoEmail))
            {
                return FuncionarioAccesoResultado.Falla("Ingresa un correo electrónico válido.");
            }

            var (funcionario, usuario, error) = await ResolverCuentaAsync(funcionarioId, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            if (string.Equals(usuario!.Email, nuevoEmail, StringComparison.OrdinalIgnoreCase))
            {
                return new FuncionarioAccesoResultado { Exitoso = true, UserId = usuario.Id, Email = usuario.Email };
            }

            var enUso = await _userManager.FindByEmailAsync(nuevoEmail);
            if (enUso is not null && enUso.Id != usuario.Id)
            {
                return FuncionarioAccesoResultado.Falla("Ese correo ya está registrado en LuxuryCloud.");
            }

            usuario.Email = nuevoEmail;
            usuario.UserName = nuevoEmail;
            var update = await _userManager.UpdateAsync(usuario);
            if (!update.Succeeded)
            {
                return FuncionarioAccesoResultado.Falla(update.Errors.Select(TraducirError).ToArray());
            }

            await _userManager.UpdateSecurityStampAsync(usuario);

            _logger.LogInformation(
                "Correo de acceso de funcionario actualizado. FuncionarioId {FuncionarioId}. UserId {UserId}.",
                funcionario!.IdFuncionario,
                usuario.Id);

            return new FuncionarioAccesoResultado
            {
                Exitoso = true,
                UserId = usuario.Id,
                Email = nuevoEmail,
                NombreParaCorreo = funcionario.Nombre
            };
        }

        private async Task<(Funcionario? Funcionario, AppUsuario? Usuario, FuncionarioAccesoResultado? Error)>
            ResolverCuentaAsync(int funcionarioId, CancellationToken cancellationToken)
        {
            var funcionario = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == funcionarioId, cancellationToken);

            if (funcionario is null)
            {
                return (null, null,
                    FuncionarioAccesoResultado.Falla("El funcionario no existe o no pertenece a tu negocio."));
            }

            if (string.IsNullOrWhiteSpace(funcionario.AppUsuarioId))
            {
                return (funcionario, null,
                    FuncionarioAccesoResultado.Falla("Este funcionario no tiene una cuenta de acceso."));
            }

            var usuario = await _userManager.FindByIdAsync(funcionario.AppUsuarioId);
            if (usuario is null)
            {
                return (funcionario, null,
                    FuncionarioAccesoResultado.Falla("No se encontró la cuenta de acceso del funcionario."));
            }

            // Defensa en profundidad: la cuenta debe pertenecer al mismo tenant.
            if (usuario.TenantId != funcionario.TenantId)
            {
                _logger.LogWarning(
                    "Desalineación tenant entre funcionario y cuenta. FuncionarioId {FuncionarioId}. UserId {UserId}.",
                    funcionario.IdFuncionario,
                    usuario.Id);
                return (funcionario, null,
                    FuncionarioAccesoResultado.Falla("La cuenta de acceso no es válida para este negocio."));
            }

            return (funcionario, usuario, null);
        }

        private async Task<string> GenerarTokenCodificadoAsync(AppUsuario usuario)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(usuario);
            return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        }

        private static string GenerarPasswordSegura()
        {
            // Cumple la política (8+ chars, mayúscula). Aleatoria; nunca se muestra.
            const string mayus = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string minus = "abcdefghijkmnpqrstuvwxyz";
            const string nums = "23456789";
            const string simbolos = "!@#$%*";
            const string todos = mayus + minus + nums + simbolos;

            var sb = new StringBuilder();
            sb.Append(mayus[RandomNumberGenerator.GetInt32(mayus.Length)]);
            sb.Append(minus[RandomNumberGenerator.GetInt32(minus.Length)]);
            sb.Append(nums[RandomNumberGenerator.GetInt32(nums.Length)]);
            sb.Append(simbolos[RandomNumberGenerator.GetInt32(simbolos.Length)]);

            for (var i = 0; i < 12; i++)
            {
                sb.Append(todos[RandomNumberGenerator.GetInt32(todos.Length)]);
            }

            return sb.ToString();
        }

        private static string TraducirError(IdentityError error) => error.Code switch
        {
            "DuplicateUserName" or "DuplicateEmail" =>
                "Ese correo ya está registrado en LuxuryCloud.",
            "PasswordTooShort" =>
                "La contraseña es demasiado corta.",
            "PasswordRequiresUpper" =>
                "La contraseña debe incluir al menos una letra mayúscula.",
            _ => error.Description
        };
    }
}
