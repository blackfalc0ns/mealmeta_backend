using MealMate.Application.Common.Models;
using MediatR;

namespace MealMate.Application.Common.Messaging;

/// <summary>A command that mutates state and returns no value.</summary>
public interface ICommand : IRequest<Result>, IBaseCommand;

/// <summary>A command that mutates state and returns a value.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>, IBaseCommand;

/// <summary>Marker so pipeline behaviors (transaction) can target commands only.</summary>
public interface IBaseCommand;

/// <summary>
/// Marker for commands that own their persistence: the handler (or its mutation
/// executor) commits inside its own transaction/lock, so UnitOfWorkBehavior must
/// not run a second save. A second save after reconciliation can resurrect a stale
/// tracked entity and crash the pipeline (F04/V07).
/// </summary>
public interface ISelfPersistingCommand : IBaseCommand;

/// <summary>A read-only query.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
