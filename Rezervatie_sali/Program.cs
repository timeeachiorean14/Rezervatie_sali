using Rezervatie_sali.Domain.States;
using Rezervatie_sali.Domain.Workflows;

var wf1 = new CreateReservationWorkflow();
var wf2 = new ApprovalNotificationWorkflow();
var wf3 = new AttendancePenaltyWorkflow();

var start = new DateTime(2026, 11, 24, 10, 0, 0);

// 1. Workflow 1: Studentul #1 rezervă o sală de grup pentru 4h
var step1 = wf1.Execute(
    new UnvalidatedReservation(UserId: 1, ResourceId: 2, ResourceType: "GROUP", StartTime: start, DurationHours: 4),
    nextReservationId: 101, isUserSuspended: false, bookedHoursToday: 0, hasOverlap: false, requiresApproval: true);

step1.Match(
    onSuccess: ok1 =>
    {
        Console.WriteLine($"[WF1] Stare: {ok1.State.GetType().Name} | Event: {ok1.Event}");

        // 2. Workflow 2: Bibliotecarul aprobă și se trimite reminder-ul la 09:35
        var step2 = wf2.Execute(ok1.State, librarianApproved: true, currentTime: start.AddMinutes(-25));
        step2.Match(
            onSuccess: ok2 =>
            {
                Console.WriteLine($"[WF2] {ok2.ReminderLog} | Event: {ok2.Event}");

                // 3. Workflow 3: La 10:20 nu a făcut check-in -> al 3-lea no-show -> suspendare 7 zile!
                var confirmed = (ConfirmedReservation)ok2.State;
                var step3 = wf3.Execute(confirmed, checkedInAt: null, currentTime: start.AddMinutes(20),
                    userNoShowCount: 2, history: new List<IReservationState>(), totalAvailableSlots: 10);

                step3.Match(
                    onSuccess: ok3 => Console.WriteLine($"[WF3] Stare: {ok3.State.GetType().Name} | Suspendare: {ok3.SuspendedEvent} | Stats: {ok3.Stats}"),
                    onFailure: err => Console.WriteLine($"[WF3 Eroare] {err}"));
            },
            onFailure: err => Console.WriteLine($"[WF2 Eroare] {err}"));
    },
    onFailure: err => Console.WriteLine($"[WF1 Eroare] {err}"));