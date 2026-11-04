using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Vistumbler.Infrastructure.Export.M8b;

/// <summary>One 1 km square a lookup landed on, with how many of the visible BSSIDs were seen there.</summary>
public sealed record M8bMatch(string Square, int Count, double Latitude, double Longitude);

/// <summary>
/// Magic 8 Ball (.m8b) files, the offline "where am I" format from WiGLE WiFi Wardriving: for each BSSID, the
/// 1 km MGRS squares it has been seen in, keyed by a 30-bit SipHash of the MAC so the file doesn't list MACs.
/// Writing follows WiGLE's MagicEightBallRunnable (BSD 3-clause, see THIRD-PARTY-NOTICES.md) so files load in
/// either app; the lookup is our own, from the format's description: hash each visible MAC, collect the squares
/// they map to, and the square most of them agree on is where you are.
/// </summary>
/// <remarks>
/// Layout: ASCII header lines "MJG", "2", "SIP-2-4", slice bits (hex), "MGRS-1000", "4", "9", record count (hex),
/// each ending in \n; then the records, 13 bytes each, sorted by key: a little-endian 32-bit key and a 9-byte
/// MGRS square such as "18SVK8924".
/// </remarks>
public sealed class M8bFile
{
    public const int SliceBits = 30;
    private const int KeySize = 4, SquareSize = 9, RecordSize = KeySize + SquareSize;
    private static readonly byte[] SipKey = new byte[16];   // all zero, as WiGLE uses

    private readonly uint[] _keys;      // sorted
    private readonly byte[] _squares;   // 9 bytes per record, in the same order as _keys

    private M8bFile(uint[] keys, byte[] squares) { _keys = keys; _squares = squares; }

    public int RecordCount => _keys.Length;

    /// <summary>The file's key for a MAC (any separators, or none); null if it isn't 12 hex digits.</summary>
    public static uint? KeyFor(string? mac)
    {
        if (string.IsNullOrEmpty(mac)) return null;
        Span<byte> bytes = stackalloc byte[6];
        int digits = 0;
        foreach (char c in mac)
        {
            int v = HexValue(c);
            if (v < 0) continue;
            if (digits >= 12) return null;
            bytes[digits / 2] = (byte)(digits % 2 == 0 ? v << 4 : bytes[digits / 2] | v);
            digits++;
        }
        if (digits != 12) return null;
        return (uint)(SipHash24.Hash(SipKey, bytes) & ((1UL << SliceBits) - 1));
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    /// <summary>
    /// Writes an m8b file from AP positions (one or more per BSSID; WiGLE uses each AP's best-signal position).
    /// Points outside UTM's 80°S to 84°N, and bad MACs, are skipped. Returns the number of records written.
    /// </summary>
    public static int Write(Stream output, IEnumerable<(string Bssid, double Latitude, double Longitude)> points)
    {
        var map = new SortedDictionary<uint, HashSet<string>>();
        int records = 0;
        foreach (var (bssid, lat, lon) in points)
        {
            if (lat == 0 && lon == 0) continue;
            var key = KeyFor(bssid);
            var square = MgrsSquare.FromLatLon(lat, lon);
            if (key is null || square is null) continue;
            if (!map.TryGetValue(key.Value, out var set)) map[key.Value] = set = new HashSet<string>();
            if (set.Add(square.Value.Text)) records++;
        }

        var header = "MJG\n2\nSIP-2-4\n" + SliceBits.ToString("x", CultureInfo.InvariantCulture) + "\nMGRS-1000\n4\n9\n"
                     + records.ToString("x", CultureInfo.InvariantCulture) + "\n";
        output.Write(Encoding.ASCII.GetBytes(header));

        Span<byte> record = stackalloc byte[RecordSize];
        foreach (var (key, squares) in map)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(record, key);
            foreach (var square in squares.Order(StringComparer.Ordinal))
            {
                Encoding.ASCII.GetBytes(square, record[KeySize..]);
                output.Write(record);
            }
        }
        return records;
    }

    /// <summary>Reads an m8b file. Throws <see cref="InvalidDataException"/> if it isn't one this app understands.</summary>
    public static M8bFile Read(Stream input)
    {
        string Line()
        {
            var sb = new StringBuilder();
            while (true)
            {
                int b = input.ReadByte();
                if (b < 0) throw new InvalidDataException("Truncated m8b header");
                if (b == '\n') return sb.ToString();
                if (sb.Length > 64) throw new InvalidDataException("Not an m8b file");
                sb.Append((char)b);
            }
        }
        static int Hex(string s) => int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v
            : throw new InvalidDataException($"Bad m8b header value '{s}'");

        if (Line() != "MJG") throw new InvalidDataException("Not an m8b file");
        if (Line() != "2") throw new InvalidDataException("Unsupported m8b version");
        if (Line() != "SIP-2-4") throw new InvalidDataException("Unsupported m8b hash");
        if (Hex(Line()) != SliceBits) throw new InvalidDataException("Unsupported m8b slice size");
        if (Line() != "MGRS-1000") throw new InvalidDataException("Unsupported m8b coordinates");
        if (Hex(Line()) != KeySize || Hex(Line()) != SquareSize) throw new InvalidDataException("Unsupported m8b record size");
        int count = Hex(Line());
        if (count < 0) throw new InvalidDataException("Bad m8b record count");

        var keys = new uint[count];
        var squares = new byte[count * SquareSize];
        var record = new byte[RecordSize];
        for (int i = 0; i < count; i++)
        {
            input.ReadExactly(record);
            keys[i] = BinaryPrimitives.ReadUInt32LittleEndian(record);
            Buffer.BlockCopy(record, KeySize, squares, i * SquareSize, SquareSize);
        }

        // Files from WiGLE and from here are sorted, but don't rely on it
        bool sorted = true;
        for (int i = 1; i < count && sorted; i++) sorted = keys[i - 1] <= keys[i];
        if (!sorted)
        {
            var order = Enumerable.Range(0, count).ToArray();
            Array.Sort(keys, order);
            var resorted = new byte[squares.Length];
            for (int i = 0; i < count; i++) Buffer.BlockCopy(squares, order[i] * SquareSize, resorted, i * SquareSize, SquareSize);
            squares = resorted;
        }
        return new M8bFile(keys, squares);
    }

    public static M8bFile Read(string path)
    {
        using var stream = new BufferedStream(File.OpenRead(path), 1 << 16);
        return Read(stream);
    }

    /// <summary>The squares a MAC has been seen in.</summary>
    public IEnumerable<string> SquaresFor(string mac)
    {
        if (KeyFor(mac) is not uint key) yield break;
        int i = Array.BinarySearch(_keys, key);
        if (i < 0) yield break;
        while (i > 0 && _keys[i - 1] == key) i--;
        for (; i < _keys.Length && _keys[i] == key; i++)
            yield return Encoding.ASCII.GetString(_squares, i * SquareSize, SquareSize);
    }

    /// <summary>
    /// Where the visible BSSIDs place you: every square any of them has been seen in, with how many of them agree,
    /// most agreed first. Searches all the given files; a BSSID counts once per square however many files list it.
    /// </summary>
    public static IReadOnlyList<M8bMatch> Locate(IEnumerable<M8bFile> files, IEnumerable<string> bssids)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var fileList = files.ToList();
        foreach (var bssid in bssids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var squares = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in fileList) squares.UnionWith(file.SquaresFor(bssid));
            foreach (var sq in squares) counts[sq] = counts.GetValueOrDefault(sq) + 1;
        }

        var matches = new List<M8bMatch>(counts.Count);
        foreach (var (sq, count) in counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            try
            {
                var (lat, lon) = new MgrsSquare(sq).Center();
                matches.Add(new M8bMatch(sq, count, lat, lon));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or IndexOutOfRangeException)
            {
                // A malformed square in someone else's file; skip it
            }
        }
        return matches;
    }
}
