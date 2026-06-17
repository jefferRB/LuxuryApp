namespace LuxuryApp.Services.Identity
{
    /// <summary>
    /// Nombres de roles de la aplicación. Centralizados para evitar strings mágicos
    /// repartidos por controladores, seeder y servicios.
    /// </summary>
    public static class AppRoles
    {
        /// <summary>Dueño/administrador del tenant. Acceso completo a los módulos del negocio.</summary>
        public const string Administrador = "Administrador";

        /// <summary>Rol base que recibe el dueño al registrarse.</summary>
        public const string Registrado = "Registrado";

        /// <summary>
        /// Funcionario del negocio con acceso individual y limitado a su propio portal.
        /// NO debe combinarse con Administrador.
        /// </summary>
        public const string Funcionario = "Funcionario";
    }

    /// <summary>
    /// Políticas de autorización de tenant. Las usamos para separar claramente
    /// el acceso administrativo del acceso de funcionario.
    /// </summary>
    public static class AppAuthorizationPolicies
    {
        /// <summary>Solo administradores del tenant.</summary>
        public const string RequireTenantAdmin = "RequireTenantAdmin";

        /// <summary>Solo funcionarios con portal habilitado.</summary>
        public const string RequireFuncionario = "RequireFuncionario";
    }
}
