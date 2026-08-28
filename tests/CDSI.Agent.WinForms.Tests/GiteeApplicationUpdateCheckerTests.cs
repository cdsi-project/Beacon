using System.Net;
using CDSI.Agent.WinForms;

namespace CDSI.Agent.WinForms.Tests;

public sealed class GiteeApplicationUpdateCheckerTests
{
    [Fact]
    public async Task CheckAsync_ReadsTheGiteeVersionFileAndFindsANewerVersion()
    {
        Uri? requestedUri = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("0.2.11\r\n")
            };
        }));
        var checker = new GiteeApplicationUpdateChecker(httpClient);

        var result = await checker.CheckAsync("0.2.10");

        Assert.Equal(GiteeApplicationUpdateChecker.VersionFileUrl, requestedUri?.AbsoluteUri);
        Assert.Equal("0.2.10", result.CurrentVersion);
        Assert.Equal("0.2.11", result.LatestVersion);
        Assert.True(result.IsUpdateAvailable);
    }

    [Theory]
    [InlineData("0.2.10", "0.206", false)]
    [InlineData("0.2.10", "0.2.10", false)]
    [InlineData("0.2.99", "0.3.10", true)]
    [InlineData("1.2.98", "1.2.99", true)]
    public async Task CheckAsync_UsesTheBeaconVersionSequence(
        string currentVersion,
        string latestVersion,
        bool expectedUpdate)
    {
        using var httpClient = CreateHttpClient(HttpStatusCode.OK, latestVersion);
        var checker = new GiteeApplicationUpdateChecker(httpClient);

        var result = await checker.CheckAsync(currentVersion);

        Assert.Equal(expectedUpdate, result.IsUpdateAvailable);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("0.2.100")]
    [InlineData("0.207")]
    [InlineData("not-a-version")]
    public async Task CheckAsync_RejectsAnInvalidRemoteVersion(string remoteVersion)
    {
        using var httpClient = CreateHttpClient(HttpStatusCode.OK, remoteVersion);
        var checker = new GiteeApplicationUpdateChecker(httpClient);

        await Assert.ThrowsAsync<FormatException>(
            () => checker.CheckAsync("0.2.10"));
    }

    [Fact]
    public async Task CheckAsync_RejectsAnUnsuccessfulGiteeResponse()
    {
        using var httpClient = CreateHttpClient(HttpStatusCode.NotFound, "not found");
        var checker = new GiteeApplicationUpdateChecker(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => checker.CheckAsync("0.2.10"));
    }

    [Fact]
    public void CreateOpenGiteeReleasesStartInfo_UsesTheSystemBrowser()
    {
        var startInfo = MainForm.CreateOpenGiteeReleasesStartInfo();

        Assert.Equal(GiteeApplicationUpdateChecker.ReleasesUrl, startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
    }

    private static HttpClient CreateHttpClient(
        HttpStatusCode statusCode,
        string content)
    {
        return new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            }));
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
