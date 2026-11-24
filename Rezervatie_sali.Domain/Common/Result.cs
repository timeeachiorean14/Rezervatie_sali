namespace Rezervatie_sali.Domain.Common;

public abstract record Result<TSuccess, TError>
{
    public sealed record Success(TSuccess Value) : Result<TSuccess, TError>;
    public sealed record Failure(TError Error) : Result<TSuccess, TError>;

    public static Result<TSuccess, TError> Ok(TSuccess value) => new Success(value);
    public static Result<TSuccess, TError> Fail(TError error) => new Failure(error);

    public Result<TNext, TError> Map<TNext>(Func<TSuccess, TNext> mapper) => this switch
    {
        Success s => Result<TNext, TError>.Ok(mapper(s.Value)),
        Failure f => Result<TNext, TError>.Fail(f.Error),
        _ => throw new InvalidOperationException()
    };

    public Result<TNext, TError> Bind<TNext>(Func<TSuccess, Result<TNext, TError>> binder) => this switch
    {
        Success s => binder(s.Value),
        Failure f => Result<TNext, TError>.Fail(f.Error),
        _ => throw new InvalidOperationException()
    };

    public void Match(Action<TSuccess> onSuccess, Action<TError> onFailure)
    {
        if (this is Success s) onSuccess(s.Value);
        else if (this is Failure f) onFailure(f.Error);
    }
}