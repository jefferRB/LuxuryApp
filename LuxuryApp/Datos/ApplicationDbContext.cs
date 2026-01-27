using LuxuryApp.Models.DataBase;
using LuxuryApp.Models.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace ProyectoIdentity.Datos
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions options) : base(options) 
        {

        }
        //se agregan los modelos
        public DbSet<AppUsuario> AppUsuario { get; set; }

        public DbSet<ClientesModel> Clientes { get; set; }
        public DbSet<ClienteVisitas> ClienteVisitas { get; set; }
        public DbSet<ClienteImagenesModel> ClienteImagenes { get; set; }
    }

 

}
