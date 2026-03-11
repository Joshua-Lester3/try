// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace Microsoft.TryDotNet.FileIntegration.Tests;

/// <summary>
/// Root model for the chapter-listings.json metadata file.
/// Maps chapter names (e.g. "Chapter01") to lists of <see cref="ListingEntry"/> records
/// describing the expected compilation and run behavior of each code listing.
/// </summary>
public class ChapterListingsRoot
{
    [JsonPropertyName("chapters")]
    public Dictionary<string, List<ListingEntry>> Chapters { get; set; } = new();
}

/// <summary>
/// Describes a single code listing file and its expected compilation/run behavior.
/// Used as metadata to drive test assertions in <see cref="CSharpFileCompilationTests"/>.
/// </summary>
public class ListingEntry
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    [JsonPropertyName("can_compile")]
    public bool CanCompile { get; set; }

    [JsonPropertyName("can_run")]
    public bool CanRun { get; set; }
}
