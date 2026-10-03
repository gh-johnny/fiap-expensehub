using System;

namespace ExpenseHub.Api.Infrastructure;

internal sealed class ApiProblemException : Exception
{
    public ApiProblemException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }
}
