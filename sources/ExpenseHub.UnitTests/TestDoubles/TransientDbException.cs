using System.Data.Common;

namespace ExpenseHub.UnitTests.TestDoubles;

/// <summary>Stands for a provider failure such as a locked or unavailable database.</summary>
internal sealed class TransientDbException : DbException
{
    public TransientDbException()
        : base("Simulated database is locked.")
    {
    }
}
