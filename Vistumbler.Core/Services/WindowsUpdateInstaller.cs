using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace Vistumbler.Core.Services;

/// <summary>
/// Installs a release with the NSIS setup.exe published alongside it (build/installer.nsi). Only installed copies can
/// update themselves: a portable copy (unzipped release) has no uninstall.exe next to the app, and its users are sent
/// to the release page instead.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsUpdateInstaller
{
    /// <summary>True when the running app was installed by the setup.exe, which puts uninstall.exe beside it.</summary>
    public static bool IsInstalledCopy => File.Exists(Path.Combine(InstallDirectory, "uninstall.exe"));

    public static string InstallDirectory => AppContext.BaseDirectory.TrimEnd('\\', '/');

    /// <summary>
    /// The setup.exe suffix for the running build. An x64 build on an ARM64 PC (emulated) stays on x64, matching what
    /// is installed.
    /// </summary>
    public static string InstallerSuffix =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "-win-arm64-setup.exe" : "-win-x64-setup.exe";

    /// <summary>Downloads <paramref name="asset"/> to %TEMP%\<paramref name="productName"/>-update and returns its path.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, ReleaseAsset asset, string productName,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{productName}-update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(asset.Name));
        var partial = path + ".part";

        using (var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(partial);
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;
                if (total > 0) progress?.Report((double)received / total.Value);
            }
        }
        File.Move(partial, path, overwrite: true);
        return path;
    }

    /// <summary>
    /// When the running app is code-signed, requires the installer to carry a valid Authenticode signature from the
    /// same publisher, so a tampered or substituted download is never run with admin rights. Unsigned builds (no
    /// signing certificate configured in CI) skip the check and rely on the HTTPS download.
    /// </summary>
    public static void VerifyPublisher(string installerPath)
    {
        var appSubject = GetSignerSubject(Environment.ProcessPath ?? "");
        if (appSubject is null) return;

        if (!IsSignatureTrusted(installerPath))
            throw new InvalidOperationException("The downloaded installer isn't validly signed, so it wasn't run.");
        if (!string.Equals(GetSignerSubject(installerPath), appSubject, StringComparison.Ordinal))
            throw new InvalidOperationException("The downloaded installer is signed by a different publisher, so it wasn't run.");
    }

    /// <summary>
    /// Starts the installer silently in update mode: it waits for this app to exit, upgrades the install in place
    /// and starts the app again. Returns false if the user declined the UAC prompt. The caller must exit promptly.
    /// </summary>
    public static bool Launch(string installerPath)
    {
        try
        {
            // /D= must come last and unquoted (NSIS rule); it keeps the update in the folder this copy runs from
            Process.Start(new ProcessStartInfo(installerPath, $"/S /UPDATE /D={InstallDirectory}")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)   // ERROR_CANCELLED: UAC prompt declined
        {
            return false;
        }
    }

    private static string? GetSignerSubject(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // X509CertificateLoader can't read the signer from a signed PE file
            using var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return cert.Subject;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            return null;   // not signed
        }
    }

    // ── WinVerifyTrust: checks the signature covers the file's contents and chains to a trusted root ─────────────

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private static bool IsSignatureTrusted(string path)
    {
        var fileInfo = new WinTrustFileInfo
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            pcwszFilePath = path,
        };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2,            // WTD_UI_NONE
                fdwRevocationChecks = 0,   // WTD_REVOKE_NONE
                dwUnionChoice = 1,         // WTD_CHOICE_FILE
                pFile = pFile,
                dwStateAction = 0,         // WTD_STATEACTION_IGNORE: no state to close afterwards
            };
            var action = GenericVerifyV2;
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
