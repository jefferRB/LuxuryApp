namespace LuxuryApp.Models.Funcionarios
{
    /// <summary>
    /// Permisos disponibles dentro del portal del funcionario. Strings centralizados
    /// para no repetirlos en controladores ni vistas.
    /// </summary>
    public static class FuncionarioPortalPermissions
    {
        public const string VerMiPanel = "VerMiPanel";
        public const string VerMiCalendario = "VerMiCalendario";
        public const string CrearMisCitas = "CrearMisCitas";
        public const string EditarMisCitas = "EditarMisCitas";
        public const string CancelarMisCitas = "CancelarMisCitas";
        public const string VerMisCobros = "VerMisCobros";
        public const string RegistrarMisCobros = "RegistrarMisCobros";
        public const string VerMisGanancias = "VerMisGanancias";
        public const string VerMisPagos = "VerMisPagos";

        /// <summary>Todos los permisos en orden de presentación.</summary>
        public static readonly IReadOnlyList<string> Todos = new[]
        {
            VerMiPanel,
            VerMiCalendario,
            CrearMisCitas,
            EditarMisCitas,
            CancelarMisCitas,
            VerMisCobros,
            RegistrarMisCobros,
            VerMisGanancias,
            VerMisPagos
        };

        /// <summary>
        /// Valor por defecto de cada permiso. Por defecto el funcionario es de SOLO LECTURA:
        /// ve sus secciones pero no opera (crear citas / registrar cobros = false).
        /// Un permiso sin fila en BD usa este valor, así no hay que respaldar datos viejos.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, bool> Defaults =
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [VerMiPanel] = true,
                [VerMiCalendario] = true,
                [VerMisGanancias] = true,
                [VerMisPagos] = true,
                [VerMisCobros] = true,
                [CrearMisCitas] = false,
                [EditarMisCitas] = false,
                [CancelarMisCitas] = false,
                [RegistrarMisCobros] = false
            };

        public static bool EsPermisoValido(string? permiso) =>
            !string.IsNullOrWhiteSpace(permiso) && Defaults.ContainsKey(permiso);

        public static bool DefaultDe(string permiso) =>
            Defaults.TryGetValue(permiso, out var valor) && valor;
    }

    /// <summary>Conjunto resuelto de permisos de un funcionario (defaults + overrides de BD).</summary>
    public sealed class FuncionarioPortalPermisosSet
    {
        private readonly IReadOnlyDictionary<string, bool> _valores;

        public FuncionarioPortalPermisosSet(IReadOnlyDictionary<string, bool> valores)
        {
            _valores = valores;
        }

        public bool Tiene(string permiso) =>
            _valores.TryGetValue(permiso, out var valor)
                ? valor
                : FuncionarioPortalPermissions.DefaultDe(permiso);

        public bool VerMiCalendario => Tiene(FuncionarioPortalPermissions.VerMiCalendario);
        public bool CrearMisCitas => Tiene(FuncionarioPortalPermissions.CrearMisCitas);
        public bool VerMisCobros => Tiene(FuncionarioPortalPermissions.VerMisCobros);
        public bool RegistrarMisCobros => Tiene(FuncionarioPortalPermissions.RegistrarMisCobros);
        public bool VerMisGanancias => Tiene(FuncionarioPortalPermissions.VerMisGanancias);
        public bool VerMisPagos => Tiene(FuncionarioPortalPermissions.VerMisPagos);

        public static FuncionarioPortalPermisosSet Defaults() =>
            new(FuncionarioPortalPermissions.Defaults);
    }
}
