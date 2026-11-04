using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicFlow.BuildingBlocks.Cqrs;

/// <summary>
/// Small in-process dispatcher. The request type is only known at runtime, so each request type
/// gets one closed generic invoker, built once with reflection and then cached.
/// </summary>
public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<(Type, Type), object> Invokers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invoker = (RequestInvoker<TResponse>)Invokers.GetOrAdd(
            (request.GetType(), typeof(TResponse)),
            static key => Activator.CreateInstance(typeof(RequestInvoker<,>).MakeGenericType(key.Item1, key.Item2))!);

        return invoker.Invoke(request, services, ct);
    }

    private abstract class RequestInvoker<TResponse>
    {
        public abstract Task<TResponse> Invoke(IRequest<TResponse> request, IServiceProvider services, CancellationToken ct);
    }

    private sealed class RequestInvoker<TRequest, TResponse> : RequestInvoker<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> Invoke(IRequest<TResponse> request, IServiceProvider services, CancellationToken ct)
        {
            var typed = (TRequest)request;
            var handler = services.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

            RequestStep<TResponse> step = token => handler.Handle(typed, token);

            foreach (var behavior in services.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse())
            {
                var next = step;
                step = token => behavior.Handle(typed, next, token);
            }

            return step(ct);
        }
    }
}
