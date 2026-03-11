using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.DataBase;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.Productos;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace ProyectoIdentity.Datos
{
    public class ApplicationDbContext : IdentityDbContext<AppUsuario>
    {
        public ApplicationDbContext(DbContextOptions options) : base(options) 
        {

        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Funcionario>()
                .HasOne(f => f.Puesto)
                .WithMany(p => p.Funcionarios)
                .HasForeignKey(f => f.IdPuesto)
                .OnDelete(DeleteBehavior.Restrict); // opcional pero recomendado

            modelBuilder.Entity<Cita>()
       .HasOne(c => c.Funcionario)
       .WithMany()
       .HasForeignKey(c => c.FuncionarioId)
       .OnDelete(DeleteBehavior.Restrict);
        }



        //se agregan los modelos
        //Identity
        public DbSet<AppUsuario> AppUsuario { get; set; }
        //DataBase
        public DbSet<ClientesModel> Clientes { get; set; }
        public DbSet<ClienteVisitas> ClienteVisitas { get; set; }
        public DbSet<ClienteImagenesModel> ClienteImagenes { get; set; }
        //Calendar
       
        public DbSet<Cita> Citas { get; set; }
        
        //Finanzas
        public DbSet<Cobro> Cobros { get; set; }
        public DbSet<Servicio> Servicios { get; set; }
        public DbSet<Egreso> Egresos { get; set; }
        public DbSet<Categoria> Categorias { get; set; }
        //Productos
        public DbSet<Producto> Productos { get; set; }
        public DbSet<DetalleCobroProducto> DetalleCobroProductos { get; set; }
        public DbSet<MovimientoInventario> MovimientosInventario { get; set; }
        //Funcionarios
        public DbSet<Funcionario> Funcionarios { get; set; }
        public DbSet<Puesto> Puestos { get; set; }



    }



}
