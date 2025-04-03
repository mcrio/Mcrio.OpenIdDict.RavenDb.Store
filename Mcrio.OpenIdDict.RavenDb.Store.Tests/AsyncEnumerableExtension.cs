using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores.Tests;

internal static class AsyncEnumerableExtension
{
    internal static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> enumerable)
    {
        var list = new List<T>();
        await foreach (T item in enumerable)
        {
            list.Add(item);
        }

        return list;
    }
}