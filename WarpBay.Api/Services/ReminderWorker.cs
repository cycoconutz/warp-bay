using Microsoft.EntityFrameworkCore;
using WarpBay.Api.Data;

namespace WarpBay.Api.Services;

// Demo-friendly reminder worker: every 60s, mark due reminders Sent (logs to DB; plug SMTP/SMS in prod).
public class ReminderWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<ReminderWorker> _log;
    public ReminderWorker(IServiceProvider sp, ILogger<ReminderWorker> log) { _sp = sp; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WarpBayDb>();
                var now = DateTime.UtcNow;
                var due = await db.Reminders
                    .Where(r => r.Status == "Queued" && r.ScheduledForUtc <= now)
                    .Take(50).ToListAsync(stoppingToken);
                foreach (var r in due) { r.Status = "Sent"; r.SentAt = now; }
                if (due.Count > 0) { await db.SaveChangesAsync(stoppingToken); _log.LogInformation("Reminders sent: {n}", due.Count); }
            }
            catch (Exception ex) { _log.LogWarning(ex, "reminder sweep failed"); }
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }
}
