using System.Diagnostics.CodeAnalysis;

namespace Malabak.SharedKernel;

[SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed", Justification = "Generic marker interface mapping command to its response type")]
public interface ICommand<TResponse>
{
}

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<TResponse> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

[SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed", Justification = "Generic marker interface mapping query to its response type")]
public interface IQuery<TResponse>
{
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<TResponse> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
