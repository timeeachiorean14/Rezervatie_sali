namespace Rezervatie_sali.Domain.Events;

public record ReservationCreatedEvent(int ReservationId, int UserId, int ResourceId, DateTime StartTime, bool RequiresApproval);
public record ReservationConfirmedEvent(int ReservationId, int UserId, int ResourceId, DateTime StartTime);
public record UserSuspendedEvent(int UserId, DateTime SuspendedUntil, string Reason);