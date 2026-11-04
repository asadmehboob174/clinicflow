namespace ClinicFlow.BuildingBlocks.Domain;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
}

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public IReadOnlyDictionary<string, string[]>? Details { get; init; }

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}

/// <summary>Lets pipeline behaviors build a failed response without knowing the value type.</summary>
public interface IResultFactory<out TSelf>
{
    static abstract TSelf FromError(Error error);
}

public class Result : IResultFactory<Result>
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);
    public static Result FromError(Error error) => new(error);

    public static implicit operator Result(Error error) => new(error);
}

public sealed class Result<T> : Result, IResultFactory<Result<T>>
{
    private readonly T? _value;

    private Result(T? value, Error? error) : base(error) => _value = value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("A failed result has no value.");

    public static new Result<T> FromError(Error error) => new(default, error);

    /// <summary>C# does not allow implicit conversions from interfaces, so lists use this.</summary>
    public static Result<T> From(T value) => new(value, null);

    public static implicit operator Result<T>(T value) => new(value, null);
    public static implicit operator Result<T>(Error error) => new(default, error);
}
