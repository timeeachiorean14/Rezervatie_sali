namespace Rezervatie_sali.Domain.States;

public interface IReservationState { }

public record UnvalidatedReservation(int UserId, int ResourceId, string ResourceType, DateTime StartTime, int DurationHours) : IReservationState;
public record ValidatedReservation(int UserId, int ResourceId, string ResourceType, DateTime StartTime, int DurationHours) : IReservationState;
public record PendingApprovalReservation(int ReservationId, int UserId, int ResourceId, DateTime StartTime, int DurationHours) : IReservationState;
public record ConfirmedReservation(int ReservationId, int UserId, int ResourceId, DateTime StartTime, int DurationHours) : IReservationState;
public record ActiveReservation(int ReservationId, int UserId, DateTime CheckedInAt) : IReservationState;
public record NoShowReservation(int ReservationId, int UserId, int ResourceId) : IReservationState;
public record RejectedReservation(int ReservationId, string Reason) : IReservationState;