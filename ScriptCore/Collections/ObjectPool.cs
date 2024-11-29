using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TornadoScript.ScriptCore.Collections
{
    /// <summary>
    /// A thread-safe object pool implementation for managing reusable game objects
    /// </summary>
    public class ObjectPool<T> : IEnumerable<T> where T : class
    {
        private readonly ConcurrentBag<T> _objects;
        private readonly Func<T> _objectGenerator;
        private readonly int _maxSize;
        private readonly Action<T> _resetAction;

        /// <summary>
        /// Gets the current count of objects in the pool
        /// </summary>
        public int Count => _objects.Count;

        /// <summary>
        /// Creates a new instance of ObjectPool
        /// </summary>
        /// <param name="objectGenerator">Function to create new objects</param>
        /// <param name="maxSize">Maximum size of the pool</param>
        /// <param name="resetAction">Optional action to reset objects when returned to pool</param>
        public ObjectPool(Func<T> objectGenerator, int maxSize, Action<T> resetAction = null)
        {
            _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
            _maxSize = maxSize > 0 ? maxSize : throw new ArgumentException("Max size must be greater than 0", nameof(maxSize));
            _resetAction = resetAction;
            _objects = new ConcurrentBag<T>();
        }

        /// <summary>
        /// Gets an object from the pool or creates a new one if pool is empty
        /// </summary>
        public T Get()
        {
            try
            {
                if (_objects.TryTake(out T item))
                {
                    Logger.Log($"Retrieved object from pool. Current size: {Count}");
                    return item;
                }

                var newItem = _objectGenerator();
                Logger.Log($"Created new object. Current pool size: {Count}");
                return newItem;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error getting object from pool: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Returns an object to the pool if there's room
        /// </summary>
        /// <param name="item">The item to return to the pool</param>
        /// <returns>True if the item was returned to the pool, false if the pool was full</returns>
        public bool Return(T item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            try
            {
                if (_objects.Count >= _maxSize)
                {
                    Logger.Log($"Pool is full ({Count}/{_maxSize}). Discarding object.");
                    return false;
                }

                _resetAction?.Invoke(item);
                _objects.Add(item);
                Logger.Log($"Returned object to pool. Current size: {Count}/{_maxSize}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error returning object to pool: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Clears all objects from the pool
        /// </summary>
        /// <param name="dispose">If true and T implements IDisposable, dispose the objects</param>
        public void Clear(bool dispose = false)
        {
            try
            {
                int count = _objects.Count;
                if (dispose)
                {
                    while (_objects.TryTake(out T item))
                    {
                        if (item is IDisposable disposable)
                        {
                            try
                            {
                                disposable.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Error disposing object in pool: {ex.Message}");
                            }
                        }
                    }
                }
                else
                {
                    while (_objects.TryTake(out _)) { }
                }

                Logger.Log($"Cleared pool. Removed {count} objects.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error clearing pool: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through the pool
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
            return _objects.GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator that iterates through the pool
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
