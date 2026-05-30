using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniMap360.Services.Ai
{
    public interface IAiMapToolService
    {
        Task<(double? Lat, double? Lng, string? DisplayName, string? Error)> GeocodeLocationAsync(string locationText, CancellationToken ct = default);
        Task<(List<ProximityListingItem> Items, string? Error)> SearchNearbyListingsAsync(double lat, double lng, double radiusKm, IReadOnlyCollection<string> types, CancellationToken ct = default);
    }

    public class ProximityListingItem
    {
        public int Id { get; set; }
        public string Type { get; set; } = ""; // "room" or "job"
        public string Title { get; set; } = "";
        public string Price { get; set; } = "";
        public double Lat { get; set; }
        public double Lng { get; set; }
        public string Address { get; set; } = "";
        public double DistanceKm { get; set; }
        public string DetailUrl { get; set; } = "";
    }
}
