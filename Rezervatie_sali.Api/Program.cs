using System.Collections.Concurrent;
using System.Threading.Channels;
using Rezervatie_sali.Domain.Common;
using Rezervatie_sali.Domain.Events;
using Rezervatie_sali.Domain.Operations;
using Rezervatie_sali.Domain.States;
using Rezervatie_sali.Domain.Workflows;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<EventBus>();
builder.Services.AddSingleton<AppStore>();
builder.Services.AddSingleton<CreateReservationWorkflow>();
builder.Services.AddSingleton<ApprovalNotificationWorkflow>();
builder.Services.AddSingleton<AttendancePenaltyWorkflow>();
builder.Services.AddHostedService<WorkflowEventConsumer>();

var app = builder.Build();

// Activează servirea interfeței grafice din wwwroot/index.html
app.UseDefaultFiles();
app.UseStaticFiles();

// ================= API ENDPOINTS =================

// 1. Căutare spații și echipamente (cu filtre)
app.MapGet("/api/resources", (string? type, int? minCapacity, AppStore store) =>
{
    var list = store.Resources.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(type) && type != "ALL")
        list = list.Where(r => r.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
    if (minCapacity.HasValue)
        list = list.Where(r => r.Capacity >= minCapacity.Value);
    return Results.Ok(list);
});

// 2. Creare rezervare -> Declanșează Workflow 1 și publică în coadă
app.MapPost("/api/reservations", async (CreateReservationDto dto, AppStore store, CreateReservationWorkflow wf1, EventBus bus) =>
{
    var user = store.Users.FirstOrDefault(u => u.Id == dto.UserId);
    var res = store.Resources.FirstOrDefault(r => r.Id == dto.ResourceId);
    if (user == null || res == null) return Results.BadRequest(new { error = "Student sau resursă invalidă." });

    bool isSuspended = user.SuspendedUntil.HasValue && user.SuspendedUntil > DateTime.Now;
    int bookedToday = store.Reservations.Values
        .Where(r => r.UserId == dto.UserId && r.StartTime.Date == dto.StartTime.Date && r.Status is "CONFIRMED" or "PENDING" or "ACTIVE")
        .Sum(r => r.DurationHours);

    bool overlap = store.Reservations.Values.Any(r =>
        r.ResourceId == dto.ResourceId && r.Status is "CONFIRMED" or "PENDING" or "ACTIVE" &&
        dto.StartTime < r.StartTime.AddHours(r.DurationHours) &&
        dto.StartTime.AddHours(dto.DurationHours) > r.StartTime);

    var input = new UnvalidatedReservation(dto.UserId, dto.ResourceId, res.Type, dto.StartTime, dto.DurationHours);
    var result = wf1.Execute(input, store.NextId(), isSuspended, bookedToday, overlap, res.RequiresApproval);

    if (result is Result<(IReservationState State, ReservationCreatedEvent Event), string>.Failure fail)
        return Results.BadRequest(new { error = fail.Error });

    var ok = ((Result<(IReservationState State, ReservationCreatedEvent Event), string>.Success)result).Value;
    var record = new ReservationRecord(
        ok.Event.ReservationId, dto.UserId, user.Name, dto.ResourceId, res.Name, res.Type,
        dto.StartTime, dto.DurationHours, ok.State is PendingApprovalReservation ? "PENDING" : "CONFIRMED", ok.State);

    store.Reservations[record.Id] = record;
    store.LogEvent($"[WF1 -> COADĂ] Publicat {nameof(ReservationCreatedEvent)} (Rezervare #{record.Id}, Status: {record.Status})");
    await bus.PublishAsync(ok.Event);

    return Results.Ok(record);
});

// 3. Aprobare / Respingere bibliotecar -> Rulează Workflow 2
app.MapPost("/api/reservations/{id}/decision", async (int id, bool approve, AppStore store, ApprovalNotificationWorkflow wf2, EventBus bus) =>
{
    if (!store.Reservations.TryGetValue(id, out var rec) || rec.DomainState is not PendingApprovalReservation pending)
        return Results.BadRequest(new { error = "Rezervarea nu este în așteptarea aprobării." });

    // Executăm WF2 (simulăm timpul ca fiind T - 25 min pentru a declanșa și reminder-ul)
    var result = wf2.Execute(pending, approve, pending.StartTime.AddMinutes(-25));
    if (result is Result<(IReservationState State, string? ReminderLog, ReservationConfirmedEvent? Event), string>.Failure fail)
        return Results.BadRequest(new { error = fail.Error });

    var ok = ((Result<(IReservationState State, string? ReminderLog, ReservationConfirmedEvent? Event), string>.Success)result).Value;
    rec.DomainState = ok.State;
    rec.Status = approve ? "CONFIRMED" : "REJECTED";

    if (ok.ReminderLog != null) store.LogEvent($"[WF2] {ok.ReminderLog}");
    if (ok.Event != null)
    {
        store.LogEvent($"[WF2 -> COADĂ] Publicat {nameof(ReservationConfirmedEvent)} (Rezervare #{id})");
        await bus.PublishAsync(ok.Event);
    }
    return Results.Ok(rec);
});

// 4. Check-in student -> Rulează Workflow 3 (prezență confirmată)
app.MapPost("/api/reservations/{id}/check-in", (int id, AppStore store, AttendancePenaltyWorkflow wf3) =>
{
    if (!store.Reservations.TryGetValue(id, out var rec) || rec.DomainState is not ConfirmedReservation confirmed)
        return Results.BadRequest(new { error = "Rezervarea nu poate primi check-in (nu este confirmată sau a expirat)." });

    var user = store.Users.First(u => u.Id == rec.UserId);
    var history = store.Reservations.Values.Select(r => r.DomainState);
    var result = wf3.Execute(confirmed, confirmed.StartTime.AddMinutes(5), confirmed.StartTime.AddMinutes(5), user.NoShowCount, history, 10);

    if (result is Result<(IReservationState State, int NewNoShowCount, UserSuspendedEvent? SuspendedEvent, UsageStatistics Stats), string>.Success ok)
    {
        rec.DomainState = ok.Value.State;
        rec.Status = "ACTIVE";
        store.LogEvent($"[WF3] Check-in confirmat pentru Rezervarea #{id} ({rec.UserName}).");
        return Results.Ok(rec);
    }
    return Results.BadRequest(new { error = "Eroare la check-in." });
});

// 5. Date complete pentru interfața grafică (Dashboard)
app.MapGet("/api/dashboard", (AppStore store, AttendancePenaltyWorkflow wf3) =>
{
    var history = store.Reservations.Values.Select(r => r.DomainState);
    var stats = AttendancePenaltyOperations.CalculateStatistics(history, 10);
    return Results.Ok(new
    {
        users = store.Users,
        resources = store.Resources,
        reservations = store.Reservations.Values.OrderByDescending(r => r.Id),
        stats,
        logs = store.EventLogs.TakeLast(20).Reverse()
    });
});

app.Run();

// ================= COADA DE EVENIMENTE & CONSUMER =================

public class EventBus
{
    private readonly Channel<object> _channel = Channel.CreateUnbounded<object>();
    public ValueTask PublishAsync(object ev) => _channel.Writer.WriteAsync(ev);
    public IAsyncEnumerable<object> ConsumeAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

public class WorkflowEventConsumer(
    EventBus bus, AppStore store,
    ApprovalNotificationWorkflow wf2, AttendancePenaltyWorkflow wf3) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var ev in bus.ConsumeAllAsync(stoppingToken))
        {
            switch (ev)
            {
                // WF1 a creat o rezervare -> WF2 o preia din coadă
                case ReservationCreatedEvent created when !created.RequiresApproval:
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000, stoppingToken); // Simulăm apropierea orei (T - 30 min)
                        if (!store.Reservations.TryGetValue(created.ReservationId, out var rec)) return;
                        if (rec.DomainState is ConfirmedReservation conf)
                        {
                            var res2 = wf2.Execute(conf, null, conf.StartTime.AddMinutes(-25));
                            if (res2 is Result<(IReservationState State, string? ReminderLog, ReservationConfirmedEvent? Event), string>.Success ok2)
                            {
                                if (ok2.Value.ReminderLog != null) store.LogEvent($"[WF2] {ok2.Value.ReminderLog}");
                                if (ok2.Value.Event != null)
                                {
                                    store.LogEvent($"[WF2 -> COADĂ] Publicat {nameof(ReservationConfirmedEvent)} (#{created.ReservationId})");
                                    await bus.PublishAsync(ok2.Value.Event);
                                }
                            }
                        }
                    }, stoppingToken);
                    break;

                // WF2 a trimis reminder-ul -> WF3 pornește monitorizarea de 15 min (simulată prin 8 secunde)
                case ReservationConfirmedEvent confirmedEv:
                    _ = Task.Run(async () =>
                    {
                        store.LogEvent($"[WF3] Monitorizare prezență pornită pentru Rezervarea #{confirmedEv.ReservationId} (ai 8 secunde pentru Check-in la demo!)");
                        await Task.Delay(8000, stoppingToken);

                        if (!store.Reservations.TryGetValue(confirmedEv.ReservationId, out var rec)) return;
                        if (rec.DomainState is ConfirmedReservation conf && rec.Status == "CONFIRMED")
                        {
                            var user = store.Users.First(u => u.Id == rec.UserId);
                            var history = store.Reservations.Values.Select(r => r.DomainState);
                            var res3 = wf3.Execute(conf, null, conf.StartTime.AddMinutes(20), user.NoShowCount, history, 10);

                            if (res3 is Result<(IReservationState State, int NewNoShowCount, UserSuspendedEvent? SuspendedEvent, UsageStatistics Stats), string>.Success ok3)
                            {
                                rec.DomainState = ok3.Value.State;
                                rec.Status = "NO_SHOW";
                                user.NoShowCount = ok3.Value.NewNoShowCount;
                                store.LogEvent($"[WF3] Absență detectată (>15 min) la Rezervarea #{rec.Id}! {user.Name} are {user.NoShowCount} no-show-uri.");

                                if (ok3.Value.SuspendedEvent != null)
                                {
                                    user.SuspendedUntil = ok3.Value.SuspendedEvent.SuspendedUntil;
                                    store.LogEvent($"[WF3 -> COADĂ] Publicat {nameof(UserSuspendedEvent)} pentru {user.Name} (blocat 7 zile)!");
                                    await bus.PublishAsync(ok3.Value.SuspendedEvent);
                                }
                            }
                        }
                    }, stoppingToken);
                    break;

                // WF3 a suspendat studentul -> WF1 anulează automat rezervările lui viitoare
                case UserSuspendedEvent suspended:
                    foreach (var r in store.Reservations.Values.Where(r => r.UserId == suspended.UserId && r.Status is "CONFIRMED" or "PENDING"))
                    {
                        r.Status = "CANCELLED";
                        store.LogEvent($"[WF1 <- COADĂ] Rezervarea #{r.Id} anulată automat (cont suspendat).");
                    }
                    break;
            }
        }
    }
}

// ================= MODELE IN-MEMORY =================

public record CreateReservationDto(int UserId, int ResourceId, DateTime StartTime, int DurationHours);

public class UserItem(int id, string name, int noShowCount)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public int NoShowCount { get; set; } = noShowCount;
    public DateTime? SuspendedUntil { get; set; }
}

public record ResourceItem(int Id, string Name, string Type, int Capacity, string Equipment, bool RequiresApproval);

public class ReservationRecord(int id, int userId, string userName, int resourceId, string resourceName, string resourceType, DateTime startTime, int durationHours, string status, IReservationState domainState)
{
    public int Id { get; } = id;
    public int UserId { get; } = userId;
    public string UserName { get; } = userName;
    public int ResourceId { get; } = resourceId;
    public string ResourceName { get; } = resourceName;
    public string ResourceType { get; } = resourceType;
    public DateTime StartTime { get; } = startTime;
    public int DurationHours { get; } = durationHours;
    public string Status { get; set; } = status;
    public IReservationState DomainState { get; set; } = domainState;
}

public class AppStore
{
    public List<UserItem> Users { get; } = new()
    {
        new UserItem(1, "Andrei Popescu (are deja 2 absențe)", 2),
        new UserItem(2, "Maria Ionescu (0 absențe)", 0)
    };

    public List<ResourceItem> Resources { get; } = new()
    {
        new ResourceItem(1, "Cabină Individuală A1", "INDIVIDUAL", 1, "Priză, Lampă LED", false),
        new ResourceItem(2, "Sală Grup 101", "GROUP", 6, "Tablă, TV HDMI", false),
        new ResourceItem(3, "Sală Conferință + Proiector 4K", "EQUIPMENT", 12, "Proiector, Sistem Audio", true)
    };

    public ConcurrentDictionary<int, ReservationRecord> Reservations { get; } = new();
    public ConcurrentQueue<string> EventLogs { get; } = new();
    private int _id = 100;
    public int NextId() => Interlocked.Increment(ref _id);

    public void LogEvent(string message) => EventLogs.Enqueue($"{DateTime.Now:HH:mm:ss} | {message}");
}