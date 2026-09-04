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

## When an episode counts as watched

Two things can trigger a scrobble.

**Playback stopping.** Jellyfin reports where you stopped, and the plugin compares that against
the runtime. Past the threshold, it scrobbles.

The threshold is the only gate whenever the timings are known, deliberately: Jellyfin has its
own completion rule (`MaxResumePct`, 90% by default) and deferring to that first would cap the
setting, so anything above 90 would quietly do nothing. Setting 95 here really does mean 95,
whether or not Jellyfin considers the episode played.

If the timings are not known — a client that reports no position, or an item with no runtime —
there is nothing to measure, so the plugin follows Jellyfin's own judgement. Jellyfin marks
those played, and disagreeing would leave AniList behind your library.

**Marking something played by hand.** Toggling an episode watched in the Jellyfin UI scrobbles
it, if that option is on. Only the manual toggle is handled here; playback itself is covered
above.

A normal watch can raise both signals, so a repeat scrobble of the same episode by the same
user within a minute is dropped.

## How episodes are matched

AniList models each season and cour as its own entry, while Jellyfin models a show as one
series with numbered seasons. The plugin resolves a match in this order, stopping at the first
that works:

1. **A manual mapping** for the season, then for the series.
2. **An AniList id** on the season, then on the series.
3. **The series' TheTVDB id together with the season number**, resolved through the
   [Fribb/anime-lists](https://github.com/Fribb/anime-lists) mapping database. That dataset
   pins each AniList entry to a TheTVDB series *and season*, so it names the season's own
   entry outright.
4. **A MyAnimeList id**, resolved through AniList directly.
5. **An AniDB id**, resolved through the same mapping database. AniList's API cannot resolve
   AniDB ids itself, so libraries built by the AniDB provider or Shoko need this table.
6. **A title search**, accepted only above a configurable similarity threshold.

The mapping database is downloaded once and refreshed weekly.

The TheTVDB season lookup matters more than its position in the list suggests. Anime libraries
routinely tag the *series* but leave seasons untagged, and a series id names only the first
AniList entry. Reaching season 3 from it means walking the AniList `SEQUEL` relation twice,
which fails whenever two seasons are linked by anything other than a TV-to-TV sequel edge, and
cannot recover at all when the series id happens to name a later season rather than the first.
Looking the season up directly sidesteps both. The sequel walk is still there as the fallback
for libraries with no TheTVDB ids.

Where a season is split across two cours the lookup returns both entries; the first is used and
the overflow rule below carries later episodes into the second.

Libraries numbered continuously across seasons are handled the same way: an episode that runs
past the end of one entry continues into its sequel, and the corrected mapping is cached so the
rest of the season skips the lookup.

Libraries do not agree on whether an episode number restarts each season, and a single library
is often inconsistent with itself, so a number that does not fit its season is re-read as
counting from the first episode of the series:

- Jujutsu Kaisen stored as `1..24`, `1..23`, then `48..59` — episode 59 is episode 12 of the
  third AniList entry, not progress 59 on a 12-episode one.
- One Piece keeps every episode in a single "season 23" folder numbered from 1156, and AniList
  holds the whole show as one still-airing entry with no seasons to walk to at all.

The re-read is only tried when the number cannot be counting from the start of its own season,
so an ordinary season 2 episode 1 is never mistaken for season 1.

Specials (season 0) are never scrobbled automatically, because they do not map onto a numbered
run without guessing. Add a manual mapping if you want them counted.

### Provider ids are checked before they are trusted

An id on a library item is not proof. In a real 110-series anime library, four shows carried an
AniDB id belonging to something else entirely — two live-action shows that would have scrobbled
onto unrelated anime, and two ids pointing at a special or a film rather than the series.

So before anything is written, the AniList entry is compared against **both** the item's title
and its folder name, and the match is refused unless one of them is close enough (0.80 by
default). Both halves matter: a library can show the wrong title while the folder is right — a
folder named `Girlfriend, Girlfriend` displaying as *The Girlfriend Experience* — or the
reverse. Taking the better of the two keeps a good id from being thrown away over a bad
display name.

Manual mappings are an explicit instruction and are never checked.

Titles are compared after compatibility normalisation, so `Ranma ½` and `Ranma1/2` compare as
the same words rather than differing by a character that would otherwise be discarded.

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
