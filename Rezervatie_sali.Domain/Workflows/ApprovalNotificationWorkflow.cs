using Rezervatie_sali.Domain.Common;
using Rezervatie_sali.Domain.Events;
using Rezervatie_sali.Domain.Operations;
using Rezervatie_sali.Domain.States;

namespace Rezervatie_sali.Domain.Workflows;

public class ApprovalNotificationWorkflow
{
    public Result<(IReservationState State, string? ReminderLog, ReservationConfirmedEvent? Event), string> Execute(
        IReservationState currentState, bool? librarianApproved, DateTime currentTime)
    {
        Result<IReservationState, string> afterDecision = currentState switch
        {
            PendingApprovalReservation p when librarianApproved.HasValue =>
                ApprovalNotificationOperations.ProcessLibrarianDecision(p, librarianApproved.Value),
            ConfirmedReservation c =>
                Result<IReservationState, string>.Ok(c),
            _ => Result<IReservationState, string>.Fail("Rezervarea așteaptă decizia bibliotecarului.")
        };

        return afterDecision.Bind(state =>
        {
            if (state is RejectedReservation rejected)
                return Result<(IReservationState, string?, ReservationConfirmedEvent?), string>.Ok((rejected, null, null));

            var confirmed = (ConfirmedReservation)state;
            return ApprovalNotificationOperations.PrepareReminder(confirmed, currentTime)
                .Map(res => (
                    (IReservationState)res.Reservation,
                    (string?)res.ReminderMessage,
                    (ReservationConfirmedEvent?)ApprovalNotificationOperations.PublishConfirmedEvent(res.Reservation)
                ));
        });
    }
}