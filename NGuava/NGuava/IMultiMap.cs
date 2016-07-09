using System.Collections.Generic;

namespace NGuava
{
    /// <summary>
    /// </summary>
    /// <typeparam name="K"></typeparam>
    /// <typeparam name="V"></typeparam>
    public interface IMultiMap<K, V>
    {
        /// <summary>
        /// </summary>
        IEnumerable<K> Keys { get; }

        /// <summary>
        /// </summary>
        int CountPairs { get; }

        /// <summary>
        /// </summary>
        void Clear();

        /// <summary>
        /// </summary>
        /// <param name="pair"></param>
        /// <returns></returns>
        bool ContainsKeyValuePair(KeyValuePair<K, V> pair);

        /// <summary>
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        bool ContainsKey(K key);

        /// <summary>
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        bool ContainsValue(V value);

        /// <summary>
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        void Add(K key, V value);

        /// <summary>
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        IEnumerable<V> Get(K key);

        /// <summary>
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        void Remove(K key, V value);

        /// <summary>
        /// </summary>
        /// <param name="key"></param>
        void RemoveAll(K key);

        ISet<V> GetSet(K key);

        /// <summary>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="A"></typeparam>
        /// <param name="map"></param>
        void AddAll(IMultiMap<K, V> map);

        //TODO : it needs to add contravariance ImultiMap here to get.
    }
}