using Rezervatie_sali.Domain.Common;
using Rezervatie_sali.Domain.Events;
using Rezervatie_sali.Domain.States;

namespace Rezervatie_sali.Domain.Operations;

public static class ApprovalNotificationOperations
{
    // Operația 2.1: Procesare aprobare/respingere bibliotecar
    public static Result<IReservationState, string> ProcessLibrarianDecision(
        PendingApprovalReservation pending, bool isApproved, string? rejectReason = null)
    {
        if (!isApproved)
            return Result<IReservationState, string>.Ok(
                new RejectedReservation(pending.ReservationId, rejectReason ?? "Respins de bibliotecar."));

        return Result<IReservationState, string>.Ok(
            new ConfirmedReservation(pending.ReservationId, pending.UserId, pending.ResourceId, pending.StartTime, pending.DurationHours));
    }

    // Operația 2.2: Trimitere reminder cu 30 min înainte
    public static Result<(ConfirmedReservation Reservation, string ReminderMessage), string> PrepareReminder(
        ConfirmedReservation confirmed, DateTime currentTime)
    {
        if (currentTime < confirmed.StartTime.AddMinutes(-30))
            return Result<(ConfirmedReservation, string), string>.Fail("Nu a sosit încă momentul pentru reminder (T - 30 min).");

        string msg = $"[REMINDER] Student #{confirmed.UserId}, rezervarea #{confirmed.ReservationId} începe la {confirmed.StartTime:HH:mm}.";
        return Result<(ConfirmedReservation, string), string>.Ok((confirmed, msg));
    }

    // Operația 2.3: Generare eveniment către Workflow 3
    public static ReservationConfirmedEvent PublishConfirmedEvent(ConfirmedReservation confirmed) =>
        new(confirmed.ReservationId, confirmed.UserId, confirmed.ResourceId, confirmed.StartTime);
}