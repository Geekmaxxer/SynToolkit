#nullable enable
using System;
using System.Collections.Generic;

namespace SynToolkit.Utils
{
    /// <summary>A small, thread-safe LRU for immutable metadata, never live setting values.</summary>
    internal sealed class BoundedCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly object _gate = new();
        private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _entries = new();
        private readonly LinkedList<(TKey Key, TValue Value)> _usage = new();

        public BoundedCache(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        internal int Count { get { lock (_gate) return _entries.Count; } }

        public TValue GetOrAdd(TKey key, Func<TValue> factory)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var existing))
                {
                    _usage.Remove(existing);
                    _usage.AddFirst(existing);
                    return existing.Value.Value;
                }
            }

            // Windows metadata calls must not hold the cache lock.
            TValue value = factory();
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var existing)) return existing.Value.Value;
                var node = _usage.AddFirst((key, value));
                _entries.Add(key, node);
                if (_entries.Count > _capacity && _usage.Last is { } oldest)
                {
                    _entries.Remove(oldest.Value.Key);
                    _usage.RemoveLast();
                }
                return value;
            }
        }
    }
}
