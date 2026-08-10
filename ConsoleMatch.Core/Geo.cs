namespace ConsoleMatch.Core;

public static class Geo
{
    private const double EarthRadiusKm = 6371.0;

    /// Great-circle distance between two coordinates, in kilometres.
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static bool IsValidLatitude(double lat) => lat is >= -90 and <= 90;

    public static bool IsValidLongitude(double lon) => lon is >= -180 and <= 180;

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
