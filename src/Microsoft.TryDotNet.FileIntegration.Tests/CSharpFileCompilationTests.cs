using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.DotNet.Interactive.Connection;
using Microsoft.DotNet.Interactive.CSharpProject.Events;
using Microsoft.DotNet.Interactive.Events;
using Xunit;

namespace Microsoft.TryDotNet.FileIntegration.Tests;

/// <summary>
/// Integration tests that compile C# source files through the Try .NET API and verify
/// compilation outcomes against optional chapter-listings.json metadata.
/// </summary>
/// <remarks>
/// <para>Test data comes from two sources:</para>
/// <list type="bullet">
///   <item><strong>Embedded samples</strong> — <c>.cs</c> files in the <c>Samples/</c> directory
///   deployed alongside the test assembly. These have no chapter metadata and are always
///   expected to compile successfully.</item>
///   <item><strong>External listings</strong> — files under the path specified by the
///   <c>TRYDOTNET_LISTINGS_PATH</c> environment variable, following a
///   <c>Chapter##/##.##.cs</c> naming convention. Compilation expectations are driven by
///   the <see cref="ChapterListingsFixture"/> metadata.</item>
/// </list>
/// </remarks>
public class CSharpFileCompilationTests : IClassFixture<WebApplicationFactory<Program>>, IClassFixture<ChapterListingsFixture>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ChapterListingsFixture _listingsFixture;

    public CSharpFileCompilationTests(WebApplicationFactory<Program> factory, ChapterListingsFixture listingsFixture)
    {
        _factory = factory;
        _listingsFixture = listingsFixture;
    }

    /// <summary>
    /// Provides test data by enumerating embedded sample files and, when configured,
    /// external chapter listing files. Each row contains the full path, a display name,
    /// and an optional chapter name.
    /// </summary>
    public static IEnumerable<object[]> SampleFiles()
    {
        // Always include embedded Samples/ directory
        var samplesDir = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "Samples");

        if (Directory.Exists(samplesDir))
        {
            foreach (var file in Directory.EnumerateFiles(samplesDir, "*.cs"))
            {
                // Embedded samples have no chapter
                yield return new object[] { file, Path.GetFileName(file), null };
            }
        }

        // Additionally include external listings if configured
        var externalPath = Environment.GetEnvironmentVariable(ChapterListingsFixture.ListingsPathEnvVar);

        if (!string.IsNullOrEmpty(externalPath) && Directory.Exists(externalPath))
        {
            foreach (var file in Directory.EnumerateFiles(externalPath, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(externalPath, file);
                var segments = relativePath.Split(Path.DirectorySeparatorChar);

                // Only include Chapter##/##.##.cs — exactly two segments, matching the numbering pattern
                if (segments.Length != 2)
                    continue;
                if (!Regex.IsMatch(segments[0], @"^Chapter\d+$"))
                    continue;
                if (!Regex.IsMatch(segments[1], @"^\d+\.\d+\.cs$"))
                    continue;

                yield return new object[] { file, relativePath, segments[0] };
            }
        }
    }

    /// <summary>
    /// Compiles a single C# source file through the Try .NET API and asserts the outcome
    /// matches the expectation from chapter-listings.json (if available), or that embedded
    /// samples compile successfully.
    /// </summary>
    [Theory]
    [MemberData(nameof(SampleFiles))]
    public async Task File_compilation_matches_expectation(string fullPath, string displayName, string? chapterName)
    {
        var fileName = Path.GetFileName(fullPath);
        var fileContent = await File.ReadAllTextAsync(fullPath);

        var client = _factory.CreateDefaultClient();

        var requestJson = BuildCommandPayload(fileName, fileContent);
        var requestBody = JsonContent.Create(JsonDocument.Parse(requestJson).RootElement);

        var response = await client.PostAsync("commands", requestBody);

        var responseJson = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken.None)).RootElement;

        var events = responseJson
            .GetProperty("events")
            .EnumerateArray()
            .Select(KernelEventEnvelope.Deserialize)
            .Select(ee => ee.Event)
            .ToList();

        response.EnsureSuccessStatusCode();

        var assemblyProduced = events.OfType<AssemblyProduced>().SingleOrDefault();
        bool actualCompiled = assemblyProduced is not null;

        // Record result for fixture update logic
        if (chapterName is not null)
        {
            _listingsFixture.ActualResults[(chapterName, fileName)] = actualCompiled;
        }

        var expectedCompile = _listingsFixture.GetExpectedCanCompile(chapterName, fileName);

        // Chapter files require the metadata JSON to be present. Fail early with a clear
        // message rather than silently asserting "all chapter files compile", which is wrong
        // for listings that intentionally reference types defined in other listing files.
        if (chapterName is not null && _listingsFixture.Model is null)
        {
            var resolvedPath = _listingsFixture.JsonFilePath ?? "(unresolved — set TRYDOTNET_LISTINGS_PATH or TRYDOTNET_LISTINGS_METADATA)";
            false.Should().BeTrue(
                $"chapter-listings.json could not be loaded, so compilation expectations for " +
                $"chapter files are unavailable. Resolved path: {resolvedPath}");
            return;
        }

        if (expectedCompile is null)
        {
            // No metadata entry (embedded samples, unknown files): assert compilation succeeds
            AssertCompilationSucceeded(assemblyProduced, events, displayName);
        }
        else if (_listingsFixture.UpdateEnabled && expectedCompile.Value != actualCompiled)
        {
            // Update mode: mismatch is noted but test passes — fixture writes the JSON on dispose
            Console.WriteLine(
                $"[UPDATE] '{displayName}': expected can_compile={expectedCompile.Value}, actual={actualCompiled}. Will update JSON on dispose.");
        }
        else if (expectedCompile.Value)
        {
            // Expected to compile successfully per chapter-listings.json
            AssertCompilationSucceeded(assemblyProduced, events, displayName,
                "per chapter-listings.json");
        }
        else
        {
            // Expected to fail compilation per chapter-listings.json
            actualCompiled.Should().BeFalse(
                $"Expected '{displayName}' to fail compilation per chapter-listings.json, but it succeeded.");
        }
    }

    /// <summary>
    /// Asserts that compilation produced a non-empty assembly, collecting diagnostic details
    /// on failure for a descriptive assertion message.
    /// </summary>
    private static void AssertCompilationSucceeded(
        AssemblyProduced? assemblyProduced,
        List<KernelEvent> events,
        string displayName,
        string? context = null)
    {
        using var scope = new AssertionScope();

        if (assemblyProduced is null)
        {
            var details = FormatCompilationFailureDetails(events);
            var contextSuffix = context is not null ? $" {context}" : "";

            assemblyProduced.Should().NotBeNull(
                $"Expected compilation of '{displayName}' to produce an assembly{contextSuffix}, but it failed:{Environment.NewLine}{details}");
        }
        else
        {
            assemblyProduced.Assembly.Value.Should().NotBeNullOrWhiteSpace(
                $"AssemblyProduced for '{displayName}' had an empty assembly value.");
        }
    }

    /// <summary>
    /// Extracts <see cref="CommandFailed"/> messages and <see cref="DiagnosticsProduced"/>
    /// entries from the event stream and formats them into a human-readable string.
    /// </summary>
    private static string FormatCompilationFailureDetails(List<KernelEvent> events)
    {
        var failures = events.OfType<CommandFailed>()
            .Select(f => f.Message);

        var diagnostics = events.OfType<DiagnosticsProduced>()
            .SelectMany(d => d.Diagnostics)
            .Select(d => $"{d.Severity} {d.Code}: {d.Message} ({d.LinePositionSpan})");

        return string.Join(Environment.NewLine,
            failures.Concat(diagnostics).DefaultIfEmpty("No AssemblyProduced event and no diagnostics found."));
    }

    /// <summary>
    /// Builds the JSON command payload that opens a project with a single file,
    /// opens that file as the active document, and compiles the project.
    /// </summary>
    private static string BuildCommandPayload(string fileName, string fileContent)
    {
        var escapedContent = JsonEncodedText.Encode(fileContent).ToString();

        return $$"""
            {
                "commands": [
                    {
                        "commandType": "OpenProject",
                        "command": {
                            "project": {
                                "files": [
                                    {
                                        "relativeFilePath": "{{fileName}}",
                                        "content": "{{escapedContent}}"
                                    }
                                ]
                            }
                        },
                        "token": "file-test::1"
                    },
                    {
                        "commandType": "OpenDocument",
                        "command": {
                            "relativeFilePath": "{{fileName}}"
                        },
                        "token": "file-test::2"
                    },
                    {
                        "commandType": "CompileProject",
                        "command": {},
                        "token": "file-test::3"
                    }
                ]
            }
            """;
    }
}
