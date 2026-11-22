using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.SharedKernel.Persistence;

public static class BatchDelete
{
    /// <summary>
    /// Hard-deletes every row the query matches, <see cref="DataRetentionRules.BatchSize"/> rows per statement, so a large
    /// backlog never holds long locks. Bypasses soft delete (ExecuteDelete does not go through SaveChanges).
    /// </summary>
    public static async Task<int> DeleteInBatchesAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        where T : class
    {
        var total = 0;
        while (true)
        {
            var deleted = await query.Take(DataRetentionRules.BatchSize).ExecuteDeleteAsync(cancellationToken);
            total += deleted;
            if (deleted < DataRetentionRules.BatchSize)
            {
                return total;
            }
        }
    }
}
