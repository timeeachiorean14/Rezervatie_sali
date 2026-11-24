using Rezervatie_sali.Domain.Common;
using Rezervatie_sali.Domain.Events;
using Rezervatie_sali.Domain.States;

namespace Rezervatie_sali.Domain.Operations;

public record UsageStatistics(int TotalReservations, int ActiveCount, int NoShowCount, double OccupancyRatePercent);

public static class AttendancePenaltyOperations
{
    // Operația 3.1: Verificare check-in sau anulare automată la > 15 min întârziere
    public static Result<IReservationState, string> EvaluateAttendance(
        ConfirmedReservation confirmed, DateTime? checkedInAt, DateTime currentTime)
    {
        if (checkedInAt.HasValue && checkedInAt.Value <= confirmed.StartTime.AddMinutes(15))
            return Result<IReservationState, string>.Ok(
                new ActiveReservation(confirmed.ReservationId, confirmed.UserId, checkedInAt.Value));

        if (currentTime > confirmed.StartTime.AddMinutes(15))
            return Result<IReservationState, string>.Ok(
                new NoShowReservation(confirmed.ReservationId, confirmed.UserId, confirmed.ResourceId));

        return Result<IReservationState, string>.Fail("Fereastra de 15 minute pentru check-in este încă deschisă.");
    }

    // Operația 3.2: Penalizare pentru absențe (3 no-show-uri = blocare 1 săptămână)
    public static (int UpdatedNoShows, DateTime? SuspendedUntil, UserSuspendedEvent? Event) ApplyPenaltyIfNoShow(
        IReservationState state, int currentNoShowCount, DateTime currentTime)
    {
        if (state is not NoShowReservation noShow)
            return (currentNoShowCount, null, null);

        int newCount = currentNoShowCount + 1;
        if (newCount >= 3)
        {
            var suspendedUntil = currentTime.AddDays(7);
            var ev = new UserSuspendedEvent(noShow.UserId, suspendedUntil, "3 absențe (no-show) acumulate.");
            return (0, suspendedUntil, ev);
        }

        return (newCount, null, null);
    }

    // Operația 3.3: Generare statistici de utilizare
    public static UsageStatistics CalculateStatistics(IEnumerable<IReservationState> allReservations, int totalAvailableSlots)
    {
        var list = allReservations.ToList();
        int active = list.OfType<ActiveReservation>().Count();
        int noShows = list.OfType<NoShowReservation>().Count();
        double rate = totalAvailableSlots == 0 ? 0 : Math.Round((double)active / totalAvailableSlots * 100, 2);

        return new UsageStatistics(list.Count, active, noShows, rate);
    }
}