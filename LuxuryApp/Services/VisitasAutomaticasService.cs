using LuxuryApp.Models.DataBase;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services
{
    public class VisitasAutomaticasService
    {
        private readonly ApplicationDbContext _context;

        public VisitasAutomaticasService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task ProcesarCitasFinalizadas()
        {
            var ahora = DateTime.Now;

            var citas = await _context.Citas
    .Include(c => c.Servicio)
    .Where(c => !c.VisitaProcesada && c.ServicioId != null)
    .ToListAsync();

            foreach (var cita in citas)
            {
                if (cita.Servicio == null)
                    continue;
                var duracion = cita.Servicio?.DuracionMinutos ?? 30;
                var fin = cita.FechaHoraCita.AddMinutes(duracion);

                if (fin <= ahora)
                {
                    var cliente = await _context.Clientes
                        .FirstOrDefaultAsync(c =>
                            c.NumeroTelefono == cita.TelefonoCliente);

                    if (cliente == null)
                        continue;

                    // Evitar duplicados
                    bool existe = await _context.ClienteVisitas
                        .AnyAsync(v =>
                            v.NumeroTelefono == cliente.NumeroTelefono &&
                            v.FechaVisita == fin);

                    if (!existe)
                    {
                        cliente.FechaUltimaVisita = fin;

                        _context.ClienteVisitas.Add(new ClienteVisitas
                        {
                            NumeroTelefono = cliente.NumeroTelefono,
                            FechaVisita = fin
                        });
                    }

                    cita.VisitaProcesada = true;
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}