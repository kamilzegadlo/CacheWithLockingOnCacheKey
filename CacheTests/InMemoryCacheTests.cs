using System;
using System.Threading;
using CacheWithLockingOnCacheKey;
using Xunit;

namespace CacheTests
{
    public class InMemoryCacheTests
    {
        private static string GenerateUniqueCacheKey()
        {
            return Guid.NewGuid().ToString();
        }

        [Fact]
        public void Cache_returns_populated_item()
        {
            // Arrange
            ICache cache = new InMemoryCache();
            var returnObj = new object();
            var key = GenerateUniqueCacheKey();

            // Act
            var cacheItem = cache.Get(key, TimeSpan.FromSeconds(1), () => returnObj);

            // Assert
            Assert.Equal(returnObj, cacheItem);
        }

        [Fact]
        public void Cache_returns_old_stored_item()
        {
            // Arrange
            ICache cache = new InMemoryCache();
            var returnObj1 = new object();
            var returnObj2 = new object();
            var key = GenerateUniqueCacheKey();
            cache.Get(key, TimeSpan.FromSeconds(1000), () => returnObj1);

            // Act
            var cacheItem = cache.Get(key, TimeSpan.FromTicks(100), () => returnObj2);

            // Assert
            Assert.Equal(returnObj1, cacheItem);
        }

        [Fact]
        public void Cache_returns_newly_stored_item_after_previous_item_expired()
        {
            // Arrange
            ICache cache = new InMemoryCacheAllow0Expiry();
            var obj1 = new object();
            var obj2 = new object();
            var key = GenerateUniqueCacheKey();
            cache.Get(key, TimeSpan.FromSeconds(0), () => obj1);

            // Act
            var cacheItem = cache.Get(key, TimeSpan.FromTicks(100), () => obj2);

            // Assert
            Assert.Equal(obj2, cacheItem);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-100)]
        public void IfCacheDurationIsOrBelow0_DontInvoleCache(int expPeriod)
        {
            ICache cache = new InMemoryCache();
            var obj1 = new object();
            var obj2 = new object();
            var key = GenerateUniqueCacheKey();
            cache.Get(key, TimeSpan.FromTicks(1000000), () => obj1);

            // Act
            var cacheItem = cache.Get(key, TimeSpan.FromTicks(expPeriod), () => obj2);

            // Assert
            Assert.Equal(obj2, cacheItem);
        }

        [Fact]
        public void Cache_IsNotBlockedIfAnotherKeyIsBeingRefreshed()
        {
            ICache cache = new InMemoryCache();

            var thread1CacheController = new UnitTestThreadCacheController();
            thread1CacheController.hold = true;
            var resultFromThread1 = new object();

            var thread2CacheController = new UnitTestThreadCacheController();
            var resultFromThread2 = new object();

            FireReadingFromCache(cache, thread1CacheController, resultFromThread1);

            WaitUntilThreadStartsPopulating(thread1CacheController);

            FireReadingFromCache(cache, thread2CacheController, resultFromThread2);

            EnsureReadingFinished(thread2CacheController);
            Assert.Equal(resultFromThread2, thread2CacheController.result);

            Assert.False(thread1CacheController.finished);

            thread1CacheController.hold = false;

            EnsureReadingFinished(thread1CacheController);
            Assert.Equal(resultFromThread1, thread1CacheController.result);
        }

        [Fact]
        public void Cache_IsBlockedIfTheSameKeyIsBeingRefreshed()
        {
            var locksCollection = new TestLocksCollection<string, InMemoryCache.CachedObjectLock>();
            ICache cache = new InMemoryCache(locksCollection);

            var thread1CacheController = new UnitTestThreadCacheController();
            thread1CacheController.hold = true;
            var resultFromThread1 = new object();

            var thread2CacheController = new UnitTestThreadCacheController();
            thread2CacheController.cacheKey = thread1CacheController.cacheKey;
            var resultFromThread2 = new object();

            FireReadingFromCache(cache, thread1CacheController, resultFromThread1);

            WaitUntilThreadStartsPopulating(thread1CacheController);

            FireReadingFromCache(cache, thread2CacheController, resultFromThread2);

            WaitUntilTwoThreadsPickedLockObjectsUp(locksCollection);

            Assert.False(thread2CacheController.finished);
            Assert.False(thread1CacheController.finished);

            thread1CacheController.hold = false;

            EnsureReadingFinished(thread1CacheController);

            EnsureReadingFinished(thread2CacheController);

            Assert.Equal(resultFromThread1, thread1CacheController.result);
            Assert.Equal(resultFromThread1, thread2CacheController.result);
        }

        [Fact]
        public void Cache_KeyShouldBeRemovedFromTheCollectionToAvoidMemoryLeak()
        {
            ILocksCollection<string, InMemoryCache.CachedObjectLock> testLocksCollection = new TestLocksCollection<string, InMemoryCache.CachedObjectLock>();

            ICache cache = new InMemoryCache(testLocksCollection);

            var thread1CacheController = new UnitTestThreadCacheController();
            thread1CacheController.hold = true;
            var resultFromThread1 = new object();

            FireReadingFromCache(cache, thread1CacheController, resultFromThread1);

            WaitUntilThreadStartsPopulating(thread1CacheController);

            Assert.True(testLocksCollection.ContainsKey(thread1CacheController.cacheKey));

            thread1CacheController.hold = false;

            EnsureReadingFinished(thread1CacheController);

            Assert.False(testLocksCollection.ContainsKey(thread1CacheController.cacheKey));
        }

        [Fact]
        public void Cache_KeyShouldBeRemovedFromTheCollectionToAvoidMemoryLeak_TwoThreadsOnTheSameKey()
        {
            var locksCollection = new TestLocksCollection<string, InMemoryCache.CachedObjectLock>();

            ICache cache = new InMemoryCache(locksCollection);

            var thread1CacheController = new UnitTestThreadCacheController();
            thread1CacheController.hold = true;
            var resultFromThread1 = new object();

            var thread2CacheController = new UnitTestThreadCacheController();
            thread2CacheController.cacheKey = thread1CacheController.cacheKey;
            var resultFromThread2 = new object();

            FireReadingFromCache(cache, thread1CacheController, resultFromThread1);

            WaitUntilThreadStartsPopulating(thread1CacheController);

            FireReadingFromCache(cache, thread2CacheController, resultFromThread2);

            WaitUntilTwoThreadsPickedLockObjectsUp(locksCollection);

            Assert.True(locksCollection.ContainsKey(thread1CacheController.cacheKey));

            thread1CacheController.hold = false;

            EnsureReadingFinished(thread1CacheController);
            EnsureReadingFinished(thread2CacheController);

            Assert.False(locksCollection.ContainsKey(thread1CacheController.cacheKey));
            Assert.False(thread1CacheController.exceptionThrown);
            Assert.False(thread2CacheController.exceptionThrown);
        }

        [Fact]
        public void IfExceptionThrown_LockedCallsShouldAlsoThrow_NotReturnNull()
        {
            var locksCollection = new TestLocksCollection<string, InMemoryCache.CachedObjectLock>();
            ICache cache = new InMemoryCache(locksCollection);

            var thread1CacheController = new UnitTestThreadCacheController();
            thread1CacheController.hold = true;
            thread1CacheController.throwException = true;
            var resultFromThread1 = new object();

            var thread2CacheController = new UnitTestThreadCacheController();
            thread2CacheController.cacheKey = thread1CacheController.cacheKey;
            var resultFromThread2 = new object();

            FireReadingFromCache(cache, thread1CacheController, resultFromThread1);

            WaitUntilThreadStartsPopulating(thread1CacheController);

            FireReadingFromCache(cache, thread2CacheController, resultFromThread2);

            WaitUntilTwoThreadsPickedLockObjectsUp(locksCollection);

            thread1CacheController.hold = false;

            EnsureReadingFinished(thread1CacheController);

            Assert.True(thread1CacheController.exceptionThrown);

            EnsureReadingFinished(thread2CacheController);

            Assert.True(thread2CacheController.exceptionThrown);
        }

        [Fact]
        public void SecondReadStillReturnTheResultOfFirstDespiteCacheExpiredBecauseItWasLocked()
        {
            var LocksCollection = new TestLocksCollection<string, InMemoryCache.CachedObjectLock>();
            ICache cache = new InMemoryCacheAllow0Expiry(LocksCollection);

            var thread1CacheController = new UnitTestThreadCacheController();
            thread1CacheController.hold = true;
            thread1CacheController.expirySeconds = 0;
            var resultFromThread1 = new object();

            var thread2CacheController = new UnitTestThreadCacheController();
            thread2CacheController.cacheKey = thread1CacheController.cacheKey;
            var resultFromThread2 = new object();

            FireReadingFromCache(cache, thread1CacheController, resultFromThread1);

            WaitUntilThreadStartsPopulating(thread1CacheController);

            FireReadingFromCache(cache, thread2CacheController, resultFromThread2);

            WaitUntilTwoThreadsPickedLockObjectsUp(LocksCollection);

            thread1CacheController.hold = false;

            EnsureReadingFinished(thread1CacheController);

            EnsureReadingFinished(thread2CacheController);

            Assert.Equal(resultFromThread1, thread1CacheController.result);
            Assert.Equal(resultFromThread1, thread2CacheController.result);
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private class InMemoryCacheAllow0Expiry : InMemoryCache
        {
            public InMemoryCacheAllow0Expiry() { }

            public InMemoryCacheAllow0Expiry(ILocksCollection<string, CachedObjectLock> cacheLocks) : base(cacheLocks) { }

            public override bool ShouldReadFromCache(TimeSpan expires)
            {
                return true;
            }
        }

        private class TestLocksCollection<TKey, TValue> : LocksCollection<TKey, TValue>
        {
            public int numberOfCalls;

            public override TValue GetOrAdd(TKey key, TValue value)
            {
                var v = base.GetOrAdd(key, value);
                Interlocked.Increment(ref numberOfCalls);
                return v;
            }
        }

        private class UnitTestThreadCacheController
        {
            public string cacheKey = GenerateUniqueCacheKey();
            public UInt16 expirySeconds = 1000;
            public bool hold;
            public bool startedPopulating;
            public bool finished;
            public object result;
            public bool throwException;
            public bool exceptionThrown;
        }

        private void FireReadingFromCache(ICache cache, UnitTestThreadCacheController unitTestController, object result)
        {
            new Thread(() =>
            {
                try
                {
                    unitTestController.result = cache.Get(unitTestController.cacheKey
                        , TimeSpan.FromSeconds(unitTestController.expirySeconds),
                        () =>
                        {
                            unitTestController.startedPopulating = true;

                            while (unitTestController.hold)
                            {
                                Thread.Sleep(10);
                            }

                            if (unitTestController.throwException)
                                throw new Exception("unit test");

                            return result;
                        });
                }
                catch (Exception)
                {
                    unitTestController.exceptionThrown = true;
                }

                unitTestController.finished = true;
            }).Start();
        }

        private void EnsureReadingFinished(UnitTestThreadCacheController unitTestController)
        {
            WaitUntil(() => unitTestController.finished);
        }

        private void WaitUntilThreadStartsPopulating(UnitTestThreadCacheController unitTestController)
        {
            WaitUntil(() => unitTestController.startedPopulating);
        }

        private void WaitUntilTwoThreadsPickedLockObjectsUp(TestLocksCollection<string, InMemoryCache.CachedObjectLock> dic)
        {
            WaitUntil(() => dic.numberOfCalls == 2);
        }

        private void WaitUntil(Func<bool> func)
        {
            DateTime startTime = DateTime.UtcNow;

            while (!func())
            {
                Thread.Sleep(10);
                if (DateTime.UtcNow > startTime.AddSeconds(5))
                    Assert.True(false, "Time out");
            }
        }
    }
}
