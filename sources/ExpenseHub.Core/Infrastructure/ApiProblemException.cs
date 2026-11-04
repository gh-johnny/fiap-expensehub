using System;
using System.Collections.Generic;

namespace ExpenseHub.Core.Infrastructure;

internal sealed class ApiProblemException : Exception
{
    public ApiProblemException(int statusCode, string code, string message, IReadOnlyDictionary<string, object?>? extensions = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Extensions = extensions ?? new Dictionary<string, object?>();
    }

    public int StatusCode { get; }

    public string Code { get; }

    /// <summary>Gets additional sanitized ProblemDetails members, such as a persisted resource identifier.</summary>
    public IReadOnlyDictionary<string, object?> Extensions { get; }
}
