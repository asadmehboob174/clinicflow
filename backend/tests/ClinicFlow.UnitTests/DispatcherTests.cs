using ClinicFlow.BuildingBlocks.Cqrs;
using ClinicFlow.BuildingBlocks.Domain;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicFlow.UnitTests;

public class DispatcherTests
{
    public sealed record Echo(string Text) : IQuery<string>;

    public sealed class EchoHandler(List<string> trace) : IQueryHandler<Echo, string>
    {
        public Task<Result<string>> Handle(Echo request, CancellationToken ct)
        {
            trace.Add("handler");
            return Task.FromResult<Result<string>>(request.Text);
        }
    }

    public sealed class EchoValidator : AbstractValidator<Echo>
    {
        public EchoValidator() => RuleFor(e => e.Text).NotEmpty();
    }

    public sealed class Trace<TRequest, TResponse>(List<string> trace, string name) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestStep<TResponse> next, CancellationToken ct)
        {
            trace.Add($"{name} in");
            var response = await next(ct);
            trace.Add($"{name} out");
            return response;
        }
    }

    private static (IDispatcher Dispatcher, List<string> Trace) Build()
    {
        var trace = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddScoped<IRequestHandler<Echo, Result<string>>, EchoHandler>();
        services.AddScoped<IValidator<Echo>, EchoValidator>();
        services.AddScoped<IPipelineBehavior<Echo, Result<string>>>(_ => new Trace<Echo, Result<string>>(trace, "first"));
        services.AddScoped<IPipelineBehavior<Echo, Result<string>>, ValidationBehavior<Echo, Result<string>>>();
        services.AddScoped<IPipelineBehavior<Echo, Result<string>>>(_ => new Trace<Echo, Result<string>>(trace, "last"));
        return (new Dispatcher(services.BuildServiceProvider()), trace);
    }

    [Fact]
    public async Task Behaviors_wrap_the_handler_in_registration_order()
    {
        var (dispatcher, trace) = Build();

        var result = await dispatcher.Send(new Echo("hi"));

        result.Value.ShouldBe("hi");
        trace.ShouldBe(["first in", "last in", "handler", "last out", "first out"]);
    }

    [Fact]
    public async Task Failed_validation_stops_before_the_handler()
    {
        var (dispatcher, trace) = Build();

        var result = await dispatcher.Send(new Echo(""));

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Details!.ShouldContainKey("Text");
        trace.ShouldNotContain("handler");
    }
}
