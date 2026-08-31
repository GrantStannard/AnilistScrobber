using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AniListScrobbler.AniList.Models;

/// <summary>
/// The status of an entry on a user's AniList list.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MediaListStatus
{
    /// <summary>Currently watching.</summary>
    CURRENT,

    /// <summary>Planning to watch.</summary>
    PLANNING,

    /// <summary>Finished watching.</summary>
    COMPLETED,

    /// <summary>Dropped.</summary>
    DROPPED,

    /// <summary>Paused.</summary>
    PAUSED,

    /// <summary>Rewatching.</summary>
    REPEATING,
}

/// <summary>
/// The AniList account behind an access token.
/// </summary>
public class Viewer
{
    /// <summary>Gets or sets the AniList user id.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the AniList user name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// The set of titles AniList holds for a media entry.
/// </summary>
public class MediaTitle
{
    /// <summary>Gets or sets the romaji title.</summary>
    [JsonPropertyName("romaji")]
    public string? Romaji { get; set; }

    /// <summary>Gets or sets the English title.</summary>
    [JsonPropertyName("english")]
    public string? English { get; set; }

    /// <summary>Gets or sets the native title.</summary>
    [JsonPropertyName("native")]
    public string? Native { get; set; }
}

/// <summary>
/// A single edge in a media's relation graph.
/// </summary>
public class MediaRelationEdge
{
    /// <summary>Gets or sets the relation type, for example SEQUEL or PREQUEL.</summary>
    [JsonPropertyName("relationType")]
    public string? RelationType { get; set; }

    /// <summary>Gets or sets the related media.</summary>
    [JsonPropertyName("node")]
    public Media? Node { get; set; }
}

/// <summary>
/// The relations connection of a media entry.
/// </summary>
public class MediaRelations
{
    /// <summary>Gets or sets the relation edges.</summary>
    [JsonPropertyName("edges")]
    public IReadOnlyList<MediaRelationEdge> Edges { get; set; } = Array.Empty<MediaRelationEdge>();
}

/// <summary>
/// The current user's list entry for a media, as returned inline on a media query.
/// </summary>
public class MediaListEntry
{
    /// <summary>Gets or sets the list entry id.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the number of episodes watched.</summary>
    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    /// <summary>Gets or sets the list status.</summary>
    [JsonPropertyName("status")]
    public MediaListStatus? Status { get; set; }

    /// <summary>Gets or sets the number of completed rewatches.</summary>
    [JsonPropertyName("repeat")]
    public int Repeat { get; set; }
}

/// <summary>
/// An AniList media entry (one season, cour or film).
/// </summary>
public class Media
{
    /// <summary>Gets or sets the AniList media id.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the MyAnimeList id, when AniList knows one.</summary>
    [JsonPropertyName("idMal")]
    public int? IdMal { get; set; }

    /// <summary>Gets or sets the titles.</summary>
    [JsonPropertyName("title")]
    public MediaTitle? Title { get; set; }

    /// <summary>Gets or sets the media format, for example TV or MOVIE.</summary>
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    /// <summary>Gets or sets the total episode count, when known.</summary>
    [JsonPropertyName("episodes")]
    public int? Episodes { get; set; }

    /// <summary>Gets or sets the season year.</summary>
    [JsonPropertyName("seasonYear")]
    public int? SeasonYear { get; set; }

    /// <summary>Gets or sets alternative titles.</summary>
    [JsonPropertyName("synonyms")]
    public IReadOnlyList<string> Synonyms { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the relation graph, when requested.</summary>
    [JsonPropertyName("relations")]
    public MediaRelations? Relations { get; set; }

    /// <summary>Gets or sets the authenticated user's list entry, when requested with a token.</summary>
    [JsonPropertyName("mediaListEntry")]
    public MediaListEntry? MediaListEntry { get; set; }

    /// <summary>
    /// Gets the best available display title.
    /// </summary>
    public string DisplayTitle =>
        Title?.English ?? Title?.Romaji ?? Title?.Native ?? $"AniList #{Id}";
}

/// <summary>
/// The result of saving a list entry.
/// </summary>
public class SaveMediaListEntryResult
{
    /// <summary>Gets or sets the list entry id.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the stored progress.</summary>
    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    /// <summary>Gets or sets the stored status.</summary>
    [JsonPropertyName("status")]
    public MediaListStatus? Status { get; set; }
}
