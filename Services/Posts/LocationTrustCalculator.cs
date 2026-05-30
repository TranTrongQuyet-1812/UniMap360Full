using System;

namespace UniMap360.Services.Posts;

public static class LocationTrustCalculator
{
    private const double EarthRadiusMeters = 6371000.0;

    /// <summary>
    /// Calculates the distance in meters between two geocoordinates using the Haversine formula.
    /// </summary>
    public static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);

        var c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        return EarthRadiusMeters * c;
    }

    private static double ToRadians(double degrees) => (Math.PI / 180.0) * degrees;

    /// <summary>
    /// Determines the confidence levels and suspicious flag based on the distance.
    /// </summary>
    public static (string Confidence, bool IsSuspicious) DetermineConfidence(double distanceMeters)
    {
        if (distanceMeters <= 500.0)
        {
            return ("High", false);
        }
        if (distanceMeters <= 2000.0)
        {
            return ("Medium", false);
        }
        if (distanceMeters <= 10000.0)
        {
            return ("Low", true);
        }
        return ("VeryLow", true);
    }
}
