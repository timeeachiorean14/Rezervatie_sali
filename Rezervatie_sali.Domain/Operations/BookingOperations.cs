using Rezervatie_sali.Domain.Common;
using Rezervatie_sali.Domain.Events;
using Rezervatie_sali.Domain.States;

namespace Rezervatie_sali.Domain.Operations;

public static class BookingOperations
{
    // Operația 1.1: Validare suspendare și limite orare (max 2h individual, max 4h sală grup)
    public static Result<ValidatedReservation, string> ValidateRules(
        UnvalidatedReservation input, bool isUserSuspended, int alreadyBookedHoursToday)
    {
        if (isUserSuspended)
            return Result<ValidatedReservation, string>.Fail("Contul studentului este suspendat.");

        int maxHours = input.ResourceType == "INDIVIDUAL" ? 2 : 4;
        if (input.DurationHours <= 0 || alreadyBookedHoursToday + input.DurationHours > maxHours)
            return Result<ValidatedReservation, string>.Fail($"Limita zilnică depășită (maxim {maxHours}h pentru {input.ResourceType}).");

        return Result<ValidatedReservation, string>.Ok(
            new ValidatedReservation(input.UserId, input.ResourceId, input.ResourceType, input.StartTime, input.DurationHours));
    }

    // Operația 1.2: Verificare suprapunere și alocare stare (Confirmat automat sau Așteaptă aprobare)
    public static Result<IReservationState, string> CheckAvailabilityAndAllocate(
        ValidatedReservation valid, int newReservationId, bool hasTimeOverlap, bool requiresSpecialApproval)
    {
        if (hasTimeOverlap)
            return Result<IReservationState, string>.Fail("Intervalul orar este deja ocupat.");

        if (requiresSpecialApproval || valid.DurationHours >= 4)
            return Result<IReservationState, string>.Ok(
                new PendingApprovalReservation(newReservationId, valid.UserId, valid.ResourceId, valid.StartTime, valid.DurationHours));

        return Result<IReservationState, string>.Ok(
            new ConfirmedReservation(newReservationId, valid.UserId, valid.ResourceId, valid.StartTime, valid.DurationHours));
    }

    // Operația 1.3: Generare eveniment către Workflow 2
    public static ReservationCreatedEvent PublishCreatedEvent(IReservationState state) => state switch
    {
        PendingApprovalReservation p => new ReservationCreatedEvent(p.ReservationId, p.UserId, p.ResourceId, p.StartTime, true),
        ConfirmedReservation c => new ReservationCreatedEvent(c.ReservationId, c.UserId, c.ResourceId, c.StartTime, false),
        _ => throw new InvalidOperationException("Stare invalidă pentru publicare.")
    };
}