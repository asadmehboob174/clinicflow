using System.Diagnostics;
using ClinicFlow.BuildingBlocks.Domain;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace ClinicFlow.BuildingBlocks.Cqrs;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestStep<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();

        var response = await next(ct);

        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (response is Result { Error: { } error })
        {
            logger.LogInformation("{Request} failed with {ErrorCode} in {Elapsed:0.0} ms", name, error.Code, elapsed);
        }
        else
        {
            logger.LogDebug("{Request} finished in {Elapsed:0.0} ms", name, elapsed);
        }

        return response;
    }
}

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result, IResultFactory<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestStep<TResponse> next, CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, ct);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return await next(ct);
        }

        var details = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());

        return TResponse.FromError(
            Error.Validation("validation.failed", "One or more fields are not valid.") with { Details = details });
    }
}
