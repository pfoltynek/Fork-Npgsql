﻿using System;
using System.Data.Common;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization;

namespace Npgsql;

/// <summary>
/// The exception that is thrown when server-related issues occur.
/// </summary>
/// <remarks>
/// PostgreSQL errors (e.g. query SQL issues, constraint violations) are raised via
/// <see cref="PostgresException"/> which is a subclass of this class.
/// Purely Npgsql-related issues which aren't related to the server will be raised
/// via the standard CLR exceptions (e.g. ArgumentException).
/// </remarks>
[Serializable]
public class NpgsqlException : DbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NpgsqlException"/> class.
    /// </summary>
    public NpgsqlException() {}

    /// <summary>
    /// Initializes a new instance of the <see cref="NpgsqlException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a null reference (<string>Nothing</string> in Visual Basic) if no inner exception is specified.</param>
    public NpgsqlException(string? message, Exception? innerException)
        : base(message, innerException) {}

    /// <summary>
    /// Initializes a new instance of the <see cref="NpgsqlException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public NpgsqlException(string? message)
        : base(message) { }

    /// <summary>
    /// Specifies whether the exception is considered transient, that is, whether retrying the operation could
    /// succeed (e.g. a network error or a timeout).
    /// </summary>
    public override bool IsTransient
        => InnerException is IOException or SocketException or TimeoutException or NpgsqlException { IsTransient: true };

    /// <inheritdoc cref="DbException.BatchCommand"/>
    public new NpgsqlBatchCommand? BatchCommand { get; set; }

    /// <inheritdoc/>
    protected override DbBatchCommand? DbBatchCommand => BatchCommand;

    #region Serialization

    /// <summary>
    /// Initializes a new instance of the <see cref="NpgsqlException"/> class with serialized data.
    /// </summary>
    /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
    /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
    [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.")]
    protected internal NpgsqlException(SerializationInfo info, StreamingContext context) : base(info, context) {}

    #endregion
}
