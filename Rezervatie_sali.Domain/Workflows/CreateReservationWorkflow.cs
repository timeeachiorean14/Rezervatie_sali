using Rezervatie_sali.Domain.Common;
using Rezervatie_sali.Domain.Events;
using Rezervatie_sali.Domain.Operations;
using Rezervatie_sali.Domain.States;

namespace Rezervatie_sali.Domain.Workflows;

public class CreateReservationWorkflow
{
    public Result<(IReservationState State, ReservationCreatedEvent Event), string> Execute(
        UnvalidatedReservation input, int nextReservationId, bool isUserSuspended,
        int bookedHoursToday, bool hasOverlap, bool requiresApproval)
    {
        return BookingOperations.ValidateRules(input, isUserSuspended, bookedHoursToday)
            .Bind(valid => BookingOperations.CheckAvailabilityAndAllocate(valid, nextReservationId, hasOverlap, requiresApproval))
            .Map(state => (state, BookingOperations.PublishCreatedEvent(state)));
    }
}