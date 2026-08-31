# AniList Scrobbler for Jellyfin

Tracks what you watch in Jellyfin and logs it to [AniList](https://anilist.co).

When you finish an episode, the plugin works out which AniList entry it belongs to and moves
your progress forward. Each Jellyfin user links their own AniList account, so a shared server
updates each person's list separately.

Built against **Jellyfin 10.11** (`net9.0`).

## What it does

- Scrobbles an episode once you have watched enough of it (85% by default).
- Optionally scrobbles when you mark something played by hand.
- Scrobbles anime films as a single-episode entry.
- Sets the list status to *Watching*, or *Completed* on the final episode.
- Never moves your progress backwards, and never lowers a completed entry.
- Restricts scrobbling to chosen libraries, so live-action shows are left alone.

This is a one-way scrobbler: Jellyfin to AniList. It does not pull AniList progress back into
Jellyfin, and it does not sync ratings.

## How episodes are matched

AniList models each season and cour as its own entry, while Jellyfin models a show as one
series with numbered seasons. The plugin resolves a match in this order, stopping at the first
that works:

1. **A manual mapping** for the season, then for the series.
2. **An AniList id** on the season, then on the series.
3. **A MyAnimeList id**, resolved through AniList directly.
4. **An AniDB id**, resolved through the [Fribb/anime-lists](https://github.com/Fribb/anime-lists)
   mapping database. AniList's API cannot resolve AniDB ids itself, so libraries built by the
   AniDB provider or Shoko need this table. It is downloaded once and refreshed weekly.
5. **A title search**, accepted only above a configurable similarity threshold.

When only the series carries an id, that id names the *first* AniList entry. Later seasons are
reached by following the AniList `SEQUEL` relation, so season 3 in Jellyfin lands on the third
entry in the chain rather than overwriting season 1.

Libraries numbered continuously across seasons are handled the same way: an episode that runs
past the end of one entry continues into its sequel, and the corrected mapping is cached so the
rest of the season skips the lookup.

Specials (season 0) are never scrobbled automatically, because they do not map onto a numbered
run without guessing. Add a manual mapping if you want them counted.

### When matching gets it wrong

Add a manual mapping in the plugin settings. You need the Jellyfin series or season id — it is
the `id=` value in the URL when you open the item in the web UI — and the AniList media id,
which is the number in an AniList URL such as `anilist.co/anime/16498`.

Use the **offset** when Jellyfin numbers episodes continuously but AniList restarts each
season: a season whose first episode is 26 in Jellyfin and 1 on AniList needs an offset of 25.

## Installing

Build and copy the plugin into your Jellyfin plugin directory:

```bash
./scripts/package.sh
```

That writes `artifacts/jellyfin-plugin-anilist-scrobbler_<version>.zip`. Extract it into a new
folder under your server's plugin directory, then restart Jellyfin:

```
<jellyfin config>/plugins/AniList Scrobbler/Jellyfin.Plugin.AniListScrobbler.dll
```

Only the plugin's own assembly is packaged. Jellyfin already ships everything it depends on,
and shipping duplicate framework assemblies would shadow the server's copies.

## Linking an AniList account

1. Create an API client at [anilist.co/settings/developer](https://anilist.co/settings/developer)
   with the redirect URL `https://anilist.co/api/v2/oauth/pin`.
2. Put the client id and secret into the plugin settings and save.
3. Open the authorization link on the settings page, approve access, and copy the PIN.
4. Paste the PIN and exchange it for a token, then press **Check token** and save.

If you already have a token from somewhere else, paste it straight into the token field and
skip the client id and secret entirely.

Repeat per Jellyfin user — the user picker at the top of the settings page switches which
account you are editing.

## Rate limits

AniList documents 90 requests per minute but frequently serves a degraded 30, which is the
default here. Requests are queued through a sliding-window limiter, and a `429` pauses all
traffic for the interval AniList asks for.

## Development

```bash
dotnet build          # build
dotnet test           # run the tests
./scripts/package.sh  # produce the release zip
```

The repository targets .NET 9 because Jellyfin 10.11 does. To build against Jellyfin 10.10
instead, set `TargetFramework` to `net8.0` and the `Jellyfin.Controller` / `Jellyfin.Model`
package versions to `10.10.7`, and change `targetAbi` in `build.yaml` to `10.10.0.0`.

`.github/workflows` is read by GitHub Actions and by Gitea/Forgejo Actions alike, so CI runs on
either host — provided Actions is enabled on the instance.

## Licence

GPL-3.0-only. See [LICENSE](LICENSE).
