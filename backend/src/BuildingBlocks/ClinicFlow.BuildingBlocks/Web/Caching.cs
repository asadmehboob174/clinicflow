using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace ClinicFlow.BuildingBlocks.Web;

public static class DistributedCacheExtensions
{
    public static async Task<T> GetOrCreate<T>(
        this IDistributedCache cache, string key, TimeSpan lifetime, Func<CancellationToken, Task<T>> factory, CancellationToken ct)
    {
        var cached = await cache.GetAsync(key, ct);
        if (cached is not null)
        {
            return JsonSerializer.Deserialize<T>(cached)!;
        }

        var value = await factory(ct);
        await cache.SetAsync(
            key,
            JsonSerializer.SerializeToUtf8Bytes(value),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime },
            ct);
        return value;
    }
}
