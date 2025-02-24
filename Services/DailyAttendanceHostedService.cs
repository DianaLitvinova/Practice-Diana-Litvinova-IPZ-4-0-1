using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Diana_Litvinova_IPZ_4_0_1.Services
{
    public class DailyAttendanceHostedService : IHostedService, IDisposable
    {
        private readonly ILogger<DailyAttendanceHostedService> _logger;
        private Timer? _timer = null;
        private readonly string _connectionString;

        public DailyAttendanceHostedService(ILogger<DailyAttendanceHostedService> logger)
        {
            _logger = logger;
            _connectionString = "Host=localhost;Database=KinderGarden;Username=daily_attendance_service;Password=service_password;";
        }

        public Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Сервіс щоденної відвідуваності запущено.");
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromHours(24));
            return Task.CompletedTask;
        }

        private async void DoWork(object? state)
        {
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                using var cmd = new NpgsqlCommand("SELECT create_daily_attendance_secure()", conn);
                await cmd.ExecuteNonQueryAsync();

                _logger.LogInformation("Створено записи відвідуваності для {date}",
                    DateTime.Now.Date.ToShortDateString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Виникла помилка при створенні записів щоденної відвідуваності");
            }
        }

        public Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Сервіс щоденної відвідуваності зупиняється.");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}