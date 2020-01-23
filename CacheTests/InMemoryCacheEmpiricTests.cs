using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Caching;
using System.Threading;
using CacheWithLockingOnCacheKey;
using Xunit;

namespace CacheTests
{
    //Classic unit tests are in InMemoryCacheTests.cs file. Here are some empiric tests because of nature of concurrency.
    public class InMemoryCacheEmpiricTests
    {
        private UInt16 _expirySeconds = 1;
        private UInt16 _cacheKeyCount = 100;
        private UInt16 _countOfThreads = 1000;
        private UInt16 _callDBCost = 100;
        private UInt16 _everyNHasCacheExipry0 = 5;

        [Fact]
        public void CompareNewImplementationWithReferenceOne()
        {
            var referenceResult = FireNReads(new ReferenceCacheImplementation());

            var locks = new LocksCollection<string, InMemoryCache.CachedObjectLock>();
            var currentResult = FireNReads(new InMemoryCache(locks));

            //I can see 13s for current and 98s for reference one
            Assert.True(currentResult.elapsedMilliseconds * 5 < referenceResult.elapsedMilliseconds,
                "The current solution should be at least 5 times quicker than the reference one");

            //Here i can see the spikes in the old solution which we can see in AppDynamics.
            //Reference solution: full time: 97,646 and the longest execution: 97,273
            //Whereas new solution: full time: 12,939 and the longest execution: 180
            Assert.True(currentResult.results.Max()*10 < referenceResult.results.Max(),
                "the longest execution in the current solution should be at least 10 times shorter than in the reference one.");

            Assert.True(locks.IsEmpty(), "There is a memory leak!");
        }

        private class Result
        {
            public long elapsedMilliseconds;
            public ConcurrentBag<long> results = new ConcurrentBag<long>();
        }

        private Result FireNReads(ICache cache)
        {
            MemoryCache.Default.Trim(100);
            Thread[] threads = new Thread[_countOfThreads];
            Result r = new Result();

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            for (UInt16 i = 0; i < _countOfThreads; ++i)
            {
                threads[i] = FireReadingFromCache(cache, (i % _cacheKeyCount).ToString()
                    , i % _everyNHasCacheExipry0 == 0 ? 0u : _expirySeconds, r.results);
            }

            foreach (var thread in threads)
            {
                thread.Join();
            }

            stopwatch.Stop();
            r.elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

            return r;
        }

        private Thread FireReadingFromCache(ICache cache, string key, uint expirySeconds, ConcurrentBag<long> r)
        {
            var t = new Thread(() =>
            {
                Stopwatch s = new Stopwatch();
                s.Start();
                cache.Get(key
                    , TimeSpan.FromSeconds(expirySeconds),
                    () =>
                    {
                        Thread.Sleep(_callDBCost);
                        return new object();
                    });
                s.Stop();
                r.Add(s.ElapsedMilliseconds);
            });
            t.Start();
            return t;
        }

        private class ReferenceCacheImplementation : ICache
        {
            public virtual T Get<T>(string key, TimeSpan expires, Func<T> populate) where T : class, new()
            {
                var cache = MemoryCache.Default;
                var cacheItem = cache.Get(key) as T;

                if (cacheItem == null)
                {
                    lock (cache)
                    {
                        cacheItem = cache.Get(key) as T;

                        if (cacheItem == null)
                        {
                            cacheItem = populate();
                            cache.Add(key, cacheItem, DateTimeOffset.Now.Add(expires));
                        }
                    }
                }

                return cacheItem;
            }
        }
    }
}
