using System.Data;
using LuxuryApp.Models.DataBase;
using Microsoft.Data.SqlClient;

namespace LuxuryApp.Services.DataBase
{
    public class RecordatorioService
    {
        private readonly IConfiguration _config;
        private readonly EmailService _emailService;
        private readonly string _connectionString;

        public RecordatorioService(IConfiguration config)
        {
            _config = config;
            /* _emailService = emailService;*/

            _connectionString = config.GetConnectionString("ConexionSql");
        }
        public async Task<List<ClientesModel>> ObtenerUsuariosProximos()
        {
            var usuarios = new List<ClientesModel>();

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand("ObtenerCitasProximas", connection))
            {
                command.CommandType = CommandType.StoredProcedure;

                await connection.OpenAsync();

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        usuarios.Add(new ClientesModel
                        {
                            Nombre = reader["Nombre"].ToString(),
                            CorreoElectronico = reader["CorreoElectronico"].ToString()
                        });
                    }
                }
            }

            return usuarios;
        }

        public async Task<List<ClientesModel>> ObtenerCumpleañerosHoy()
        {
            var usuarios = new List<ClientesModel>();

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(@"
        SELECT Nombre, CorreoElectronico, FechaCumpleaños
        FROM Clientes
        WHERE FechaCumpleaños IS NOT NULL
        AND DAY(FechaCumpleaños) = DAY(GETDATE())
        AND MONTH(FechaCumpleaños) = MONTH(GETDATE())", connection))
            {
                await connection.OpenAsync();

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        usuarios.Add(new ClientesModel
                        {
                            Nombre = reader["Nombre"].ToString(),
                            CorreoElectronico = reader["CorreoElectronico"].ToString(),
                            FechaCumpleaños = reader.GetDateTime(reader.GetOrdinal("FechaCumpleaños"))
                        });
                    }
                }
            }

            return usuarios;
        }
    }
}
