# yt-dlp_web Review Bug Fixes – Implementation Plan

> **For agentic workers (Google Antigravity or similar):** Work through this plan one task at a time, in order. Read `AGENTS.md` first; its rules apply to every task. Each task ends with a passing test run (or the manual check it names) and a commit. Do not start a task until the previous one is committed. Steps use checkbox (`- [ ]`) syntax; tick them as you go. If your agent has the superpowers skills installed, use `superpowers:executing-plans`.

**Goal:** Fix the bugs found in the 2026-10-04 code review: command injection, the committed key, wrong-file results, runaway downloads, broken non-Docker hosting, log corruption, progress display, loopback HTTP calls, and stale deployment files.

**Architecture:** Move logic that can be tested out of `DownloadService`, `Program.cs` and the Razor pages into small static helpers and one `IDownloadStore` service under `Services/`, then call those from the existing code. Add one xUnit test project. Nothing changes the app's hosting model: Blazor Server, files under `config/`, and Docker as the main deployment.

**Tech Stack:** ASP.NET Core 8 (`net8.0`), Blazor Interactive Server, xUnit, Docker (Alpine), yt-dlp, Caddy (optional).

**Spec:** The code review findings (summarised in the table below) and `AGENTS.md` at the repo root.

## Findings being fixed

| ID | Finding | Task |
| --- | --- | --- |
| F1 | yt-dlp command line built by string concatenation; a crafted URL/format can inject `--exec` (remote code execution) | 1 |
| F2 | ASP.NET Data Protection key committed in `config/keys/` | 0 |
| F3 | Output file found by per-second timestamp; concurrent downloads get each other's files | 3 |
| F4 | yt-dlp path hard-coded to `/usr/local/bin/yt-dlp`; `Process.Start` failure is unhandled | 5 |
| F5 | No cancellation, timeout, or concurrency cap; live streams download forever | 6 |
| F6 | One malformed log line hides every log entry; log file grows without limit | 7 |
| F7 | Progress template uses `|` (literal default), and `N/A` is not filtered, so total size is wrong | 4 |
| F8 | Pages call the app's own API through a loopback `HttpClient` | 8 |
| F9 | Caddyfile proxies to the wrong port and paths; PWA files are copied but never used | 10 |
| F10 | File-name path check has no trailing separator and decodes the name twice | 2 |
| – | `/api/download` accepts `ClientIp` from the request body (log forging) | 9 |

## Out of scope

- **Access control (login, tokens).** Deliberately not part of this plan. Operators protect the app with their reverse proxy or keep it on a private network. Do not add authentication.
- **Rewriting git history** to remove the committed key. Task 0 stops tracking it. Rewriting history and force-pushing is the repo owner's call.
- Moving to a newer .NET version.

## Global Constraints

- Target framework stays `net8.0` for the app and the test project. The dev machine has .NET SDK 10 and the .NET 8 runtime, so `dotnet test` works locally.
- Never use `ProcessStartInfo.Arguments`; always use `ProcessStartInfo.ArgumentList`.
- File-scoped namespace `yt_dlp_web.Services` for all new service/helper files; new types are `public`.
- Do not change the existing JSON shapes returned by `/api/download` and `/api/downloads`.
- Keep the dark and light themes working in any markup you touch; reuse existing CSS classes and variables.
- Log user-visible failures through `ILoggingService.LogError(message, clientIp, "Download" | "Update")`.
- Nothing under `config/` is committed after Task 0.
- Test command for every task: `dotnet test tests/yt-dlp_web.Tests/yt-dlp_web.Tests.csproj`. Build command: `dotnet build yt-dlp_web.slnx`.

## Review Focus

These are the inputs most likely to hurt real users that the code paths above could miss. Each one has a test in the task named.

1. **Titles with `%`, `#`, `?`, spaces, or non-ASCII characters**: the Download, Play and Delete actions must all still work → `TryResolve_AcceptsUnusualButValidNames` (Task 2).
2. **Very long titles (over 255 bytes in UTF-8)**: the download must not fail with "File name too long" → `OutputTemplate_TruncatesTitle` (Task 3).
3. **Two users downloading at the same moment, even the same URL**: each must get their own file → `Find_IgnoresOtherJobsAndPartialFiles` (Task 3).
4. **A playlist or a live-stream URL**: one video is downloaded, or a clear "live streams are not supported" error is shown → `Build_DisablesPlaylistsAndLiveStreams` (Task 6) plus the manual check in Task 6.
5. **yt-dlp missing or not executable** (non-Docker hosting, bad config): show a clear error instead of a 500 → `ResolveExecutablePath_*` (Task 5) plus the manual check in Task 5.

---

### Task 0: Branch, commit AGENTS.md, stop tracking `config/`

**Files:**
- Modify: `.gitignore` (append)
- Untrack: `config/keys/key-767ccfc1-4ef2-457c-9f85-82fdaf85e914.xml`
- Add: `AGENTS.md`, this plan

- [x] **Step 1:** `git checkout -b fix/review-bug-fixes`
- [x] **Step 2:** Append to `.gitignore`:
  ```
  # Runtime data (Data Protection keys, logs, downloads)
  /config/
  ```
- [x] **Step 3:** `git rm --cached -r config/` (the file stays on disk).
- [x] **Step 4:** Verify: `git status --short` shows the key as `D` and `.gitignore` as `M`; `git check-ignore config/keys/x.xml` prints the path.
- [x] **Step 5:** Commit: `git add .gitignore AGENTS.md docs/superpowers/plans && git commit -m "chore: stop tracking config/, add AGENTS.md and fix plan"`
- [ ] **Step 6 (human, not the agent):** On every deployed instance, stop the container, delete `config/keys/*.xml`, and start it again. A new key is generated. Users will need to reload open tabs once.

---

### Task 1: Test project and safe yt-dlp argument building (F1)

**Files:**
- Create: `tests/yt-dlp_web.Tests/yt-dlp_web.Tests.csproj`
- Create: `yt-dlp_web/yt-dlp_web/Services/YtDlpArguments.cs`
- Create: `tests/yt-dlp_web.Tests/YtDlpArgumentsTests.cs`
- Modify: `yt-dlp_web/yt-dlp_web/Services/DownloadService.cs:77-162`
- Modify: `yt-dlp_web.slnx`, `.dockerignore` (add `tests/`)

**Interfaces:**
- Produces:
  ```csharp
  public static class YtDlpArguments
  {
      public static readonly IReadOnlySet<string> AudioFormats;      // {"mp3","m4a","aac"}
      public static readonly IReadOnlySet<string> SubtitleLanguages; // {"en","all"}
      public const string ProgressTemplate = /* current string from DownloadService.cs:114, unchanged */;
      // Returns null when valid, otherwise the user-facing error message. uri is non-null when valid.
      public static string? Validate(DownloadRequest req, out Uri? uri);
      // Caller passes denoPath only if the file exists.
      public static List<string> Build(DownloadRequest req, Uri uri, string outputTemplate, string? denoPath);
  }
  ```

- [x] **Step 1: Scaffold the test project**
  ```bash
  dotnet new xunit -o tests/yt-dlp_web.Tests -f net8.0
  dotnet add tests/yt-dlp_web.Tests reference yt-dlp_web/yt-dlp_web/yt-dlp_web.csproj
  dotnet sln yt-dlp_web.slnx add tests/yt-dlp_web.Tests/yt-dlp_web.Tests.csproj
  ```
  Add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to the test csproj if the build complains about ASP.NET types. Delete the template's `UnitTest1.cs`. Add `tests/` to `.dockerignore`.

- [x] **Step 2: Write the failing tests** in `YtDlpArgumentsTests.cs`:
  ```csharp
  [Fact] public void Validate_MaliciousUrl_IsEscapedIntoASingleArgument()
  {
      var req = new DownloadRequest { Url = "https://youtu.be/x\" --exec \"before_dl:sh -c 'id'\"" };
      Assert.Null(YtDlpArguments.Validate(req, out var uri));
      var args = YtDlpArguments.Build(req, uri!, "/dl/%(title)s.%(ext)s", null);
      Assert.DoesNotContain("--exec", args);
      Assert.Equal("--", args[^2]);
      Assert.Equal("https://youtu.be/x%22%20--exec%20%22before_dl:sh%20-c%20'id'%22", args[^1]);
  }
  [Theory] [InlineData(null)] [InlineData("")] [InlineData("ftp://x/y")] [InlineData("not a url")]
  public void Validate_RejectsMissingOrNonHttpUrl(string? url)  // error is "Missing url" for null/empty, "Invalid url" otherwise
  [Fact] public void Validate_RejectsUnknownAudioFormat()        // AudioFormat = "mp3 --exec x" → "Unsupported audio format"
  [Fact] public void Validate_RejectsUnknownSubtitleLanguage()   // SubLangs = "en,--exec" → "Unsupported subtitle language"
  [Theory] [InlineData("--exec")] [InlineData("best video")] [InlineData("b\"")]
  public void Validate_RejectsBadFormat(string format)           // → "Invalid format"
  [Fact] public void Validate_AcceptsTypicalFormatSelector()     // "bv*[height<=1080]+ba/b" → null
  [Fact] public void Build_ExtractAudio_AddsAudioOptions()       // ExtractAudio, AudioFormat "m4a" → contains sequence "-x","--audio-format","m4a"
  [Fact] public void Build_DefaultsAudioAndSubtitleValues()      // AudioFormat/SubLangs null → "mp3" / "en"
  [Fact] public void Build_AddsDenoRuntimeOnlyWhenGiven()        // denoPath "/d/deno" → contains "--js-runtimes","deno:/d/deno"; null → no "--js-runtimes"
  ```

- [x] **Step 3:** Run the tests. Expected: compile failure, `YtDlpArguments` not defined.

- [x] **Step 4: Implement `YtDlpArguments`.** Validation rules: URL must parse with `Uri.TryCreate(..., UriKind.Absolute)` and be `http`/`https`. `AudioFormat`/`SubLangs` must be null or in the sets (case-sensitive). `Format` must be null/empty or match `^[A-Za-z0-9_+/,.\[\]<>=*:!?-]{1,200}$` and not start with `-`. `Build` keeps the current option order from `DownloadService.cs:107-151` and ends with `"--", uri.AbsoluteUri`. It never uses `req.Url`.

- [x] **Step 5: Use it in `DownloadService.DownloadAsync`.** Replace lines 81-151 with `Validate` (log + return failure on error) and `Build`. Pass `_denoPath` only when `File.Exists(_denoPath)`. Replace `Arguments = string.Join(...)` with a loop over `psi.ArgumentList.Add(arg)`.

- [x] **Step 6:** Run the tests. Expected: all pass. Then `dotnet build yt-dlp_web.slnx`. Expected: 0 errors.

- [x] **Step 7:** Commit: `fix: build yt-dlp arguments with ArgumentList and allow-lists (F1)`

---

### Task 2: One download-folder store and a safe path check (F10)

**Files:**
- Create: `yt-dlp_web/yt-dlp_web/Services/DownloadStore.cs`
- Create: `tests/yt-dlp_web.Tests/DownloadStoreTests.cs`
- Modify: `yt-dlp_web/yt-dlp_web/Program.cs:63-64, 125-221`
- Modify: `yt-dlp_web/yt-dlp_web/Services/DownloadService.cs:67-75` (constructor)

**Interfaces:**
- Produces:
  ```csharp
  public sealed record DownloadFileInfo(string Name, long Size, DateTime Modified, string DownloadUrl);
  public interface IDownloadStore
  {
      string RootPath { get; }                                   // absolute, no trailing separator
      bool TryResolve(string? fileName, out string fullPath);    // never URL-decodes
      IReadOnlyList<DownloadFileInfo> List();                    // newest first; DownloadUrl = $"download/{Uri.EscapeDataString(name)}"
      bool Delete(string fileName);                              // false only when the name is invalid; true if deleted or already absent
      int DeleteAll();                                           // number of files deleted
  }
  public sealed class DownloadStore : IDownloadStore { public DownloadStore(string rootPath); } // creates the directory
  ```

- [x] **Step 1: Write the failing tests** (each test uses a fresh temp directory as the store root, deleted in `Dispose`):
  ```csharp
  [Fact] TryResolve_PlainName_ReturnsPathInsideRoot            // "video.mp4" → true, fullPath == Path.Combine(root, "video.mp4")
  [Theory] [InlineData("")] [InlineData(" ")] [InlineData(".")] [InlineData("..")]
           [InlineData("../downloads_old/x.mp4")] [InlineData("sub/x.mp4")]
  TryResolve_RejectsTraversalAndSubpaths(string name)           // → false
  [Fact] TryResolve_RejectsSiblingFolderWithSamePrefix          // root ".../downloads", name "../downloads_old" → false
  [Fact] TryResolve_RejectsAbsolutePath                         // Path.Combine(Path.GetTempPath(), "x.mp4") → false
  [Theory] [InlineData("a%41b.mp4")] [InlineData("50% off #1?.mp4")] [InlineData("Ünïcødé – title.webm")]
  TryResolve_AcceptsUnusualButValidNames(string name)          // → true, Path.GetFileName(fullPath) == name (no decoding)
  [Fact] Delete_InvalidName_ReturnsFalseAndDeletesNothing       // file "keep.mp4" exists; Delete("../keep.mp4") false; file still exists
  [Fact] List_ReturnsNewestFirstWithEscapedDownloadUrl          // two files, set LastWriteTime; "a b.mp4" → DownloadUrl "download/a%20b.mp4"
  ```
  `"?"` is not a valid file-name character on Windows. Mark that case with `[Trait("os","linux")]`, or skip it with `if (OperatingSystem.IsWindows()) return;`.

- [x] **Step 2:** Run the tests. Expected: compile failure.

- [x] **Step 3: Implement `DownloadStore`.** `TryResolve` rejects null/whitespace, `"."`, `".."`, and any name where `Path.GetFileName(name) != name`. It then takes `Path.GetFullPath(Path.Combine(RootPath, name))` and requires `Path.GetDirectoryName(full)` to equal `RootPath` (`OrdinalIgnoreCase` on Windows, `Ordinal` otherwise). Catch exceptions from `GetFullPath` and return false.

- [x] **Step 4: Rewire `Program.cs`.** Register `builder.Services.AddSingleton<IDownloadStore>(new DownloadStore(Path.Combine(contentRoot, "config", "downloads")))`. Take the downloads path for `UseStaticFiles` from that store instance. Rewrite `GET /download/{fileName}`, `GET /api/downloads`, `DELETE /api/downloads/{fileName}` and `DELETE /api/downloads` to use the store, with no `Uri.UnescapeDataString`. Keep the existing status codes: 404 for invalid/missing on GET, 400 for an invalid name on DELETE, 200 otherwise. `GET /api/downloads` returns `store.List()` (serialises to the same camelCase shape).

- [x] **Step 5:** `DownloadService` constructor takes `IDownloadStore store` instead of `IWebHostEnvironment` and uses `store.RootPath` as `_downloadsPath`.

- [x] **Step 6:** Run the tests and build. Expected: all pass, 0 errors.

- [x] **Step 7:** Commit: `fix: single download store with strict path check (F10)`

---

### Task 3: Find the right output files with a per-job token (F3)

**Files:**
- Create: `yt-dlp_web/yt-dlp_web/Services/MediaFileTypes.cs`
- Create: `yt-dlp_web/yt-dlp_web/Services/DownloadOutputLocator.cs`
- Create: `tests/yt-dlp_web.Tests/MediaFileTypesTests.cs`, `tests/yt-dlp_web.Tests/DownloadOutputLocatorTests.cs`
- Modify: `yt-dlp_web/yt-dlp_web/Services/YtDlpArguments.cs`, `DownloadService.cs:102-104, 225-270`

**Interfaces:**
- Consumes: `YtDlpArguments.Build(req, uri, outputTemplate, denoPath)` (Task 1).
- Produces:
  ```csharp
  public static class MediaFileTypes
  {
      // Lowercase extension sets, including the leading dot:
      // Audio: .mp3 .m4a .aac .wav .flac .ogg .opus
      // Video: .mp4 .webm .mkv .mov .avi .flv .m4v
      // Image: .jpg .jpeg .png .webp .gif .bmp
      // Subtitle: .vtt .srt .ass .ssa .sub .lrc
      // Partial: .part .ytdl .temp, or name contains ".part-Frag"
      public static bool IsAudio(string fileName); IsVideo; IsImage; IsSubtitle; IsPartial;
      public static bool IsPlayable(string fileName);              // IsAudio || IsVideo
      public static string? SubtitleLanguage(string fileName);     // used in Task 8
  }
  public sealed record LocatedOutputs(string? MediaPath, string? ThumbnailPath, IReadOnlyList<string> SubtitlePaths);
  public static class DownloadOutputLocator
  {
      public static LocatedOutputs Find(string downloadsPath, string jobToken);
  }
  // added to YtDlpArguments:
  public static string NewJobToken();                                   // $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..24]
  public static string OutputTemplate(string downloadsPath, string jobToken); // Path.Combine(downloadsPath, $"%(title).150B_{jobToken}.%(ext)s")
  ```

- [x] **Step 1: Write the failing tests:**
  ```csharp
  // MediaFileTypesTests
  [Theory] [InlineData("a.OPUS", true)] [InlineData("a.mp4", false)] IsAudio_IsCaseInsensitive
  [Theory] [InlineData("a.mp4.part")] [InlineData("a.f137.mp4.ytdl")] [InlineData("a.mp4.part-Frag12")] IsPartial_True
  [Theory] [InlineData("t_tok.en.vtt","en")] [InlineData("t_tok.en-US.srt","en-US")]
           [InlineData("Mr. Robot_tok.vtt", null)] [InlineData("t_tok.vtt", null)]
  SubtitleLanguage_ReadsLanguageSegment(string name, string? expected)

  // DownloadOutputLocatorTests (temp dir)
  [Fact] Find_IgnoresOtherJobsAndPartialFiles
  //   files: "A_tok1.mp4", "A_tok1.webp", "A_tok1.en.vtt", "A_tok1.f137.mp4.part",
  //          "B_tok2.mp4" (LastWriteTime newest), "A_tok10.mp4"
  //   Find(dir, "tok1") → MediaPath ends "A_tok1.mp4", ThumbnailPath ends "A_tok1.webp",
  //                        SubtitlePaths single item ending "A_tok1.en.vtt"
  [Fact] Find_NoMatch_ReturnsNullMedia                        // Find(dir,"zzz").MediaPath == null, SubtitlePaths empty

  // YtDlpArgumentsTests
  [Fact] OutputTemplate_TruncatesTitle                        // contains "%(title).150B_" + token + ".%(ext)s"
  [Fact] NewJobToken_IsUniqueWithinOneSecond                  // 1000 calls → 1000 distinct values
  ```

- [x] **Step 2:** Run the tests. Expected: compile failure.

- [x] **Step 3: Implement.** `Find` matches files whose name contains `$"_{jobToken}."` (ordinal), drops `IsPartial`, picks media = not image and not subtitle, newest by `LastWriteTimeUtc`; thumbnail = newest image; subtitles ordered by name. The `"_tok1."` match is what keeps `A_tok10.mp4` out.

- [x] **Step 4: Use it in `DownloadService`.** Replace the timestamp (lines 102-104) with `NewJobToken()`/`OutputTemplate(...)`. Replace lines 225-270 with `DownloadOutputLocator.Find`. Keep the "could not find downloaded file" error, but list only files containing the token. Delete the local `imageExts`/`subExts` arrays.

- [x] **Step 5:** Run the tests and build. Expected: pass, 0 errors.

- [x] **Step 6:** Commit: `fix: locate yt-dlp output by unique job token (F3)`

---

### Task 4: Progress parsing and the template fix (F7)

**Files:**
- Create: `yt-dlp_web/yt-dlp_web/Services/ProgressParser.cs`
- Create: `tests/yt-dlp_web.Tests/ProgressParserTests.cs`
- Modify: `DownloadService.cs` (remove `ParseOutputLine`, `ProgressTrackingState` and the regex fields, lines 58-65 and 308-524); `YtDlpArguments.cs` (`ProgressTemplate` → `ProgressParser.Template`)

**Interfaces:**
- Produces:
  ```csharp
  public sealed class ProgressParser   // one instance per download; not thread-safe, so lock around Parse (stdout and stderr readers both call it)
  {
      public const string Template =
          "download-progress:%(progress._percent_str)s|%(progress._total_bytes_str)s|%(progress._total_bytes_estimate_str)s|%(progress._speed_str)s|%(progress._eta_str)s";
      // Returns the update to emit, or null when the line is unrecognised or throttled.
      public DownloadProgressUpdate? Parse(string line, DateTime utcNow);
  }
  ```
  yt-dlp prints a missing value as `N/A` (sometimes `NA`, and `Unknown…` for speed/ETA). It pads fields with spaces. Treat all of these as missing. Total = the first present of total and estimate.

- [x] **Step 1: Write the failing tests** (`t0 = new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc)`):
  ```csharp
  [Fact] Parse_UsesEstimateWhenTotalIsNA
  //   "download-progress: 50.0%|       N/A|  10.00MiB|  1.00MiB/s|00:05"
  //   → Percent 50, TotalSize "10.00MiB", DownloadedSize "5.0 MiB", Speed "1.00MiB/s", Eta "00:05", IsIndeterminate false
  [Fact] Parse_PrefersExactTotal          // "…25.0%|  20.00MiB|  19.00MiB|…" → TotalSize "20.00MiB", DownloadedSize "5.0 MiB"
  [Fact] Parse_AllSizesMissing            // both "N/A" → TotalSize null, DownloadedSize null
  [Fact] Parse_UnknownSpeedIsNull         // speed "Unknown B/s", eta "NA" → Speed null, Eta null
  [Fact] Parse_ThrottlesSmallFastChanges  // 10.0% at t0 → update; 10.1% at t0+100ms → null; 100.0% at t0+150ms → update with Eta "00:00"
  [Fact] Parse_StageLines                 // "[Merger] Merging formats into \"x.mkv\"" → Percent 95; "[ExtractAudio] Destination: x.mp3" → 92
  [Fact] Parse_UsesInvariantCulture       // set CultureInfo.CurrentCulture = de-DE; DownloadedSize "5.0 MiB" (dot, not comma)
  ```

- [x] **Step 2:** Run the tests. Expected: compile failure.

- [x] **Step 3: Implement `ProgressParser`.** Move the throttling (≥0.2 % change or ≥250 ms; always emit 100 %) and the 1.2 s speed/ETA smoothing over unchanged. Drop the `[download] NN%` fallback: with `--progress-template` set, yt-dlp does not print those lines. Keep the `[download] Destination:` stage (10 %) and the other stage lines. Format `DownloadedSize` with `CultureInfo.InvariantCulture`.

- [x] **Step 4:** In `DownloadService`, create one `ProgressParser` per call. Both reader tasks call `Parse` under a shared `lock` and invoke `onProgress` when the result is non-null.

- [x] **Step 5:** Run the tests and build. Expected: pass, 0 errors.

- [x] **Step 6:** Commit: `fix: correct progress template and parse N/A sizes (F7)`

---

### Task 5: Configurable yt-dlp path, start errors, and serialised updates (F4)

**Files:**
- Create: `yt-dlp_web/yt-dlp_web/Services/YtDlpOptions.cs`
- Create: `tests/yt-dlp_web.Tests/YtDlpOptionsTests.cs`
- Modify: `DownloadService.cs` (constructor, `Process.Start`, PATH), `UpdateService.cs`, `Program.cs:45-48` (registration), `appsettings.json`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class YtDlpOptions
  {
      public const string SectionName = "YtDlp";
      public string? ExecutablePath { get; set; }
      public string? DenoPath { get; set; }
      // configured value if non-blank; else Windows: Path.Combine(AppContext.BaseDirectory, "tools", "yt-dlp.exe"); else "/usr/local/bin/yt-dlp"
      public string ResolveExecutablePath();
  }
  // DownloadService(ILoggingService logger, IDownloadStore store, IOptions<YtDlpOptions> options)
  // UpdateService(ILoggingService logger, ILogger<UpdateService> log, IOptions<YtDlpOptions> options)
  ```

- [x] **Step 1: Write the failing tests:**
  ```csharp
  [Fact] ResolveExecutablePath_UsesConfiguredValue      // ExecutablePath "/opt/yt-dlp" → "/opt/yt-dlp"
  [Fact] ResolveExecutablePath_DefaultsPerOs            // blank → Windows: ends with Path.Combine("tools","yt-dlp.exe"); else "/usr/local/bin/yt-dlp"
  ```
- [x] **Step 2:** Run the tests. Expected: compile failure.
- [x] **Step 3: Implement and wire.** `builder.Services.Configure<YtDlpOptions>(builder.Configuration.GetSection(YtDlpOptions.SectionName))`. Change the `UpdateService` factory registration to pass `IOptions<YtDlpOptions>`. In `appsettings.json`, set `"YtDlp": { "ExecutablePath": "", "DenoPath": "" }`; this removes the personal Windows path.
- [x] **Step 4: Start errors.** In both services, wrap `Process.Start` in `try/catch (System.ComponentModel.Win32Exception ex)`. Log and return `$"yt-dlp could not be started at {path}: {ex.Message}"` (`LogError(..., "Download")` / `LogUpdate(..., false)`). Build the deno PATH prefix with `Path.PathSeparator` instead of `';'`.
- [x] **Step 5: Serialise updates.** In `UpdateService`, add `private readonly SemaphoreSlim _runLock = new(1, 1);`. `RunUpdate` does `if (!await _runLock.WaitAsync(0)) return new UpdateResult { Success = false, Message = "An update is already running" };` and releases in `finally`. Read stdout and stderr together: `await Task.WhenAll(outTask, errTask)` before `WaitForExitAsync`.
- [x] **Step 6:** Run the tests and build. Expected: pass, 0 errors.
- [x] **Step 7 (manual):** `docker compose up --build -d`, then download a short video. It must work as before. Then run once with `YtDlp__ExecutablePath=/nope` added to `docker-compose.yml` `environment`. Home must show "yt-dlp could not be started at /nope…" (no 500), and Logs must show the error. Remove the override afterwards.
- [x] **Step 8:** Commit: `fix: configurable yt-dlp path, handle start failures, serialise updates (F4)`

---

### Task 6: Cancellation, timeout, concurrency cap, and no live streams (F5)

**Files:**
- Create: `yt-dlp_web/yt-dlp_web/Services/DownloadLimiter.cs`
- Modify: `YtDlpOptions.cs`, `YtDlpArguments.cs`, `DownloadService.cs`, `Program.cs` (`/api/download` endpoint, DI), `Components/Pages/Home.razor`, `docker-compose.yml`
- Test: `tests/yt-dlp_web.Tests/YtDlpArgumentsTests.cs`, `tests/yt-dlp_web.Tests/DownloadLimiterTests.cs`

**Interfaces:**
- Consumes: `YtDlpOptions` (Task 5), job token (Task 3).
- Produces:
  ```csharp
  // YtDlpOptions additions
  public int TimeoutMinutes { get; set; } = 180;
  public int MaxConcurrentDownloads { get; set; } = 4;   // 0 or less = no limit
  // IDownloadService
  Task<DownloadResult> DownloadAsync(DownloadRequest request, Action<DownloadProgressUpdate>? onProgress = null, CancellationToken cancellationToken = default);
  // DownloadService(ILoggingService logger, IDownloadStore store, IOptions<YtDlpOptions> options, DownloadLimiter limiter)
  // Singleton
  public sealed class DownloadLimiter
  {
      public DownloadLimiter(IOptions<YtDlpOptions> options);   // SemaphoreSlim(MaxConcurrentDownloads), or no semaphore when <= 0
      public bool IsSaturated { get; }                           // no free slot right now
      public Task WaitAsync(CancellationToken ct);
      public void Release();
  }
  ```

- [x] **Step 1: Write the failing tests:**
  ```csharp
  [Fact] Build_DisablesPlaylistsAndLiveStreams   // args contain "--no-playlist" and the sequence "--match-filter","!is_live"
  [Fact] DownloadLimiter_BlocksBeyondMax         // max 1: first WaitAsync completes; second is not completed; after Release() it completes; IsSaturated true while held
  [Fact] DownloadLimiter_WaitIsCancellable       // max 1, held; WaitAsync(cancelledToken) throws OperationCanceledException
  [Fact] DownloadLimiter_ZeroMeansUnlimited      // max 0: 50 WaitAsync calls all complete immediately; IsSaturated stays false; Release() never throws
  ```
- [x] **Step 2:** Run the tests. Expected: failures/compile errors.
- [x] **Step 3: Implement in `DownloadService`:**
  - Create a linked CTS from `cancellationToken`, then call `CancelAfter(TimeSpan.FromMinutes(TimeoutMinutes))`.
  - If `IsSaturated`, emit status "Waiting for another download to finish…" first, then `await limiter.WaitAsync(token)`. Release in `finally`.
  - Pass the token to `WaitForExitAsync`. On `OperationCanceledException`: `proc.Kill(entireProcessTree: true)` (ignore `InvalidOperationException`), `await proc.WaitForExitAsync(CancellationToken.None)`, delete every file in the downloads folder whose name contains `$"_{jobToken}."`, log, and return `Success = false` with `"Download cancelled"` or `$"Download timed out after {TimeoutMinutes} minutes"` (tell them apart with `cancellationToken.IsCancellationRequested`).
  - If no media file is found and stdout contains `does not pass filter`, return `"Live streams are not supported"`.
- [x] **Step 4: Endpoint.** `/api/download` takes `HttpContext ctx` and passes `ctx.RequestAborted`. Register `DownloadLimiter` as a singleton.
- [x] **Step 5: Home.razor.** Add `@implements IDisposable` and a `CancellationTokenSource? downloadCts` field. Create it in `StartDownload`, pass `downloadCts.Token`, and dispose it in `finally`. Add a "Cancel" button in the progress card header (`btn btn-sm btn-outline-secondary`) that calls `downloadCts?.Cancel()`. `Dispose()` cancels and disposes the CTS, so closing the tab kills yt-dlp.
- [x] **Step 6: Expose the limits as environment variables.** In `docker-compose.yml` `environment`, add:
  ```yaml
  # Max yt-dlp downloads running at once; extra requests wait. 0 = no limit.
  - YtDlp__MaxConcurrentDownloads=${MAX_CONCURRENT_DOWNLOADS:-4}
  # Kill a download that runs longer than this many minutes.
  - YtDlp__TimeoutMinutes=${DOWNLOAD_TIMEOUT_MINUTES:-180}
  ```
  ASP.NET Core binds `YtDlp__X` to `YtDlp:X`, so no code is needed beyond the options class. Read the options once at startup; changing the value needs a container restart.
- [x] **Step 7:** Run the tests and build. Expected: pass, 0 errors.
- [x] **Step 8 (manual, Docker):** (a) Start a long video and click Cancel (passed in browser). (b) Repeat, but close the tab instead of clicking Cancel (passed in browser: job fb99d709 cancelled at 21:41:04 UTC, Chrome close cancelled 21:49:40 UTC, process tree terminated, downloads empty; brief-offline reconnect covered by unit tests + Blazor reconnect semantics, optional manual check). (c) Paste a YouTube live URL (passed, returned "Live streams are not supported"). (d) Restart with `MAX_CONCURRENT_DOWNLOADS=1 docker compose up -d`, start two downloads in two tabs (passed, serialized). (e) Restart with `MAX_CONCURRENT_DOWNLOADS=0` (passed, concurrent).
- [x] **Step 9:** Commit: `fix: cancel, time out, and cap concurrent yt-dlp runs (F5)` (and follow-ups `fix: cancel downloads when the browser tab disconnects (F5)` and `fix: give reconnected tabs a fresh cancellation token (F5)`)

---

### Task 7: Tolerant log reading and rotation (F6)

**Files:**
- Modify: `yt-dlp_web/yt-dlp_web/Services/LoggingService.cs`
- Create: `tests/yt-dlp_web.Tests/LoggingServiceTests.cs`

**Interfaces:**
- Produces: `public LoggingService(string logsPath, long maxFileBytes = 5 * 1024 * 1024)`. Current file `logs.jsonl`, previous file `logs.1.jsonl`.

- [x] **Step 1: Write the failing tests** (temp dir):
  ```csharp
  [Fact] GetLogs_SkipsMalformedLines
  //   LogDownload("a.mp4","1.1.1.1"); File.AppendAllText(logs.jsonl, "{\"Timestamp\":\"2026-\n");
  //   LogDownload("b.mp4","1.1.1.1") → GetLogs() has 2 entries, FileNames ["a.mp4","b.mp4"]
  [Fact] SaveEntry_RotatesWhenFileExceedsLimit
  //   new LoggingService(dir, maxFileBytes: 300); log 20 errors "e0".."e19"
  //   → File.Exists(logs.1.jsonl); new FileInfo(logs.jsonl).Length < 600; GetLogs().Last().Message == "e19";
  //     GetLogs() is in chronological order
  ```
- [x] **Step 2:** Run the tests. Expected: first test fails (returns 0 entries); second fails to compile.
- [x] **Step 3: Implement.** In `ReadLogs`, read `logs.1.jsonl` then `logs.jsonl`, deserialising each line in its own `try/catch (JsonException)`. In `SaveEntry`, inside the existing lock, if `logs.jsonl` length ≥ `maxFileBytes` then `File.Move(current, previous, overwrite: true)` before appending.
- [x] **Step 4:** Run the tests. Expected: pass.
- [x] **Step 5:** Commit: `fix: skip bad log lines and rotate logs (F6)`

---

### Task 8: Remove the loopback HttpClient and fix small UI bugs (F8)

**Files:**
- Modify: `Components/Pages/History.razor`, `Components/Pages/Home.razor`, `Components/MediaPlayerModal.razor`, `Program.cs:15-22`

**Interfaces:**
- Consumes: `IDownloadStore` (Task 2), `MediaFileTypes` (Task 3), `IUpdateService.RunUpdate()` (existing).

- [x] **Step 1: History.razor.** Replace `@inject HttpClient Http` with `@inject IDownloadStore Store`. `RefreshList` → `downloads = Store.List().ToList()`; `DeleteFile` → `Store.Delete(name)`; `DeleteAll` → `Store.DeleteAll()`. Replace the private `DownloadFile` class with `DownloadFileInfo`. Replace `IsAudioFile/IsImageFile/IsSubtitleFile/IsPlayableMedia` with the `MediaFileTypes` methods.
- [x] **Step 2: Home.razor.** Remove `@inject HttpClient Http` and the `UpdateResponse` class. `ManualUpdate` calls `UpdateService.RunUpdate()` directly. Add `string? updateError`, shown in its own card that copies the `updateMessage` card markup (lines 205-220) but uses `var(--accent-red)` for the icon. Update failures set `updateError`, not `error`, so they no longer appear under "Download Failed". `IsCurrentResultAudio` → `MediaFileTypes.IsAudio`.
- [x] **Step 3: Small fixes.** Build the progress bar style with `FormattableString.Invariant($"width: {Math.Clamp(currentProgress.Percent, 2.0, 100.0):F1}%;")`. Set `<track srclang>` (Home line 305 and MediaPlayerModal line 58) to `MediaFileTypes.SubtitleLanguage(file) ?? "und"`, and the label to the language or "Subtitles". MediaPlayerModal derives the file name with `Uri.UnescapeDataString(Path.GetFileName(SubtitleUrl))`. MediaPlayerModal `IsAudio` → `MediaFileTypes.IsAudio`.
- [x] **Step 4: Program.cs.** Delete the scoped `HttpClient` registration (lines 15-22).
- [x] **Step 5: Verify.** Build: 0 errors. `grep -rn "HttpClient\|ext is \"" yt-dlp_web/yt-dlp_web/Components` returns nothing. Tests pass.
- [x] **Step 6 (manual, Docker):** History lists files, plays one, deletes one, and Delete All works. "Update yt-dlp" on Home shows the success card. Run the app under `de-DE` culture (`LANG=de_DE.UTF-8` in compose) and confirm the progress bar still grows. An English subtitle track shows "en" in the player's track menu.
- [x] **Step 7:** Commit: `refactor: inject services instead of loopback HTTP; fix update error, culture and srclang (F8)`

---

### Task 9: Don't accept the client IP from the request body

**Files:**
- Modify: `Services/DownloadService.cs` (`DownloadRequest.ClientIp`), `Program.cs` (`/api/download`)

- [ ] **Step 1:** Add `[System.Text.Json.Serialization.JsonIgnore]` to `DownloadRequest.ClientIp`. Home still sets it in-process.
- [ ] **Step 2:** In `/api/download`, inject `IClientInfoService clientInfo` and set `req.ClientIp = clientInfo.GetClientIp();` before calling the service.
- [ ] **Step 3: Verify (Docker).** `curl -s -X POST localhost:7022/api/download -H 'Content-Type: application/json' -d '{"url":"https://example.invalid/x","clientIp":"6.6.6.6"}'`. The Logs page shows the error with the real caller IP, not `6.6.6.6`.
- [ ] **Step 4:** Commit: `fix: derive client IP server-side for /api/download`

---

### Task 10: Deployment files, PWA leftovers, time zone, and AGENTS.md (F9)

**Files:**
- Modify: `caddy/Caddyfile`, `docs/docker-and-pwa-setup.md`, `docker-compose.yml`, `yt-dlp_web/yt-dlp_web/Dockerfile`, `AGENTS.md`
- Delete: `yt-dlp_web/yt-dlp_web/ServiceWorkerFiles/` (both files)

- [ ] **Step 1: Caddyfile.** Replace the site block with one that proxies everything (Caddy proxies WebSockets for `/_blazor` automatically):
  ```
  :80 {
  	encode gzip
  	reverse_proxy yt-dlp-web:8080
  }
  ```
- [ ] **Step 2: PWA.** Delete `ServiceWorkerFiles/` and the two `COPY …ServiceWorkerFiles…` lines in the Dockerfile. *(If the owner wants a PWA later, that is a separate feature.)*
- [ ] **Step 3: docker-compose.yml.** Add `- TZ=${TZ:-UTC}` to `environment`, with a comment saying the nightly update runs at local midnight in this zone. `tzdata` is already installed in the image.
- [ ] **Step 4: Docs.** Rewrite `docs/docker-and-pwa-setup.md` as Docker + reverse proxy setup (rename it to `docs/docker-setup.md`). Cover the compose service, the `YtDlp__*` settings from Tasks 5–6 (with `MAX_CONCURRENT_DOWNLOADS` and `DOWNLOAD_TIMEOUT_MINUTES`, their defaults, and that `0` means no download limit), `TZ`, the Caddyfile above, and a note that the app has no login and must be protected by the proxy or kept on a private network.
- [ ] **Step 5: AGENTS.md.** Update "Build and run" (test command, `YtDlp` settings) and remove the fixed items from "Known issues". Keep the "Rules for changes" section.
- [ ] **Step 6 (manual):** `docker compose up --build -d`, then a full smoke test: download video, audio-only (mp3), with subtitles and thumbnail; play from History; delete; view Logs; run Update.
- [ ] **Step 7:** Commit: `chore: fix Caddyfile, drop unused PWA files, set TZ, update docs (F9)`

---

## Finishing

- [ ] `dotnet test tests/yt-dlp_web.Tests/yt-dlp_web.Tests.csproj` all green; `dotnet build yt-dlp_web.slnx` 0 errors.
- [ ] `git log --oneline master..` shows one commit per task.
- [ ] Open a PR from `fix/review-bug-fixes` to `master` (or merge locally). Tag a release (`v1.3`) and rebuild `Package/` artifacts only after merge.
