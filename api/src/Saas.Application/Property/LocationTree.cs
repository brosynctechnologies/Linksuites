using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Saas.Application.Common;
using Saas.Domain.Property;

namespace Saas.Application.Property;

public sealed record LocationNode(Guid Id, Guid? ParentId, LocationKind Kind, string Name, string Slug, double? Lat, double? Lng, int Position);

/// <summary>The whole location tree (a few hundred rows), cached briefly. Admin edits call <see cref="Invalidate"/>.</summary>
public sealed class LocationTree(IAppDbContext db, IMemoryCache cache)
{
    private const string Key = "property.locations";

    public sealed class Snapshot(IReadOnlyList<LocationNode> nodes)
    {
        private readonly Dictionary<Guid, LocationNode> _byId = nodes.ToDictionary(n => n.Id);
        private readonly Dictionary<string, LocationNode> _bySlug = nodes.ToDictionary(n => n.Slug);
        private readonly ILookup<Guid?, LocationNode> _children = nodes.ToLookup(n => n.ParentId);

        public IReadOnlyList<LocationNode> All => nodes;
        public LocationNode? Find(string slug) => _bySlug.GetValueOrDefault(slug.Trim().ToLowerInvariant());
        public LocationNode? Find(Guid id) => _byId.GetValueOrDefault(id);
        public IEnumerable<LocationNode> Children(Guid? parentId) => _children[parentId].OrderBy(n => n.Position).ThenBy(n => n.Name);

        /// <summary>The node and everything under it.</summary>
        public HashSet<Guid> Subtree(Guid id)
        {
            var result = new HashSet<Guid> { id };
            var queue = new Queue<Guid>([id]);
            while (queue.TryDequeue(out var next))
                foreach (var child in _children[next])
                    if (result.Add(child.Id)) queue.Enqueue(child.Id);
            return result;
        }

        /// <summary>From the state down to the node.</summary>
        public IReadOnlyList<LocationNode> Breadcrumb(Guid id)
        {
            var chain = new List<LocationNode>();
            for (var node = Find(id); node is not null && chain.Count < 10; node = node.ParentId is { } p ? Find(p) : null) chain.Add(node);
            chain.Reverse();
            return chain;
        }
    }

    public async Task<Snapshot> GetAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(Key, out Snapshot? cached) && cached is not null) return cached;
        var rows = await db.Locations.AsNoTracking().ToListAsync(ct);
        var snapshot = new Snapshot(rows.Select(l => new LocationNode(l.Id, l.ParentId, l.Kind, l.Name, l.Slug, l.Centre?.Y, l.Centre?.X, l.Position)).ToList());
        cache.Set(Key, snapshot, TimeSpan.FromMinutes(10));
        return snapshot;
    }

    public void Invalidate() => cache.Remove(Key);
}
