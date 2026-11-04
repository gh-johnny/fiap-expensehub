using System;

namespace ExpenseHub.Core;

/// <summary>Identifies the running build by the commit stamped into the container image.</summary>
public static class BuildInfo
{
    /// <summary>The environment variable set by the Dockerfiles from the <c>GIT_SHA</c> build argument.</summary>
    public const string VersionVariable = "APP_VERSION";

    /// <summary>The value reported when no commit was stamped.</summary>
    public const string Unknown = "unknown";

    /// <summary>Gets the commit of the running build, or <see cref="Unknown"/>.</summary>
    public static string Commit { get; } = Normalize(Environment.GetEnvironmentVariable(VersionVariable));

    /// <summary>Normalizes a raw version value: blank becomes <see cref="Unknown"/>, anything else is trimmed.</summary>
    /// <param name="value">The raw value, usually read from the environment.</param>
    /// <returns>The commit identifier to report.</returns>
    public static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? Unknown : value.Trim();
}
