using System;
using System.Collections.Concurrent;
using System.Runtime.Caching;

namespace CacheWithLockingOnCacheKey
{
    //Unfortunately .GetOrAdd method in ConcurrentDictionary doesn't come from any interface and we would like to stub it for testing.
    //Hence, need for a wrapper
    public interface ILocksCollection<TKey, TValue>
    {
        TValue GetOrAdd(TKey key, TValue value);
        void TryRemove(TKey key);
        bool ContainsKey(TKey key);
        bool IsEmpty();
    }

    public class LocksCollection<TKey, TValue> : ILocksCollection<TKey, TValue>
    {
        private readonly ConcurrentDictionary<TKey, TValue> _locks = new ConcurrentDictionary<TKey, TValue>();

        public virtual TValue GetOrAdd(TKey key, TValue value)
        {
            return _locks.GetOrAdd(key, value);
        }

        public void TryRemove(TKey key)
        {
            TValue tmp;
            _locks.TryRemove(key, out tmp);
        }

        public bool ContainsKey(TKey key)
        {
            return _locks.ContainsKey(key);
        }

        public bool IsEmpty()
        {
            return _locks.Count == 0;
        }
    }

    public class InMemoryCache : ICache
    {
        private readonly ILocksCollection<string, CachedObjectLock> _cacheLocks;

        public InMemoryCache()
        {
            _cacheLocks = new LocksCollection<string, CachedObjectLock>();
        }

        public InMemoryCache(ILocksCollection<string, CachedObjectLock> cacheLocks)
        {
            _cacheLocks = cacheLocks;
        }

        public virtual bool ShouldReadFromCache(TimeSpan expires)
        {
            return expires.CompareTo(TimeSpan.Zero) > 0;//there is no reason to touch cache and locks if expiry time is <=0
        }

        public virtual T Get<T>(string key, TimeSpan expires, Func<T> populate) where T : class, new()
        {
            if (!ShouldReadFromCache(expires))
                return populate();

            var cache = MemoryCache.Default;

            var cacheItem = cache.Get(key) as T;

            if (cacheItem == null)
            {
                var cacheLock = _cacheLocks.GetOrAdd(key, new CachedObjectLock());

                lock (cacheLock)
                {
                    if (!cacheLock.finished) //hence only one of these which acquired this lock object will call db (the problem that .GetOrAdd and Lock() are two operations, thus are concurrency unsafe)
                    {
                        cacheItem = cache.Get(key) as T;

                        if (cacheItem == null)
                        {
                            try
                            {
                                cacheItem = populate();
                                cache.Add(key, cacheItem, DateTimeOffset.Now.Add(expires));
                                cacheLock.result = cacheItem;
                            }
                            catch (Exception e)
                            {
                                cacheLock.finished = true;
                                cacheLock.exception = e;
                                _cacheLocks.TryRemove(key);
                                throw;
                            }
                        }

                        cacheLock.finished = true;
                    }
                    else
                    {
                        if (cacheLock.exception != null)
                            throw cacheLock.exception;

                        cacheItem = (T)cacheLock.result;
                    }
                    _cacheLocks.TryRemove(key);
                }
            }

            return cacheItem;
        }

        public class CachedObjectLock
        {
            public bool finished;
            public object result;
            public Exception exception;
        }
    }
}
