using ClinicFlow.BuildingBlocks.Domain;

namespace ClinicFlow.BuildingBlocks.Cqrs;

public interface IRequest<out TResponse>;

public interface ICommand : IRequest<Result>;
public interface ICommand<TValue> : IRequest<Result<TValue>>;
public interface IQuery<TValue> : IRequest<Result<TValue>>;

public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result> where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TValue> : IRequestHandler<TCommand, Result<TValue>>
    where TCommand : ICommand<TValue>;

public interface IQueryHandler<in TQuery, TValue> : IRequestHandler<TQuery, Result<TValue>>
    where TQuery : IQuery<TValue>;

public delegate Task<TResponse> RequestStep<TResponse>(CancellationToken ct);

/// <summary>Wraps every request. Behaviors run in the order they were registered.</summary>
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestStep<TResponse> next, CancellationToken ct);
}

public interface IDispatcher
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}
