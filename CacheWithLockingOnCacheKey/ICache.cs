using System;

namespace CacheWithLockingOnCacheKey
{
    public interface ICache
    {
        T Get<T>(string key, TimeSpan expires, Func<T> populate) where T : class, new();
    }
}
