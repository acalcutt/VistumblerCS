/* - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -  */
/*  MGRS / UTM Conversion Functions                                   (c) Chris Veness 2014-2016  */
/*                                                                                   MIT Licence  */
/* www.movable-type.co.uk/scripts/latlong-utm-mgrs.html                                           */
/* - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -  */
// C# port of Chris Veness's geodesy (https://github.com/chrisveness/geodesy, MIT), by way of the Java port in
// WiGLE WiFi Wardriving (net.wigle.m8b.geodesy, BSD 3-clause); the UTM → lat/lon and MGRS → UTM directions are
// ported from the original JavaScript. See THIRD-PARTY-NOTICES.md.

namespace Vistumbler.Infrastructure.Export.M8b;

/// <summary>A UTM coordinate (WGS-84), with its MGRS latitude band.</summary>
public readonly record struct Utm(int Zone, char Hemisphere, double Easting, double Northing, char LatBand)
{
    private const double FalseEasting = 500e3, FalseNorthing = 10000e3;
    private const string LatBands = "CDEFGHJKLMNPQRSTUVWXX";   // X is repeated for 80-84°N

    // WGS-84
    private const double a = 6378137.0, f = 1 / 298.257223563, k0 = 0.9996;
    private static readonly double e = Math.Sqrt(f * (2 - f));
    private static readonly double n = f / (2 - f);
    private static readonly double A;
    private static readonly double[] Alpha, Beta;   // 6th-order Krüger series, one-based

    static Utm()
    {
        double n2 = n * n, n3 = n * n2, n4 = n * n3, n5 = n * n4, n6 = n * n5;
        A = a / (1 + n) * (1 + n2 / 4 + n4 / 64 + n6 / 256);
        Alpha = new[]
        {
            double.NaN,
            n / 2 - 2 * n2 / 3 + 5 * n3 / 16 + 41 * n4 / 180 - 127 * n5 / 288 + 7891 * n6 / 37800,
            13 * n2 / 48 - 3 * n3 / 5 + 557 * n4 / 1440 + 281 * n5 / 630 - 1983433 * n6 / 1935360,
            61 * n3 / 240 - 103 * n4 / 140 + 15061 * n5 / 26880 + 167603 * n6 / 181440,
            49561 * n4 / 161280 - 179 * n5 / 168 + 6601661 * n6 / 7257600,
            34729 * n5 / 80640 - 3418889 * n6 / 1995840,
            212378941 * n6 / 319334400,
        };
        Beta = new[]
        {
            double.NaN,
            n / 2 - 2 * n2 / 3 + 37 * n3 / 96 - n4 / 360 - 81 * n5 / 512 + 96199 * n6 / 604800,
            n2 / 48 + n3 / 15 - 437 * n4 / 1440 + 46 * n5 / 105 - 1118711 * n6 / 3870720,
            17 * n3 / 480 - 37 * n4 / 840 - 209 * n5 / 4480 + 5569 * n6 / 90720,
            4397 * n4 / 161280 - 11 * n5 / 504 - 830251 * n6 / 7257600,
            4583 * n5 / 161280 - 108847 * n6 / 3991680,
            20648693 * n6 / 638668800,
        };
    }

    private static double Rad(double d) => d * Math.PI / 180;
    private static double Deg(double r) => r * 180 / Math.PI;

    /// <summary>Lat/lon → UTM (Karney 2011), with the Norway and Svalbard zone exceptions. UTM covers 80°S to 84°N.</summary>
    public static Utm FromLatLon(double lat, double lon)
    {
        if (double.IsNaN(lat) || double.IsNaN(lon) || lat < -80 || lat > 84)
            throw new ArgumentOutOfRangeException(nameof(lat), "Outside UTM limits");

        int zone = (int)Math.Floor((lon + 180) / 6) + 1;
        double lambda0 = Rad((zone - 1) * 6 - 180 + 3);
        char band = LatBands[(int)Math.Floor(lat / 8 + 10)];
        double six = Rad(6);
        if (zone == 31 && band == 'V' && lon >= 3) { zone++; lambda0 += six; }
        if (zone == 32 && band == 'X' && lon < 9) { zone--; lambda0 -= six; }
        if (zone == 32 && band == 'X' && lon >= 9) { zone++; lambda0 += six; }
        if (zone == 34 && band == 'X' && lon < 21) { zone--; lambda0 -= six; }
        if (zone == 34 && band == 'X' && lon >= 21) { zone++; lambda0 += six; }
        if (zone == 36 && band == 'X' && lon < 33) { zone--; lambda0 -= six; }
        if (zone == 36 && band == 'X' && lon >= 33) { zone++; lambda0 += six; }

        double phi = Rad(lat), lambda = Rad(lon) - lambda0;
        double cosL = Math.Cos(lambda), sinL = Math.Sin(lambda);
        double tau = Math.Tan(phi);
        double sigma = Math.Sinh(e * Math.Atanh(e * tau / Math.Sqrt(1 + tau * tau)));
        double tauP = tau * Math.Sqrt(1 + sigma * sigma) - sigma * Math.Sqrt(1 + tau * tau);
        double xiP = Math.Atan2(tauP, cosL);
        double etaP = Math.Asinh(sinL / Math.Sqrt(tauP * tauP + cosL * cosL));

        double xi = xiP, eta = etaP;
        for (int j = 1; j <= 6; j++)
        {
            xi += Alpha[j] * Math.Sin(2 * j * xiP) * Math.Cosh(2 * j * etaP);
            eta += Alpha[j] * Math.Cos(2 * j * xiP) * Math.Sinh(2 * j * etaP);
        }
        double x = k0 * A * eta + FalseEasting;
        double y = k0 * A * xi;
        if (y < 0) y += FalseNorthing;
        return new Utm(zone, lat >= 0 ? 'N' : 'S', x, y, band);
    }

    /// <summary>UTM → lat/lon (Karney 2011), from Veness's utm.js.</summary>
    public (double Lat, double Lon) ToLatLon()
    {
        double x = Easting - FalseEasting;
        double y = Hemisphere == 'S' ? Northing - FalseNorthing : Northing;
        double eta = x / (k0 * A), xi = y / (k0 * A);

        double xiP = xi, etaP = eta;
        for (int j = 1; j <= 6; j++)
        {
            xiP -= Beta[j] * Math.Sin(2 * j * xi) * Math.Cosh(2 * j * eta);
            etaP -= Beta[j] * Math.Cos(2 * j * xi) * Math.Sinh(2 * j * eta);
        }
        double sinhEtaP = Math.Sinh(etaP), sinXiP = Math.Sin(xiP), cosXiP = Math.Cos(xiP);
        double tauP = sinXiP / Math.Sqrt(sinhEtaP * sinhEtaP + cosXiP * cosXiP);

        double tau = tauP, delta;
        int guard = 0;
        do
        {
            double sigma = Math.Sinh(e * Math.Atanh(e * tau / Math.Sqrt(1 + tau * tau)));
            double tauIP = tau * Math.Sqrt(1 + sigma * sigma) - sigma * Math.Sqrt(1 + tau * tau);
            delta = (tauP - tauIP) / Math.Sqrt(1 + tauIP * tauIP)
                    * (1 + (1 - e * e) * tau * tau) / ((1 - e * e) * Math.Sqrt(1 + tau * tau));
            tau += delta;
        } while (Math.Abs(delta) > 1e-12 && ++guard < 20);

        double phi = Math.Atan(tau);
        double lambda = Math.Atan2(sinhEtaP, cosXiP);
        double lambda0 = Rad((Zone - 1) * 6 - 180 + 3);
        return (Deg(phi), Deg(lambda + lambda0));
    }
}

/// <summary>An MGRS 1 km grid square, as Magic 8 Ball files store it: "18TXM8924", 9 ASCII characters.</summary>
public readonly record struct MgrsSquare(string Text)
{
    private const string LatBands = "CDEFGHJKLMNPQRSTUVWXX";
    private static readonly string[] E100kLetters = { "ABCDEFGH", "JKLMNPQR", "STUVWXYZ" };
    private static readonly string[] N100kLetters = { "ABCDEFGHJKLMNPQRSTUV", "FGHJKLMNPQRSTUVABCDE" };

    public override string ToString() => Text;

    /// <summary>The 1 km square a point is in; null outside UTM's 80°S to 84°N.</summary>
    public static MgrsSquare? FromLatLon(double lat, double lon)
    {
        if (double.IsNaN(lat) || double.IsNaN(lon) || lat < -80 || lat > 84) return null;
        var u = Utm.FromLatLon(lat, lon);
        int col = (int)Math.Floor(u.Easting / 100e3);
        char e100k = E100kLetters[(u.Zone - 1) % 3][col - 1];
        int row = (int)(Math.Floor(u.Northing / 100e3) % 20);
        char n100k = N100kLetters[(u.Zone - 1) % 2][row];
        int eKm = (int)(u.Easting % 100e3) / 1000, nKm = (int)(u.Northing % 100e3) / 1000;
        return new MgrsSquare($"{u.Zone:00}{u.LatBand}{e100k}{n100k}{eKm:00}{nKm:00}");
    }

    /// <summary>The middle of the square, as lat/lon.</summary>
    public (double Lat, double Lon) Center() => At(500, 500);

    /// <summary>The square's corners, anticlockwise from the south-west, as lat/lon.</summary>
    public (double Lat, double Lon)[] Corners() => new[] { At(0, 0), At(1000, 0), At(1000, 1000), At(0, 1000) };

    /// <summary>A point in the square, metres east and north of its south-west corner (Veness's mgrs.toUtm, then UTM → lat/lon).</summary>
    private (double Lat, double Lon) At(double east, double north)
    {
        int zone = int.Parse(Text[..2]);
        char band = Text[2], e100k = Text[3], n100k = Text[4];
        double easting = int.Parse(Text.Substring(5, 2)) * 1000 + east;
        double northing = int.Parse(Text.Substring(7, 2)) * 1000;   // the offset is added last, so corners share the square's cycle

        char hemisphere = band >= 'N' ? 'N' : 'S';
        double e100kNum = (E100kLetters[(zone - 1) % 3].IndexOf(e100k) + 1) * 100e3;
        double n100kNum = N100kLetters[(zone - 1) % 2].IndexOf(n100k) * 100e3;

        // Northing of the bottom of the latitude band, then add 2,000 km cycles until the square is above it
        double bandLat = (LatBands.IndexOf(band) - 10) * 8;
        double nBand = Math.Floor(Utm.FromLatLon(bandLat, 3).Northing / 100e3) * 100e3;
        double n2M = 0;
        while (n2M + n100kNum + northing < nBand) n2M += 2000e3;

        return new Utm(zone, hemisphere, e100kNum + easting, n2M + n100kNum + northing + north, band).ToLatLon();
    }
}
