using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.Infrastructure.Gps;

/// <summary>
/// GPS service using serial port communication and NMEA parsing
/// </summary>
public class SerialGpsService : IGpsService
{
    private SerialPort? _serialPort;
    private bool _isConnected;
    private GpsData? _currentGpsData;
    private DateTime _lastUpdateTime;
    private CancellationTokenSource? _cancellationTokenSource;

    public event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    public event EventHandler<GpsErrorEventArgs>? GpsError;

    public GpsData? CurrentGpsData => _currentGpsData;
    public bool IsConnected => _isConnected;

    public double SecondsSinceLastUpdate =>
        _lastUpdateTime != default ? (DateTime.Now - _lastUpdateTime).TotalSeconds : 0;

    public async Task StartAsync(GpsConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (_isConnected)
            return;

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _serialPort = new SerialPort
            {
                PortName = configuration.ComPort,
                BaudRate = configuration.BaudRate,
                Parity = configuration.Parity,
                DataBits = configuration.DataBits,
                StopBits = configuration.StopBits,
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };

            _serialPort.DataReceived += SerialPort_DataReceived;
            _serialPort.Open();
            _isConnected = true;

            // Start timeout monitoring
            _ = Task.Run(() => MonitorTimeoutAsync(configuration.TimeoutSeconds, _cancellationTokenSource.Token));
        }
        catch (Exception ex)
        {
            OnGpsError(new GpsErrorEventArgs
            {
                ErrorMessage = $"Failed to open GPS port {configuration.ComPort}",
                Exception = ex
            });
            throw;
        }

        await Task.CompletedTask;
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _isConnected = false;

        if (_serialPort != null)
        {
            _serialPort.DataReceived -= SerialPort_DataReceived;
            
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            
            _serialPort.Dispose();
            _serialPort = null;
        }
    }

    public string[] GetAvailablePorts()
    {
        return SerialPort.GetPortNames();
    }

    private async Task MonitorTimeoutAsync(int timeoutSeconds, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, cancellationToken);

            if (SecondsSinceLastUpdate > timeoutSeconds && _isConnected)
            {
                OnGpsError(new GpsErrorEventArgs
                {
                    ErrorMessage = $"GPS timeout - no data received for {timeoutSeconds} seconds"
                });
                
                Stop();
                break;
            }
        }
    }

    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                return;

            string data = _serialPort.ReadLine();
            ProcessNmeaSentence(data.Trim());
        }
        catch (TimeoutException)
        {
            // Ignore timeout exceptions
        }
        catch (Exception ex)
        {
            OnGpsError(new GpsErrorEventArgs
            {
                ErrorMessage = "Error reading GPS data",
                Exception = ex
            });
        }
    }

    private void ProcessNmeaSentence(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence) || !sentence.StartsWith("$"))
            return;

        var parts = sentence.Split(',');
        if (parts.Length < 1)
            return;

        string sentenceType = parts[0];

        try
        {
            if (sentenceType == "$GPGGA" || sentenceType == "$GNGGA")
            {
                ParseGGA(parts);
            }
            else if (sentenceType == "$GPRMC" || sentenceType == "$GNRMC")
            {
                ParseRMC(parts);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error parsing NMEA sentence: {ex.Message}");
        }
    }

    private void ParseGGA(string[] parts)
    {
        // $GPGGA,hhmmss.ss,llll.ll,a,yyyyy.yy,a,x,xx,x.x,x.x,M,x.x,M,x.x,xxxx*hh
        if (parts.Length < 15)
            return;

        if (_currentGpsData == null)
            _currentGpsData = new GpsData();

        // Time
        if (!string.IsNullOrEmpty(parts[1]))
        {
            ParseGpsTime(parts[1]);
        }

        // Latitude
        if (!string.IsNullOrEmpty(parts[2]) && !string.IsNullOrEmpty(parts[3]))
        {
            _currentGpsData.Latitude = ConvertToDecimalDegrees(parts[2], parts[3]);
        }

        // Longitude
        if (!string.IsNullOrEmpty(parts[4]) && !string.IsNullOrEmpty(parts[5]))
        {
            _currentGpsData.Longitude = ConvertToDecimalDegrees(parts[4], parts[5]);
        }

        // Quality
        if (int.TryParse(parts[6], out int quality))
        {
            _currentGpsData.Quality = (GpsQuality)quality;
        }

        // Number of satellites
        if (int.TryParse(parts[7], out int satellites))
        {
            _currentGpsData.NumberOfSatellites = satellites;
        }

        // Horizontal dilution
        if (double.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out double hdop))
        {
            _currentGpsData.HorizontalDilution = hdop;
        }

        // Altitude
        if (double.TryParse(parts[9], NumberStyles.Float, CultureInfo.InvariantCulture, out double altitude))
        {
            _currentGpsData.Altitude = altitude;
        }

        UpdateTimestamp();
    }

    private void ParseRMC(string[] parts)
    {
        // $GPRMC,hhmmss.ss,A,llll.ll,a,yyyyy.yy,a,x.x,x.x,ddmmyy,x.x,a*hh
        if (parts.Length < 12)
            return;

        if (_currentGpsData == null)
            _currentGpsData = new GpsData();

        // Time
        if (!string.IsNullOrEmpty(parts[1]))
        {
            ParseGpsTime(parts[1]);
        }

        // Status (A = valid, V = invalid)
        bool isValid = parts[2] == "A";

        if (isValid)
        {
            // Latitude
            if (!string.IsNullOrEmpty(parts[3]) && !string.IsNullOrEmpty(parts[4]))
            {
                _currentGpsData.Latitude = ConvertToDecimalDegrees(parts[3], parts[4]);
            }

            // Longitude
            if (!string.IsNullOrEmpty(parts[5]) && !string.IsNullOrEmpty(parts[6]))
            {
                _currentGpsData.Longitude = ConvertToDecimalDegrees(parts[5], parts[6]);
            }

            // Speed in knots
            if (double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out double speed))
            {
                _currentGpsData.SpeedKnots = speed;
            }

            // Track angle
            if (double.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out double track))
            {
                _currentGpsData.TrackAngle = track;
            }

            // Date
            if (!string.IsNullOrEmpty(parts[9]))
            {
                ParseGpsDate(parts[9]);
            }

            UpdateTimestamp();
        }
    }

    private void ParseGpsTime(string timeStr)
    {
        // hhmmss or hhmmss.ss
        if (timeStr.Length >= 6)
        {
            // Extract time components
            // This would be combined with date for full timestamp
        }
    }

    private void ParseGpsDate(string dateStr)
    {
        // ddmmyy
        if (dateStr.Length >= 6)
        {
            // Extract date components
            // This would be combined with time for full timestamp
        }
    }

    private double ConvertToDecimalDegrees(string coordinate, string direction)
    {
        // Format: ddmm.mmmm (latitude) or dddmm.mmmm (longitude)
        if (string.IsNullOrEmpty(coordinate) || coordinate.Length < 4)
            return 0;

        // Find the decimal point
        int decimalIndex = coordinate.IndexOf('.');
        if (decimalIndex < 3)
            return 0;

        // Latitude: first 2 chars are degrees, rest are minutes
        // Longitude: first 3 chars are degrees, rest are minutes
        int degreeLength = decimalIndex - 2;
        
        string degreeStr = coordinate.Substring(0, degreeLength);
        string minuteStr = coordinate.Substring(degreeLength);

        if (!double.TryParse(degreeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double degrees) ||
            !double.TryParse(minuteStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes))
        {
            return 0;
        }

        double decimalDegrees = degrees + (minutes / 60.0);

        // Apply direction
        if (direction == "S" || direction == "W")
        {
            decimalDegrees = -decimalDegrees;
        }

        return decimalDegrees;
    }

    private void UpdateTimestamp()
    {
        _lastUpdateTime = DateTime.Now;
        
        if (_currentGpsData != null)
        {
            _currentGpsData.Timestamp = _lastUpdateTime;
            OnGpsDataReceived(new GpsDataReceivedEventArgs { GpsData = _currentGpsData });
        }
    }

    protected virtual void OnGpsDataReceived(GpsDataReceivedEventArgs e)
    {
        GpsDataReceived?.Invoke(this, e);
    }

    protected virtual void OnGpsError(GpsErrorEventArgs e)
    {
        GpsError?.Invoke(this, e);
    }
}
