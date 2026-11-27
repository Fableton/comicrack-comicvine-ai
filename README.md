# Comic Vine AI plugin

.NET plugin for ComicRack Community Edition (needs the `feature/dotnet-plugin` branch) that identifies the
selected comics on Comic Vine and tags them, like the Python Comic Vine Scraper but with cover comparison and
an AI fallback.

## Features
**Identification**
- Identifies the selected books from their metadata and file name, with a Comic Vine search scored by name, year and publisher.
- Compares the first page with the Comic Vine cover and its alternative covers. The comparison tolerates crops, scans, logo
  strips and barcodes.
- When the file's number does not settle it, a deep cover search compares several series and the issues around that number.
- Optional **Claude fallback** (`claude -p`): Claude looks at the cover and names the original publication. When Claude is sure
  and the series, the publisher and the cover agree, the book is accepted.
- Automatic tagging above a confidence threshold (80% by default). The rest go to the manual review.
- Localized reprints (Panini, Zinco, Egmont...): unwanted publishers are skipped, and *prefer the original publication* tags
  the original issue.
- Individual issues are preferred over collected editions (TPB, hardcover, omnibus). Books that were already scraped can be
  skipped.
- Learns as it goes: the series of a book is remembered between runs, and a volume accepted for one book is tried first for the
  related series names of the others.

**Tagging (same data as the Python Comic Vine Scraper)**
- Same fields, roles, imprint table, summary cleanup and date rules, plus the `CVDB<id>` key and the `comicvine_issue` /
  `comicvine_volume` custom values.
- You choose which fields are written, whether existing values are replaced, whether empty values may replace existing
  ones, and whether imprints are converted. Also: the key in Tags and/or Notes, publisher aliases and custom imprints.
- Optional page types: the matched page is marked as the front cover and the others as story pages.

**Modes and windows**
- **Automatic** or **manual** mode, chosen before every run or fixed in the settings. In manual mode every book goes to the
  review with the match found.
- The progress window shows your cover next to the best candidate, with the confidence, live counters and the Comic Vine calls
  used. *Cancel* keeps the work already done.
- **Manual review window**:
  - Series and their issues, with sortable columns, a match percentage and cover similarity.
  - Alternative covers and a link to open the issue on Comic Vine.
  - A page viewer with zoom, page types and copying of a page.
  - "This page is the cover" (detected automatically) and "use this page as the cover and search again".
  - *Previous book*, *Use for the rest* (tags the other books of the series by their own numbers), and *Edit data* before
    writing.
  - Unwanted publishers can be added or removed with a right click.
- **Research with AI**: Claude studies the page on screen and the context you give it (notes, links) and fills in the
  identification and the credits. Nothing is written until you check it.

**Comic Vine API budget**
- A disk cache, one call to list a volume, remembered series, and alternative covers only when they are needed.
- Counts the calls of the last hour per resource and waits (with a countdown you can cancel) before the 200/hour limit. Every
  call is listed in `calls.log`.
- Claude's answers are remembered by the fingerprint of the cover (`claude-cache.json`), so the same cover is never sent twice.

**Local Comic Vine database (optional)**
- A SQLite copy of Comic Vine (SQLite is embedded in the DLL) answers the searches, issues, credits and descriptions with no
  calls. The search is much more thorough, and it works without an API key.

**Other**
- English, Spanish and French, following ComicRack's language. Other languages can be added with a `languages\<code>.json`
  file.
- DPI-aware windows with adjustable text size, and a log of each run in `plugin.log`.
- A single DLL. It reads no other plugin's configuration.

The sections below explain each part in detail.

## How it works
For each selected book, one by one:
1. **Hints**: current metadata (series, number, year, publisher) plus the file name.
2. **Comic Vine search**: candidate volumes are scored by name/year/publisher, then the issue is fetched.
3. **Cover comparison**: the first page is compared (perceptual hash + correlation) with the Comic Vine cover and
   its alternative covers. The cover decides the confidence.
4. **Claude fallback**: if the confidence is below the threshold, `claude -p` looks at the cover and names the
   (original) publication, and the search is repeated with that answer.
5. **Tagging** when the confidence reaches the threshold (default 80%). Same fields and the same
   `comicvine_issue` / `comicvine_volume` custom values as the Python scraper.
6. **Leftovers**: books that were not sure enough are skipped and, at the end, shown one by one in a search
   form (series, number, publisher, year) preloaded with what the AI found.

Comic Vine calls are minimized: everything is cached on disk, books of one series reuse the volume found for the
previous one, only the needed fields are requested, and alternative covers are fetched only when the main cover
does not match. Requests are spaced out and retried when Comic Vine asks to slow down.

## Same behavior as the Python scraper
The plugin is meant to replace the Python Comic Vine Scraper, so tagging follows the same rules (a port of `comicbook.py`,
`pluginbookdata.py` and `cvdb.py`): same field mapping and role table, imprint-to-publisher table, summary cleaning, date
rules, overwrite / ignore-blanks rules, and the same `CVDB<id>` key plus the `comicvine_issue` / `comicvine_volume` values.

It has its **own settings** (*Metadata* tab) and does not read the configuration of any other plugin: which fields are
written, replace existing values, never replace with an empty value, convert imprints, key in Tags / Notes (with or
without the date), publisher aliases and custom imprints. The list of local publishers to skip (Panini, Zinco...) is in the
*General* tab. Defaults: every field on, replace values, never replace with an empty value, key in Tags and not in Notes.

## Languages
The windows come in English, Spanish and French. The language follows the one ComicRack runs in (*Language* in the
settings can force another one). Anything missing in a language is shown in English.

To add or correct a language without compiling, put a file `languages\<code>.json` in the plugin folder (for example
`languages\de.json`). It has the same format as `Languages\en.json` of the source, and only the texts that change are needed; the
file also needs `"language.name"`. It then shows up in the *Language* list. A file for a built in language overrides its texts.

## Cancel
Cancel stops the search but not the work done: the books that were not sure enough, and the one in progress, are still
offered in the manual review window. Books that were already tagged stay tagged.

## Claude answers are remembered
The cover that was sent to Claude is recognized by its fingerprint, so the same cover (a book that is processed again, a second
copy of the issue, a slightly different scan of it) is not sent twice. The answers are in `claude-cache.json`
in the plugin folder, kept for 180 days; delete the file to ask Claude again.

## Local Comic Vine database (optional)
In *General* you can choose a local copy of the Comic Vine data (a SQLite file such as the ones other tools extract from it).
The plugin stays a single DLL: SQLite (official build, public domain, 32 and 64 bits) is embedded in it and written once to
`%LOCALAPPDATA%\ComicVineAI\native`. With a database the plugin asks it instead of the API: series search (full text, also
for names with extra words and for stuck-together words like "Spiderman"), the issues of a series, credits and descriptions.
It costs no calls and has no hourly limit, so the search is much more thorough: every series that looks like the cleaned
file name (and the shorter forms of it, "Catwoman Officer Down" -> "Catwoman") is a candidate, many more are compared, and
Claude is only asked when all of that finds nothing.

The database is a snapshot (the Settings page says when it was synchronized). The API is asked only for what the
database does not have: a search that finds nothing, or an issue number beyond the last one of a series that was still
being published when the snapshot was taken (a number inside the range that is missing really is missing). If the API cannot
answer (no key, hourly limit), the local answer stands. Without a database nothing changes.

## While a book is searched
The progress window shows the best candidate found so far for the book (its cover next to yours, the confidence bar and its name)
as soon as the first cover has been compared, and replaces it when a better one turns up. The space is not empty while the search
goes on (for example while Claude is thinking).

## Cover comparison
The first page of the book is compared with the cover of Comic Vine (and with its alternative covers when nothing is accepted yet)
cell by cell on a small grid of grays, after cutting a few percent off the edges of either one (a scan and the Comic Vine image are
framed differently) and ignoring the 20% of the cells that differ most (a translation group's logo strip, a barcode box). The
Python scraper hashes the whole image to 8x8 bits and calls two covers the same above 87%. In a test with 120 covers that were
cropped, padded, rescaled, recompressed and marked, the same cover scores 85% on average here (65% with a global hash), while the
next issue of the same series stays far below. Where it counts in the confidence is set so that a cover has to reach about 90%
to be a sure match, and a score of 75% (the highest a different issue got in the test) is not enough on its own.

## Unwanted publishers
*General* has the list of unwanted publishers (the local editions: Panini, Zinco...); their series are skipped by the search. In the
review window, right click a series and choose *Add "Publisher" to the unwanted publishers* (or remove it): it takes effect at
once and is saved in the settings. The *Hide unwanted publishers* box of the window shows them again if needed.

## What a choice teaches the rest of the review
When you pick the right series by hand for a book, the books that are still waiting are looked at again at once, with no search:
the ones whose series name is the same (as written in the library) or related ("Daredevil Shadowland" for "Daredevil") are tagged
with that volume if the issue exists there and the cover agrees, and are not asked any more. The ones that do not agree stay in
the queue, with the volume preselected in the window. Going back with *Previous book* shows a book even if it was tagged this way.

## Manual review
The review window works in two steps: the series found (click one to see its cover, click a column header to sort), then,
with double click or the button, the issues of that series, where the right one is chosen (the number box preselects it).
It also has the pages of the comic with zoom to read what the publisher prints. *Previous book* goes back to the book
before, to change what was chosen for it. When the issue has alternative covers, a button shows them one by one; they are
only downloaded when asked for.

## Comic Vine call budget
Comic Vine allows 200 requests per hour for each resource (`/search`, `/issues`, `/issue`, `/volume`), and the limit is
per API key. To stay well under it:
- A volume of up to 100 issues is listed with one call (cached on disk): every book of the series, and every number that
  does not exist, is answered from that list. Bigger volumes are asked number by number until listing them is cheaper.
- **The series of a book is remembered**, like the scrape cache of the Python scraper: once `abc 4.cbr` is matched with
  "Batman vol. 1" (by the search, by Claude or by hand in the review window), `abc 5.cbr` goes straight to that volume without
  a search. The memory is keyed by the series name as it is written in the library (metadata or file name, plus the volume
  when the file has one), not by the name of the volume, and it is kept between runs (`series-choices.json`, 90 days). The
  cover still has to agree before a book is accepted; a volume that does not have the number is forgotten (it was probably
  another series). In the review window the remembered volume comes preselected, and a book whose series was chosen by hand
  for an earlier book of the same review is tagged automatically if the cover agrees.
- A volume accepted for one book is also tried first for books whose series name is related to the name of the volume
  ("Catwoman Year Two" for "Catwoman"), so a batch of one series needs one search and no more lists, even when every file
  name spells the series differently.
- The candidate volumes whose numbering (first and last issue, free with the search) contains the number are asked first,
  and the search stops at the first candidate that reaches the acceptance threshold. No volume is ever left out: a volume
  can start at #300.
- Alternative covers (one more call per issue) are fetched only when no candidate was accepted, for the two best
  candidates of the right series, whatever the main cover looks like (a variant cover can be nothing like it).
- `usage.json` counts the calls of the last hour per resource. At 190 of 200 the plugin waits (with a countdown, cancellable)
  instead of failing. If the server still refuses (another tool uses the same key) it waits and retries.
- `calls.log` lists every real call with the book and the stage (metadata / claude) that caused it.
- The progress window shows the calls of the run and of the last hour.

## Settings (Configure action, stored in `settings.json`)
- Comic Vine API key (get yours at https://comicvine.gamespot.com/api/) and/or a local Comic Vine database: one of the two is
  needed. With the database alone nothing goes to the API.
- Accept threshold, prefer the original publication (for localized reprints), cover comparison on/off
- Claude CLI on/off, executable path and model. **The cover image is sent to Claude when this is on.**
- Ask "automatic or manual?" before every run (on by default), and manual mode: every book goes to the review window, with the
  match percentage of what the search found.

## Research with AI (manual review window)
The "Research with AI..." button asks Claude about the page on screen and about a context that you write: notes, links to a wiki or
the publisher's page, text copied from there. **The cover, the file name, your library data and the context are sent to Claude, and
Claude may open the links of the context** (its web fetch tool; and Chrome through Claude in Chrome when the setting allows it, for
pages it cannot read directly). It fills in the identification and also creators, dates, summary, characters, teams, locations and
story arcs when its sources say them. Nothing is written until you check and correct everything in the window; the books written
this way have no Comic Vine key and get the tag `ClaudeInfo`. The button is enabled only when "use Claude" is on and the `claude`
executable is found. Without Claude the plugin works the same, only the books it is not sure about go to the review.

## Install
Download `ComicVineAI-vX.Y.Z.zip` from the Releases page, close ComicRack and unpack the `Comic Vine AI` folder into the `Scripts`
folder of your ComicRack profile (the one next to `ComicDb.xml`). The plugin is a single DLL and needs a ComicRack Community Edition
build with .NET plugin support (the `feature/dotnet-plugin` branch of ComicRackCE).

## Version
The version (see `<Version>` in `ComicVineAI.csproj`) shows in the title of every window of the plugin, in the plugin description
of ComicRack and in the first line of the log of each run. Please include it when you report a problem.

## Build
Build ComicRackCE from the `feature/dotnet-plugin` branch first. The project looks for its DLLs in
`..\ComicRackCE\ComicRack\bin\Release\net48\`; if yours are elsewhere pass `-p:ComicRackBin=<path>\` (with the final backslash):

    dotnet build -c Release -p:ComicRackBin=<path to ComicRack\bin\Release\net48>\

To make the zip for ComicRack (`dist\ComicVineAI-vX.Y.Z.zip`):

    .\package.ps1 -ComicRackBin <path to ComicRack\bin\Release\net48>\
