using Microsoft.EntityFrameworkCore;
using Point = NetTopologySuite.Geometries.Point;
using Saas.Application.Common;
using Saas.Domain.Property;

namespace Saas.Infrastructure.Persistence;

/// <summary>
/// Idempotent reference data for the property product: Nigeria's states, launch cities with their areas,
/// property types and amenities. Centres are approximate (state capitals and area middles) and only position
/// the map; admins can add or edit locations in the locations manager.
/// </summary>
public static class PropertySeed
{
    private static readonly (string Name, string Slug, double Lat, double Lng)[] States =
    [
        ("Abia State", "abia-state", 5.53, 7.49), ("Adamawa State", "adamawa-state", 9.21, 12.48), ("Akwa Ibom State", "akwa-ibom-state", 5.04, 7.91),
        ("Anambra State", "anambra-state", 6.21, 7.07), ("Bauchi State", "bauchi-state", 10.31, 9.84), ("Bayelsa State", "bayelsa-state", 4.92, 6.26),
        ("Benue State", "benue-state", 7.73, 8.52), ("Borno State", "borno-state", 11.83, 13.15), ("Cross River State", "cross-river-state", 4.95, 8.32),
        ("Delta State", "delta-state", 6.20, 6.73), ("Ebonyi State", "ebonyi-state", 6.32, 8.11), ("Edo State", "edo-state", 6.34, 5.63),
        ("Ekiti State", "ekiti-state", 7.62, 5.22), ("Enugu State", "enugu-state", 6.45, 7.51), ("Gombe State", "gombe-state", 10.29, 11.17),
        ("Imo State", "imo-state", 5.48, 7.03), ("Jigawa State", "jigawa-state", 11.76, 9.34), ("Kaduna State", "kaduna-state", 10.52, 7.44),
        ("Kano State", "kano-state", 12.00, 8.52), ("Katsina State", "katsina-state", 12.99, 7.60), ("Kebbi State", "kebbi-state", 12.45, 4.20),
        ("Kogi State", "kogi-state", 7.80, 6.74), ("Kwara State", "kwara-state", 8.50, 4.55), ("Lagos State", "lagos-state", 6.60, 3.35),
        ("Nasarawa State", "nasarawa-state", 8.49, 8.52), ("Niger State", "niger-state", 9.61, 6.56), ("Ogun State", "ogun-state", 7.15, 3.35),
        ("Ondo State", "ondo-state", 7.25, 5.19), ("Osun State", "osun-state", 7.77, 4.56), ("Oyo State", "oyo-state", 7.38, 3.94),
        ("Plateau State", "plateau-state", 9.90, 8.86), ("Rivers State", "rivers-state", 4.82, 7.03), ("Sokoto State", "sokoto-state", 13.06, 5.24),
        ("Taraba State", "taraba-state", 8.89, 11.37), ("Yobe State", "yobe-state", 11.75, 11.96), ("Zamfara State", "zamfara-state", 12.16, 6.66),
        ("Federal Capital Territory", "fct", 9.06, 7.49),
    ];

    private static readonly (string State, string Name, string Slug, double Lat, double Lng)[] Cities =
    [
        ("lagos-state", "Lagos", "lagos", 6.52, 3.38),
        ("fct", "Abuja", "abuja", 9.06, 7.49),
        ("rivers-state", "Port Harcourt", "port-harcourt", 4.82, 7.03),
    ];

    private static readonly (string Name, double Lat, double Lng)[] LagosAreas =
    [
        ("Lekki", 6.4474, 3.4720), ("Ikoyi", 6.4520, 3.4350), ("Victoria Island", 6.4281, 3.4219), ("Ajah", 6.4672, 3.5711),
        ("Yaba", 6.5095, 3.3711), ("Surulere", 6.5000, 3.3500), ("Ikeja", 6.6018, 3.3515), ("Gbagada", 6.5550, 3.3850),
        ("Magodo", 6.6170, 3.3870), ("Maryland", 6.5720, 3.3660), ("Ogudu", 6.5770, 3.3920), ("Ojodu", 6.6390, 3.3640),
        ("Festac", 6.4660, 3.2830), ("Ikorodu", 6.6194, 3.5105), ("Sangotedo", 6.4690, 3.6390), ("Chevron", 6.4400, 3.5320),
        ("Oniru", 6.4370, 3.4500), ("Banana Island", 6.4600, 3.4450),
    ];

    private static readonly string[] Types =
        ["Apartment", "Duplex", "Terrace", "Bungalow", "Detached house", "Self-contain", "Room and parlour", "Land", "Shop", "Office", "Warehouse"];

    private static readonly string[] Amenities =
        ["24-hour power", "Water supply", "Security", "Parking", "Pool", "Gym", "Serviced", "Fitted kitchen", "Air conditioning", "Wi-Fi",
         "Boys' quarters", "Elevator"];

    public static Point PointAt(double lat, double lng) => new(lng, lat) { SRID = 4326 };

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.Locations.ToDictionaryAsync(l => l.Slug, ct);
        Location Upsert(string slug, string name, LocationKind kind, Guid? parentId, double lat, double lng, int position)
        {
            if (existing.TryGetValue(slug, out var found)) return found;
            var location = new Location { Slug = slug, Name = name, Kind = kind, ParentId = parentId, Centre = PointAt(lat, lng), Position = position };
            db.Locations.Add(location);
            existing[slug] = location;
            return location;
        }

        for (var i = 0; i < States.Length; i++)
            Upsert(States[i].Slug, States[i].Name, LocationKind.State, null, States[i].Lat, States[i].Lng, i);
        for (var i = 0; i < Cities.Length; i++)
            Upsert(Cities[i].Slug, Cities[i].Name, LocationKind.City, existing[Cities[i].State].Id, Cities[i].Lat, Cities[i].Lng, i);
        var lagos = existing["lagos"];
        for (var i = 0; i < LagosAreas.Length; i++)
            Upsert($"{Slugs.From(LagosAreas[i].Name)}-lagos", LagosAreas[i].Name, LocationKind.Area, lagos.Id, LagosAreas[i].Lat, LagosAreas[i].Lng, i);

        for (var i = 0; i < Types.Length; i++)
        {
            var slug = Slugs.From(Types[i]);
            if (!await db.PropertyTypes.AnyAsync(t => t.Slug == slug, ct))
                db.PropertyTypes.Add(new PropertyType { Name = Types[i], Slug = slug, Position = i });
        }
        for (var i = 0; i < Amenities.Length; i++)
        {
            var slug = Slugs.From(Amenities[i]);
            if (!await db.Amenities.AnyAsync(a => a.Slug == slug, ct))
                db.Amenities.Add(new Amenity { Name = Amenities[i], Slug = slug, Position = i });
        }
        await db.SaveChangesAsync(ct);
    }
}
