using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AniListScrobbler.AniList;
using Jellyfin.Plugin.AniListScrobbler.AniList.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

public class AniListClientTests
{
    private static AniListClient Create(StubHttpClientFactory factory)
        => new(factory, NullLogger<AniListClient>.Instance);

    [Fact]
    public async Task GetViewerAsync_ParsesTheAccount()
    {
        using var factory = new StubHttpClientFactory(
            StubHttpClientFactory.Json("""{"data":{"Viewer":{"id":4242,"name":"grant"}}}"""));
        using var client = Create(factory);

        var viewer = await client.GetViewerAsync("token", CancellationToken.None);

        Assert.NotNull(viewer);
        Assert.Equal(4242, viewer.Id);
        Assert.Equal("grant", viewer.Name);
        Assert.Equal("Bearer token", factory.Requests[0].Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task GetMediaAsync_ParsesTheEntryAndListState()
    {
        using var factory = new StubHttpClientFactory(
            StubHttpClientFactory.Json(
                """
                {"data":{"Media":{"id":21,"idMal":21,"episodes":12,"format":"TV",
                "title":{"romaji":"One Piece","english":null,"native":"ONE PIECE"},
                "mediaListEntry":{"id":9,"progress":7,"status":"CURRENT","repeat":0}}}}
                """));
        using var client = Create(factory);

        var media = await client.GetMediaAsync(21, "token", includeRelations: false, CancellationToken.None);

        Assert.NotNull(media);
        Assert.Equal(21, media.Id);
        Assert.Equal(12, media.Episodes);
        Assert.Equal("One Piece", media.DisplayTitle);
        Assert.Equal(7, media.MediaListEntry?.Progress);
        Assert.Equal(MediaListStatus.CURRENT, media.MediaListEntry?.Status);
    }

    [Fact]
    public async Task GetMediaAsync_WithNullMedia_ReturnsNull()
    {
        using var factory = new StubHttpClientFactory(StubHttpClientFactory.Json("""{"data":{"Media":null}}"""));
        using var client = Create(factory);

        Assert.Null(await client.GetMediaAsync(1, "token", false, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_ReturnsCandidatesInOrder()
    {
        using var factory = new StubHttpClientFactory(
            StubHttpClientFactory.Json(
                """
                {"data":{"Page":{"media":[
                  {"id":1,"title":{"romaji":"First"},"synonyms":["Alt"]},
                  {"id":2,"title":{"romaji":"Second"}}]}}}
                """));
        using var client = Create(factory);

        var results = await client.SearchAsync("query", "token", CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Id);
        Assert.Equal("Alt", Assert.Single(results[0].Synonyms));
    }

    [Fact]
    public async Task SearchAsync_WithEmptyPage_ReturnsEmpty()
    {
        using var factory = new StubHttpClientFactory(StubHttpClientFactory.Json("""{"data":{"Page":{"media":[]}}}"""));
        using var client = Create(factory);

        Assert.Empty(await client.SearchAsync("query", "token", CancellationToken.None));
    }

    [Fact]
    public async Task SaveEntryAsync_SendsTheStatusAsAGraphQlEnumName()
    {
        using var factory = new StubHttpClientFactory(
            StubHttpClientFactory.Json(
                """{"data":{"SaveMediaListEntry":{"id":9,"progress":12,"status":"COMPLETED"}}}"""));
        using var client = Create(factory);

        var result = await client.SaveEntryAsync(21, 12, MediaListStatus.COMPLETED, null, "token", CancellationToken.None);

        Assert.Equal(12, result?.Progress);
        Assert.Equal(MediaListStatus.COMPLETED, result?.Status);

        var body = factory.RequestBodies[0];
        Assert.Contains("\"status\":\"COMPLETED\"", body, StringComparison.Ordinal);
        Assert.Contains("\"progress\":12", body, StringComparison.Ordinal);

        // A null repeat must be omitted so AniList does not reset the rewatch count.
        Assert.DoesNotContain("\"repeat\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RateLimited_ThrowsTransient()
    {
        using var factory = new StubHttpClientFactory(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.Add("Retry-After", "30");
            return response;
        });
        using var client = Create(factory);

        var exception = await Assert.ThrowsAsync<AniListException>(
            () => client.GetViewerAsync("token", CancellationToken.None));

        Assert.True(exception.IsTransient);
        Assert.False(exception.IsAuthenticationFailure);
    }

    [Fact]
    public async Task Unauthorized_ThrowsAuthenticationFailure()
    {
        using var factory = new StubHttpClientFactory(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = Create(factory);

        var exception = await Assert.ThrowsAsync<AniListException>(
            () => client.GetViewerAsync("bad", CancellationToken.None));

        Assert.True(exception.IsAuthenticationFailure);
    }

    [Fact]
    public async Task InvalidTokenErrorPayload_ThrowsAuthenticationFailure()
    {
        using var factory = new StubHttpClientFactory(
            StubHttpClientFactory.Json(
                """{"data":null,"errors":[{"message":"Invalid token"}]}""",
                HttpStatusCode.BadRequest));
        using var client = Create(factory);

        var exception = await Assert.ThrowsAsync<AniListException>(
            () => client.GetViewerAsync("bad", CancellationToken.None));

        Assert.True(exception.IsAuthenticationFailure);
    }

    [Fact]
    public async Task NotFoundErrorPayload_IsAnEmptyResultRatherThanAFailure()
    {
        // AniList answers "not found" queries with 404 plus an errors array.
        using var factory = new StubHttpClientFactory(
            StubHttpClientFactory.Json(
                """{"data":{"Media":null},"errors":[{"message":"Not Found."}]}""",
                HttpStatusCode.NotFound));
        using var client = Create(factory);

        Assert.Null(await client.GetMediaByMalIdAsync(999999, "token", CancellationToken.None));
    }

    [Fact]
    public async Task ServerError_IsTransient()
    {
        using var factory = new StubHttpClientFactory(
            StubHttpClientFactory.Json(
                """{"errors":[{"message":"Internal Error"}]}""",
                HttpStatusCode.InternalServerError));
        using var client = Create(factory);

        var exception = await Assert.ThrowsAsync<AniListException>(
            () => client.GetViewerAsync("token", CancellationToken.None));

        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task NetworkFailure_IsTransient()
    {
        using var factory = new StubHttpClientFactory(
            _ => throw new HttpRequestException("no route to host"));
        using var client = Create(factory);

        var exception = await Assert.ThrowsAsync<AniListException>(
            () => client.GetViewerAsync("token", CancellationToken.None));

        Assert.True(exception.IsTransient);
    }
}
