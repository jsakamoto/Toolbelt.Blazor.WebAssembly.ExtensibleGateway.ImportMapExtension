using ImportMapExtension.Test.Internals;

namespace ImportMapExtension.Test;

/// <summary>
/// The point of this package, end to end: run the sample app the way a developer does, edit a
/// collocated JavaScript module, reload without rebuilding, and have a real browser accept both the
/// module and the strict policy around the import map.
/// <para>
/// The app runs in a disposable container, so nothing any of this does reaches the working tree or
/// the NuGet cache of the machine running it. The browser runs here rather than in that container:
/// Playwright brings its own, and the gateway is reachable from here through the port that the
/// container publishes.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Self)]
public class GatewayE2ETests
{
    private const string OriginalModule = "export const greeting = () => \"Hello, Blazor!\";\n";

    private const string EditedModule = "export const greeting = () => \"Edited without a rebuild!\";\n";

    private const string ModulePath = $"{SolutionContainer.SolutionDir}/SampleApp/App.razor.js";

    /// <summary>
    /// The gateway that the tests which ask nothing special of the build share. The two that turn
    /// a half of this package off need a build of their own, so they start their own.
    /// </summary>
    private SolutionContainer _Gateway = null!;

    [OneTimeSetUp]
    public async Task StartTheGateway() => this._Gateway = await SolutionContainer.StartGatewayAsync();

    [OneTimeTearDown]
    public async Task DisposeTheGateway() => await this._Gateway.DisposeAsync();

    [Test]
    public async Task EditingAModuleAndReloadingWorks()
    {
        // Whatever else has run against this gateway, the module is what the working tree has in it.
        await WriteModuleAsync(this._Gateway, OriginalModule);

        // The app works as it stands: it renders, nothing is blocked, nothing is logged.
        var before = await HeadlessBrowser.LoadAsync(this._Gateway.BaseAddress);
        before.CspViolations.Is([], message: $"The browser blocked something.\n{before.Describe()}");
        before.ConsoleErrors.Is([], message: $"The browser reported an error.\n{before.Describe()}");
        before.Greeting.Is("Hello, Blazor!", message: before.Describe());

        // Edit the module. No rebuild, no restart. This is what breaks without this package.
        await WriteModuleAsync(this._Gateway, EditedModule);

        var after = await HeadlessBrowser.LoadAsync(this._Gateway.BaseAddress);
        after.CspViolations.Is([], message: $"The browser blocked something after the edit.\n{after.Describe()}");
        after.ConsoleErrors.Is([], message: $"The browser reported an error after the edit.\n{after.Describe()}");
        after.ImportResult.Is("IMPORT OK: Edited without a rebuild!", message: after.Describe());
        after.Greeting.Is("Edited without a rebuild!", message: after.Describe());
    }

    /// <summary>
    /// Proves the test above is testing something. With the removal turned off, the stale
    /// "integrity" is exactly what blocks the edited module.
    /// </summary>
    [Test]
    public async Task WithoutTheIntegrityRemovalTheEditedModuleIsBlocked()
    {
        await using var gateway = await SolutionContainer.StartGatewayAsync("-p:ImportMapStripIntegrity=false");
        await WriteModuleAsync(gateway, EditedModule);

        var report = await HeadlessBrowser.LoadAsync(gateway.BaseAddress);

        report.ImportResult.StartsWith("IMPORT FAILED").IsTrue(message: $"The module was expected to be blocked.\n{report.Describe()}");
        report.ConsoleErrors.Any(e => e.Contains("integrity", StringComparison.OrdinalIgnoreCase))
            .IsTrue(message: $"The browser was expected to complain about the integrity.\n{report.Describe()}");
    }

    /// <summary>
    /// Proves the other half. With nothing to fill the placeholder in, the policy names a digest
    /// that no import map has, and the browser refuses the import map itself.
    /// </summary>
    [Test]
    public async Task WithoutTheDigestBeingWrittenTheImportMapIsBlocked()
    {
        // A token the page does not contain leaves "sha256-{importmap}" standing in the policy.
        await using var gateway = await SolutionContainer.StartGatewayAsync("-p:ImportMapCspPlaceholder=@@never@@");

        var report = await HeadlessBrowser.LoadAsync(gateway.BaseAddress);

        report.CspViolations.IsNot([], message: $"The import map was expected to be blocked.\n{report.Describe()}");
    }

    [Test]
    public async Task EveryRouteThatAnswersWithTheDocumentCarriesAMatchingPolicy()
    {
        using var http = new HttpClient { BaseAddress = new Uri(this._Gateway.BaseAddress) };

        foreach (var path in new[] { "/", "/index.html", "/some/deep/link" })
        {
            var html = await http.GetStringAsync(path);
            Digest.InPolicyOf(html).Is(Digest.OfImportMapIn(html), message: $"The policy served at \"{path}\" does not match its import map.");
            Digest.ImportMapOf(html).Contains("\"integrity\"").IsFalse(message: $"The import map served at \"{path}\" still has its integrity.");
        }
    }

    /// <summary>
    /// The gateway answers with a pre-compressed copy when the request allows it, which the extension
    /// has to turn off for itself or it would have no plain text to rewrite.
    /// </summary>
    [Test]
    public async Task ACompressionAwareClientGetsTheSameRewrittenDocument()
    {
        using var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(this._Gateway.BaseAddress) };

        var html = await http.GetStringAsync("/index.html");

        Digest.InPolicyOf(html).Is(Digest.OfImportMapIn(html));
    }

    private static async Task WriteModuleAsync(SolutionContainer gateway, string content)
    {
        await gateway.WriteTextAsync(ModulePath, content);
        // Give the file watchers a moment to settle before the page is loaded again.
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}
