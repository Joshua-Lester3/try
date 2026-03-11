// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Text.Json;

namespace Microsoft.TryDotNet.FileIntegration.Tests;

/// <summary>
/// xUnit class fixture that loads chapter-listings.json metadata, tracks actual compilation
/// results from test runs, and optionally updates the JSON file when results diverge from
/// expectations. Shared across all tests in <see cref="CSharpFileCompilationTests"/>.
/// </summary>
/// <remarks>
/// <para>
/// Configuration is driven by three environment variables:
/// </para>
/// <list type="bullet">
///   <item><c>TRYDOTNET_LISTINGS_METADATA</c> — explicit path to the chapter-listings.json file.</item>
///   <item><c>TRYDOTNET_LISTINGS_PATH</c> — path to the external listings directory; the JSON
///   file is resolved relative to this path at <c>../../Properties/chapter-listings.json</c>.</item>
///   <item><c>TRYDOTNET_UPDATE_LISTINGS</c> — when set to <c>"true"</c>, mismatches between
///   expected and actual compilation results are written back to the JSON file on dispose.</item>
/// </list>
/// </remarks>
public class ChapterListingsFixture : IDisposable
{
    internal const string ListingsMetadataEnvVar = "TRYDOTNET_LISTINGS_METADATA";
    internal const string ListingsPathEnvVar = "TRYDOTNET_LISTINGS_PATH";
    internal const string UpdateListingsEnvVar = "TRYDOTNET_UPDATE_LISTINGS";

    /// <summary>Deserialized chapter-listings metadata, or <c>null</c> if no file was found.</summary>
    public ChapterListingsRoot? Model { get; }

    /// <summary>Absolute path to the chapter-listings.json file, or <c>null</c> if unresolved.</summary>
    public string? JsonFilePath { get; }

    /// <summary>Whether the update-on-mismatch mode is enabled via environment variable.</summary>
    public bool UpdateEnabled { get; }

    /// <summary>
    /// Thread-safe dictionary populated by test runs with actual compilation outcomes.
    /// Keyed by (chapter name, filename).
    /// </summary>
    public ConcurrentDictionary<(string chapter, string filename), bool> ActualResults { get; } = new();

    public ChapterListingsFixture()
    {
        UpdateEnabled = string.Equals(
            Environment.GetEnvironmentVariable(UpdateListingsEnvVar),
            "true",
            StringComparison.OrdinalIgnoreCase);

        JsonFilePath = ResolveJsonPath();

        if (JsonFilePath is not null && File.Exists(JsonFilePath))
        {
            var json = File.ReadAllText(JsonFilePath);
            Model = JsonSerializer.Deserialize<ChapterListingsRoot>(json);
        }
    }

    private static string? ResolveJsonPath()
    {
        var explicitPath = Environment.GetEnvironmentVariable(ListingsMetadataEnvVar);
        if (!string.IsNullOrEmpty(explicitPath))
        {
            return explicitPath;
        }

        var listingsPath = Environment.GetEnvironmentVariable(ListingsPathEnvVar);
        if (!string.IsNullOrEmpty(listingsPath))
        {
            return Path.GetFullPath(Path.Combine(listingsPath, "../../Properties/chapter-listings.json"));
        }

        return null;
    }

    /// <summary>
    /// Looks up the expected <c>can_compile</c> value for a given chapter and filename.
    /// Returns <c>null</c> when metadata is unavailable or no matching entry exists.
    /// </summary>
    public bool? GetExpectedCanCompile(string? chapterName, string filename)
    {
        if (chapterName is null || Model is null)
        {
            return null;
        }

        if (Model.Chapters.TryGetValue(chapterName, out var entries))
        {
            var entry = entries.FirstOrDefault(e =>
                string.Equals(e.Filename, filename, StringComparison.OrdinalIgnoreCase));
            return entry?.CanCompile;
        }

        return null;
    }

    /// <summary>
    /// When update mode is enabled, writes any mismatched compilation results back to the
    /// chapter-listings.json file so the metadata stays in sync with actual compiler behavior.
    /// </summary>
    public void Dispose()
    {
        if (!UpdateEnabled || Model is null || JsonFilePath is null)
        {
            return;
        }

        var mismatches = new List<string>();

        foreach (var ((chapter, filename), actual) in ActualResults)
        {
            if (!Model.Chapters.TryGetValue(chapter, out var entries))
            {
                continue;
            }

            var entry = entries.FirstOrDefault(e =>
                string.Equals(e.Filename, filename, StringComparison.OrdinalIgnoreCase));

            if (entry is not null && entry.CanCompile != actual)
            {
                mismatches.Add($"  {chapter}/{filename}: can_compile {entry.CanCompile} → {actual}");
                entry.CanCompile = actual;
            }
        }

        if (mismatches.Count > 0)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(Model, options);
            File.WriteAllText(JsonFilePath, updatedJson + Environment.NewLine);

            Console.WriteLine($"[ChapterListingsFixture] Updated {JsonFilePath} with {mismatches.Count} change(s):");
            foreach (var m in mismatches)
            {
                Console.WriteLine(m);
            }
        }
    }
}
