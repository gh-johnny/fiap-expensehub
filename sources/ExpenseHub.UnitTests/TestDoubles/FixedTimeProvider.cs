using System;

namespace ExpenseHub.UnitTests.TestDoubles;

internal sealed class FixedTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 3, 12, 30, 0, TimeSpan.Zero);
}
