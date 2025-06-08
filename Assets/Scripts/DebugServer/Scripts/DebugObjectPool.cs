using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DebugServer.Pooling
{
    /// <summary>
    /// 简单的对象池实现
    /// </summary>
    public class SimpleObjectPool<T> where T : class
    {
        private readonly ConcurrentQueue<T> pool = new ConcurrentQueue<T>();
        private readonly Action<T> onGet;
        private readonly Action<T> onRelease;
        private readonly int maxSize;
        private readonly Func<T> createFunc;

        /// <summary>
        /// 创建一个新的对象池
        /// </summary>
        /// <param name="createFunc">创建新对象的函数</param>
        /// <param name="onGet">获取对象时的回调</param>
        /// <param name="onRelease">释放对象时的回调</param>
        /// <param name="maxSize">池的最大大小</param>
        public SimpleObjectPool(Func<T> createFunc, Action<T> onGet = null, Action<T> onRelease = null, int maxSize = 100)
        {
            this.createFunc = createFunc ?? throw new ArgumentNullException(nameof(createFunc));
            this.onGet = onGet;
            this.onRelease = onRelease;
            this.maxSize = maxSize;
        }

        /// <summary>
        /// 从池中获取一个对象
        /// </summary>
        public T Get()
        {
            if (pool.TryDequeue(out T item))
            {
                onGet?.Invoke(item);
                return item;
            }
            return createFunc();
        }

        /// <summary>
        /// 将对象释放回池中
        /// </summary>
        public void Release(T item)
        {
            if (item == null) return;

            if (pool.Count < maxSize)
            {
                onRelease?.Invoke(item);
                pool.Enqueue(item);
            }
        }

        /// <summary>
        /// 获取池中当前的对象数量
        /// </summary>
        public int Count => pool.Count;

        /// <summary>
        /// 清空对象池
        /// </summary>
        public void Clear()
        {
            while (pool.TryDequeue(out _)) { }
        }
    }

    /// <summary>
    /// 对象池管理器，用于管理多个对象池
    /// </summary>
    public class ObjectPoolManager
    {
        private static readonly Dictionary<Type, object> pools = new Dictionary<Type, object>();

        /// <summary>
        /// 获取或创建指定类型的对象池
        /// </summary>
        public static SimpleObjectPool<T> GetPool<T>(Func<T> createFunc, Action<T> onGet = null, Action<T> onRelease = null, int maxSize = 100) where T : class
        {
            var type = typeof(T);
            if (!pools.TryGetValue(type, out object pool))
            {
                pool = new SimpleObjectPool<T>(createFunc, onGet, onRelease, maxSize);
                pools[type] = pool;
            }
            return (SimpleObjectPool<T>)pool;
        }

        /// <summary>
        /// 清空所有对象池
        /// </summary>
        public static void ClearAll()
        {
            foreach (var pool in pools.Values)
            {
                if (pool is SimpleObjectPool<object> objectPool)
                {
                    objectPool.Clear();
                }
            }
            pools.Clear();
        }
    }
} 