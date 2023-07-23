﻿using TaskFlux.Core;

namespace TaskFlux.Commands;

/// <summary>
/// Базовый класс команды, которая может быть выполнена над узлом
/// </summary>
public abstract class Command
{
    protected internal Command() 
    { }
    public abstract CommandType Type { get; }
    public abstract Result Apply(INode node);
    public abstract void ApplyNoResult(INode node);
    
    public abstract void Accept(ICommandVisitor visitor);
    public abstract T Accept<T>(IReturningCommandVisitor<T> visitor);
    public abstract ValueTask AcceptAsync(IAsyncCommandVisitor visitor, CancellationToken token = default);
}