using Rezervatie_sali.Domain.Common;
using Rezervatie_sali.Domain.Events;
using Rezervatie_sali.Domain.Operations;
using Rezervatie_sali.Domain.States;

namespace Rezervatie_sali.Domain.Workflows;

public class AttendancePenaltyWorkflow
{
    public Result<(IReservationState State, int NewNoShowCount, UserSuspendedEvent? SuspendedEvent, UsageStatistics Stats), string> Execute(
        ConfirmedReservation confirmed, DateTime? checkedInAt, DateTime currentTime,
        int userNoShowCount, IEnumerable<IReservationState> history, int totalAvailableSlots)
    {
        return AttendancePenaltyOperations.EvaluateAttendance(confirmed, checkedInAt, currentTime)
            .Map(state =>
            {
                var penalty = AttendancePenaltyOperations.ApplyPenaltyIfNoShow(state, userNoShowCount, currentTime);
                var stats = AttendancePenaltyOperations.CalculateStatistics(history.Append(state), totalAvailableSlots);
                return (state, penalty.UpdatedNoShows, penalty.Event, stats);
            });
    }
}