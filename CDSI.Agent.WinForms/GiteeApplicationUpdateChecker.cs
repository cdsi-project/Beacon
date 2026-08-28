using System.Globalization;
using System.Net.Http.Headers;

namespace CDSI.Agent.WinForms;

public sealed record ApplicationUpdateCheckResult(
    string CurrentVersion,
    string LatestVersion,
    bool IsUpdateAvailable);

public sealed class GiteeApplicationUpdateChecker
{
    internal const string VersionFileUrl =
        "https://gitee.com/cdsi/beacon/raw/master/VERSION";
    internal const string ReleasesUrl =
        "https://gitee.com/cdsi/beacon/releases";
    private const int MaximumVersionFileLength = 64;
    private readonly HttpClient _httpClient;

    public GiteeApplicationUpdateChecker(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<ApplicationUpdateCheckResult> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        var parsedCurrentVersion = BeaconApplicationVersion.Parse(currentVersion);
        using var request = new HttpRequestMessage(HttpMethod.Get, VersionFileUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(
            "CDSI-Beacon",
            currentVersion));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > MaximumVersionFileLength)
        {
            throw new InvalidDataException("Gitee VERSION 文件内容过长。");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(
            stream,
            detectEncodingFromByteOrderMarks: true);
        var buffer = new char[MaximumVersionFileLength + 1];
        var charactersRead = await reader.ReadBlockAsync(
            buffer.AsMemory(),
            cancellationToken);
        if (charactersRead > MaximumVersionFileLength)
        {
            throw new InvalidDataException("Gitee VERSION 文件内容过长。");
        }

        var latestVersion = new string(buffer, 0, charactersRead)
            .Trim()
            .TrimStart('\uFEFF');
        var parsedLatestVersion = BeaconApplicationVersion.Parse(latestVersion);
        return new ApplicationUpdateCheckResult(
            parsedCurrentVersion.ToString(),
            parsedLatestVersion.ToString(),
            parsedLatestVersion.CompareTo(parsedCurrentVersion) > 0);
    }
}

internal readonly record struct BeaconApplicationVersion(
    int Major,
    int Minor,
    int Revision,
    bool IsLegacy) : IComparable<BeaconApplicationVersion>
{
    public static BeaconApplicationVersion Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("版本号不能为空。");
        }

        var trimmed = value.Trim();
        var parts = trimmed.Split('.');
        if (parts.Length == 3 &&
            parts[2].Length == 2 &&
            TryParseSegment(parts[0], out var major) &&
            TryParseSegment(parts[1], out var minor) &&
            TryParseSegment(parts[2], out var revision) &&
            revision is >= 10 and <= 99)
        {
            return new BeaconApplicationVersion(major, minor, revision, false);
        }

        if (parts.Length == 2 &&
            parts[0] == "0" &&
            parts[1].Length == 3 &&
            TryParseSegment(parts[1], out var legacyBuild) &&
            legacyBuild is >= 100 and <= 206)
        {
            return new BeaconApplicationVersion(0, 0, legacyBuild, true);
        }

        throw new FormatException(
            $"版本号“{trimmed}”不符合 x.y.zz 格式，也不是受支持的历史版本。");
    }

    public int CompareTo(BeaconApplicationVersion other)
    {
        if (IsLegacy != other.IsLegacy)
        {
            return IsLegacy ? -1 : 1;
        }

        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        if (IsLegacy)
        {
            return Revision.CompareTo(other.Revision);
        }

        var minorComparison = Minor.CompareTo(other.Minor);
        return minorComparison != 0
            ? minorComparison
            : Revision.CompareTo(other.Revision);
    }

    public override string ToString()
    {
        return IsLegacy
            ? $"0.{Revision.ToString(CultureInfo.InvariantCulture)}"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{Major}.{Minor}.{Revision:D2}");
    }

    private static bool TryParseSegment(string value, out int result)
    {
        result = 0;
        return value.Length > 0 &&
            value.All(character => character is >= '0' and <= '9') &&
            int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out result);
    }
}
