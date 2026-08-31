using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

/// <summary>
/// Serves canned HTTP responses and records the requests that were sent.
/// </summary>
public sealed class StubHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly StubHandler _handler;

    public StubHttpClientFactory(params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
    {
        _handler = new StubHandler(responders);
    }

    /// <summary>Gets the bodies of every request that was sent.</summary>
    public IReadOnlyList<string> RequestBodies => _handler.RequestBodies;

    /// <summary>Gets every request that was sent.</summary>
    public IReadOnlyList<HttpRequestMessage> Requests => _handler.Requests;

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);

    public void Dispose() => _handler.Dispose();

    /// <summary>Builds a JSON response.</summary>
    public static Func<HttpRequestMessage, HttpResponseMessage> Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>[] _responders;
        private readonly List<string> _bodies = new();
        private readonly List<HttpRequestMessage> _requests = new();
        private int _index;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage>[] responders)
        {
            _responders = responders;
        }

        public IReadOnlyList<string> RequestBodies => _bodies;

        public IReadOnlyList<HttpRequestMessage> Requests => _requests;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Add(request);
            _bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            var responder = _responders[Math.Min(_index, _responders.Length - 1)];
            _index++;

            return responder(request);
        }
    }
}
