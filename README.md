# CacheWithLockingOnCacheKey
Cache wrapper around System.Runtime.Caching.MemoryCache with lock set on cache key level

There was a quite common implementation of cache in our project which unfortunately use of the same object to lock in for the whole application. 
We saw many spikes in AppDynamics and logs were pointing that threads were locked.

Additionaly many of our calls also have expiry period equal 0.

The new implementation handles both issues:
1. move locking from one object for the whole application to object per cache key
2. do not involve cache or locks if expire date <= 0.

Classic unit tests are in InMemoryCacheTests.cs file.
Some benchmark to the previous solution and empiric tests are in InMemoryEmpiricTests.cs

Encapsulation is slightly broken (e.g. public CachedObjectLock class) to improve testability of the code.
I didn't move some classes to separate files. The reason is that these classes can exists only in a scope of main classes. 
