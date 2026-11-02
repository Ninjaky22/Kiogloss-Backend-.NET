// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Mantiene el mismo orden en que el sistema anterior mostraba algunas listas (etiquetas, imágenes...).
// Si se cambia, esas listas podrían verse en otro orden en las pantallas. Mejor no tocarlo.
// ======================================

namespace Kiogloss.Api.Compat;

/// <summary>
/// Iteration order of java.util.HashMap / HashSet. The Spring code returned several collections
/// straight from HashMap/HashSet (cart item maps, product attributes, product tags/images), so the
/// JSON order seen by the frontends is the hash-bucket order. Collections whose elements use
/// identity hash codes (truly random in Java) are ordered by id instead.
/// </summary>
public static class JavaHash
{
    /// <summary>new HashMap&lt;&gt;() / new HashSet&lt;&gt;()</summary>
    public const int DefaultCapacity = 16;

    /// <summary>java.lang.String#hashCode.</summary>
    public static int Of(string? value)
    {
        if (value is null) return 0;
        var h = 0;
        unchecked
        {
            foreach (var c in value) h = 31 * h + c;
        }
        return h;
    }

    /// <summary>java.lang.Long#hashCode.</summary>
    public static int Of(long value) => unchecked((int)(value ^ (long)((ulong)value >> 32)));

    /// <summary>Lombok @EqualsAndHashCode: result = result * 59 + (field == null ? 43 : field.hashCode()).</summary>
    public static int Lombok(params int?[] fieldHashes)
    {
        var result = 1;
        unchecked
        {
            foreach (var h in fieldHashes) result = result * 59 + (h ?? 43);
        }
        return result;
    }

    /// <summary>
    /// Initial capacity Hibernate uses for a collection it loads with <paramref name="size"/> elements
    /// (CollectionHelper#mapOfSize / setOfSize: size / 0.75 + 1).
    /// </summary>
    public static int HibernateLoadedCapacity(int size) => (int)(size / 0.75f) + 1;

    /// <summary>new HashSet&lt;&gt;(collection): max(size / .75 + 1, 16).</summary>
    public static int CopyConstructorCapacity(int size) => Math.Max((int)(size / .75f) + 1, DefaultCapacity);

    /// <summary>Table length of a HashMap created with <paramref name="initialCapacity"/> once it holds <paramref name="size"/> entries.</summary>
    public static int TableSize(int initialCapacity, int size)
    {
        var table = TableSizeFor(initialCapacity);
        while (size > table * 0.75f) table *= 2;
        return table;
    }

    /// <summary>
    /// Orders items as a java.util.HashMap/HashSet whose table has (at least) <paramref name="tableSize"/>
    /// buckets would iterate them after inserting in <paramref name="insertionOrder"/>.
    /// The table grows (doubling) whenever the size exceeds 0.75 * length, like HashMap#resize.
    /// </summary>
    public static List<T> HashOrder<T>(IEnumerable<T> insertionOrder, Func<T, int> hashCode, int tableSize)
    {
        var items = insertionOrder.ToList();
        var table = Math.Max(1, tableSize);
        while (items.Count > table * 0.75f) table *= 2;
        return items
            .Select((item, index) => (item, index, bucket: Spread(hashCode(item)) & (table - 1)))
            .OrderBy(x => x.bucket)
            .ThenBy(x => x.index)
            .Select(x => x.item)
            .ToList();
    }

    /// <summary>Order of a collection Hibernate loaded from the database (rows in result-set order).</summary>
    public static List<T> LoadedOrder<T>(IReadOnlyCollection<T> loaded, Func<T, int> hashCode) =>
        HashOrder(loaded, hashCode, TableSizeFor(HibernateLoadedCapacity(loaded.Count)));

    /// <summary>
    /// Order of a Set Hibernate loaded lazily (PersistentSet): observed to use at least the default
    /// 16 buckets, unlike the element-collection maps which are sized for the loaded rows.
    /// </summary>
    public static List<T> LoadedSetOrder<T>(IReadOnlyCollection<T> loaded, Func<T, int> hashCode) =>
        HashOrder(loaded, hashCode, TableSizeFor(CopyConstructorCapacity(loaded.Count)));

    /// <summary>Order of new HashSet&lt;&gt;(collection).</summary>
    public static List<T> CopiedOrder<T>(IReadOnlyCollection<T> source, Func<T, int> hashCode) =>
        HashOrder(source, hashCode, TableSizeFor(CopyConstructorCapacity(source.Count)));

    /// <summary>Order of a new HashMap&lt;&gt;() with String keys.</summary>
    public static List<string> DefaultMapOrder(IEnumerable<string> keys) =>
        HashOrder(keys.Distinct(), Of, DefaultCapacity);

    /// <summary>HashMap#tableSizeFor.</summary>
    public static int TableSizeFor(int capacity)
    {
        var n = 1;
        while (n < capacity) n <<= 1;
        return n;
    }

    private static int Spread(int h) => h ^ (int)((uint)h >> 16);
}
