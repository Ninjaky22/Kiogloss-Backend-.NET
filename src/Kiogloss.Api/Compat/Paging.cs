using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Kiogloss.Api.Compat;

/// <summary>org.springframework.data.domain.PageRequest (page index + size + sort).</summary>
public sealed class PageRequest
{
    public int PageNumber { get; }
    public int PageSize { get; }
    public bool Sorted { get; }

    private PageRequest(int page, int size, bool sorted)
    {
        PageNumber = page;
        PageSize = size;
        Sorted = sorted;
    }

    public long Offset => (long)PageNumber * PageSize;

    /// <summary>PageRequest.of(page, size[, sort]) with the same argument checks.</summary>
    public static PageRequest Of(int page, int size, bool sorted = false)
    {
        if (page < 0) throw new IllegalArgumentException("Page index must not be less than zero");
        if (size < 1) throw new IllegalArgumentException("Page size must not be less than one");
        return new PageRequest(page, size, sorted);
    }
}

/// <summary>JSON shape of org.springframework.data.domain.Sort.</summary>
public sealed class SortJson(bool sorted)
{
    [JsonPropertyName("sorted")] public bool Sorted { get; } = sorted;
    [JsonPropertyName("unsorted")] public bool Unsorted { get; } = !sorted;
    [JsonPropertyName("empty")] public bool Empty { get; } = !sorted;
}

/// <summary>JSON shape of org.springframework.data.domain.PageRequest.</summary>
public sealed class PageableJson(PageRequest request)
{
    [JsonPropertyName("pageNumber")] public int PageNumber { get; } = request.PageNumber;
    [JsonPropertyName("pageSize")] public int PageSize { get; } = request.PageSize;
    [JsonPropertyName("sort")] public SortJson Sort { get; } = new(request.Sorted);
    [JsonPropertyName("offset")] public long Offset { get; } = request.Offset;
    [JsonPropertyName("paged")] public bool Paged { get; } = true;
    [JsonPropertyName("unpaged")] public bool Unpaged { get; } = false;
}

/// <summary>
/// org.springframework.data.domain.PageImpl serialized "as is" (Spring Data's default DIRECT mode),
/// with the same property order the Spring backend produced.
/// </summary>
public sealed class SpringPage<T>
{
    private readonly PageRequest _request;

    public SpringPage(List<T> content, PageRequest request, long total)
    {
        _request = request;
        Content = content;
        // PageImpl constructor adjustment
        TotalElements = content.Count > 0 && request.Offset + request.PageSize > total
            ? request.Offset + content.Count
            : total;
    }

    [JsonPropertyName("content")] public List<T> Content { get; }
    [JsonPropertyName("pageable")] public PageableJson Pageable => new(_request);
    [JsonPropertyName("totalElements")] public long TotalElements { get; }
    [JsonPropertyName("totalPages")] public int TotalPages => (int)Math.Ceiling((double)TotalElements / _request.PageSize);
    [JsonPropertyName("last")] public bool Last => !(Number + 1 < TotalPages);
    [JsonPropertyName("size")] public int Size => _request.PageSize;
    [JsonPropertyName("number")] public int Number => _request.PageNumber;
    [JsonPropertyName("sort")] public SortJson Sort => new(_request.Sorted);
    [JsonPropertyName("numberOfElements")] public int NumberOfElements => Content.Count;
    [JsonPropertyName("first")] public bool First => Number == 0;
    [JsonPropertyName("empty")] public bool Empty => Content.Count == 0;

    public SpringPage<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        new(Content.Select(mapper).ToList(), _request, TotalElements);
}

public static class PagingExtensions
{
    /// <summary>
    /// Runs the content query and, like Spring Data's PageableExecutionUtils, only issues the count
    /// query when the total cannot be derived from the content.
    /// </summary>
    public static async Task<SpringPage<T>> ToSpringPageAsync<T>(this IQueryable<T> query, PageRequest request,
        CancellationToken ct = default)
    {
        var content = await query.Skip((int)request.Offset).Take(request.PageSize).ToListAsync(ct);
        long total;
        if (request.Offset == 0 && request.PageSize > content.Count)
            total = content.Count;
        else if (content.Count != 0 && request.PageSize > content.Count)
            total = request.Offset + content.Count;
        else
            total = await query.LongCountAsync(ct);
        return new SpringPage<T>(content, request, total);
    }

    /// <summary>Applies an ORDER BY on a property chosen at runtime (Sort.by(direction, property)).</summary>
    public static IOrderedQueryable<T> OrderByProperty<T, TKey>(this IQueryable<T> query, Expression<Func<T, TKey>> key,
        bool descending) =>
        descending ? query.OrderByDescending(key) : query.OrderBy(key);
}
