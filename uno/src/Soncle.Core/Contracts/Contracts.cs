// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Core.Models;

namespace Soncle.Core.Contracts;

/// <summary>
/// The <c>window.api</c> surface from <c>src/preload.cjs</c>, written as C# interfaces so the UI
/// (later) is built against contracts. Each member carries the JS member it replaces. All methods
/// are async and take a <see cref="CancellationToken"/>; events are C# events.
/// </summary>
public interface IMusicCatalog
{
    Task<Page> HomeAsync(string? chip = null, CancellationToken ct = default);           // home(chip)
    Task<Page> HomeMoreAsync(CancellationToken ct = default);                            // homeMore()
    Task<Page> ExploreAsync(CancellationToken ct = default);                             // explore()
    Task<Page> BrowseAsync(string id, string? parameters = null, CancellationToken ct = default); // browse(id, params)
    Task<IReadOnlyList<string>> SuggestionsAsync(string query, CancellationToken ct = default);   // suggestions(q)
    Task<Page> SearchAsync(string query, string type = "all", CancellationToken ct = default);    // search(q, type)
    Task<Page> SearchMoreAsync(string query, string type, CancellationToken ct = default);        // searchMore(q, type)
    Task<Album> AlbumAsync(string id, CancellationToken ct = default);                   // album(id)
    Task<Artist> ArtistAsync(string id, CancellationToken ct = default);                 // artist(id)
    Task<Playlist> PlaylistAsync(string id, bool all = false, CancellationToken ct = default);    // playlist(id, all)
    Task<Page> UpNextAsync(string videoId, string? playlistId = null, CancellationToken ct = default); // upNext(id, pl)
    Task<Page> RadioAsync(string playlistId, string? parameters = null, CancellationToken ct = default); // radio(pl, params)
    Task<Page> RelatedAsync(string videoId, CancellationToken ct = default);             // related(id)
    Task<Page> LibraryAsync(CancellationToken ct = default);                             // library()
    Task<SongInfo> SongInfoAsync(string id, CancellationToken ct = default);             // songInfo(id)
    Task<bool> RateAsync(string id, bool like, CancellationToken ct = default);          // rate(id, like)
    Task<StreamInfo> PrefetchAsync(string id, CancellationToken ct = default);           // prefetch(id)
}

/// <summary>Resolving and reading song bytes.</summary>
public interface IStreams
{
    Task<StreamInfo> ResolveAsync(string id, bool force = false, CancellationToken ct = default);
}

/// <summary>Sign-in and account state.</summary>
public interface IAccount
{
    Task<bool> SignInAsync(CancellationToken ct = default);                              // signIn()
    Task<bool> SignInBrowserAsync(CancellationToken ct = default);                       // signInBrowser()
    Task<bool> SignOutAsync(CancellationToken ct = default);                             // signOut()
    Task<bool> AuthStatusAsync(CancellationToken ct = default);                          // authStatus()
    Task<bool?> PremiumStatusAsync(CancellationToken ct = default);                      // premiumStatus()
}

/// <summary>The library store as the UI sees it.</summary>
public interface IStore
{
    Task<JsonElementLike> GetAsync(CancellationToken ct = default);                      // storeGet()
    Task<bool> SetAsync(string key, JsonElementLike value, CancellationToken ct = default); // storeSet(k, v)
}

/// <summary>A minimal, contract-friendly wrapper over a JSON value.</summary>
public sealed record JsonElementLike(string Json);

/// <summary>Downloads.</summary>
public interface IDownloads
{
    Task<bool> AddAsync(IReadOnlyList<Track> tracks, CancellationToken ct = default);    // download(tracks)
    Task<bool> RemoveAsync(string id, CancellationToken ct = default);                   // removeDownload(id)
    Task<Downloads> ListAsync(CancellationToken ct = default);                           // downloads()
    Task<long> SizeAsync(CancellationToken ct = default);                                // downloadsSize()
    Task<bool> OpenFolderAsync(CancellationToken ct = default);                          // openDownloads()
    event Action<DownloadProgress>? Progress;                                            // onDownload
}

public sealed record Downloads(IReadOnlyList<Track> Done, IReadOnlyList<string> Active);
public sealed record DownloadProgress(string Id, string State, double? Progress, string? Error);

/// <summary>Lyrics.</summary>
public interface ILyrics
{
    Task<LyricsResult> GetAsync(Track track, CancellationToken ct = default);            // lyrics(t)
}

/// <summary>AutoEq headphone correction.</summary>
public interface IHeadphones
{
    Task<IReadOnlyList<HeadphoneProfile>> SearchAsync(string query, CancellationToken ct = default); // hpSearch(q)
    Task<HeadphoneProfile?> MatchAsync(string model, CancellationToken ct = default);     // hpMatch(model)
    Task<HeadphoneProfile> ProfileAsync(HeadphoneProfile profile, CancellationToken ct = default);   // hpProfile(p)
}

/// <summary>Flow radio feature cache.</summary>
public interface IFlowCache
{
    Task<Dictionary<string, FlowFeatures>> GetAsync(IReadOnlyList<string> ids, CancellationToken ct = default); // flowGet(ids)
    Task<bool> PutAsync(string id, FlowFeatures features, CancellationToken ct = default);  // flowPut(id, f)
    Task<int> CountAsync(CancellationToken ct = default);                                 // flowCount()
    Task<bool> ClearAsync(CancellationToken ct = default);                                // flowClear()
}
public interface ILocalLibrary
{
    Task<IReadOnlyList<string>> FoldersAsync(CancellationToken ct = default);            // localFolders()
    Task<bool> AddFolderAsync(CancellationToken ct = default);                            // localAddFolder()
    Task<IReadOnlyList<string>> RemoveFolderAsync(string dir, CancellationToken ct = default); // localRemoveFolder(dir)
    Task<object> ScanAsync(CancellationToken ct = default);                               // localScan()
    Task<IReadOnlyList<Track>> TracksAsync(CancellationToken ct = default);               // localTracks()
    Task<bool> OpenAsync(IReadOnlyList<string> paths, CancellationToken ct = default);    // localOpen(paths)
    Task<IReadOnlyList<Track>> PendingAsync(CancellationToken ct = default);              // localPending()
    Task<bool> RevealAsync(string id, CancellationToken ct = default);                    // localReveal(id)
    event Action<object>? LocalProgress;                                                  // onLocalProgress
    event Action<IReadOnlyList<Track>>? LocalPlay;                                        // onLocalPlay
}

/// <summary>Spotify / CSV import.</summary>
public interface IImport
{
    Task<IReadOnlyList<Track>> FromSpotifyAsync(string link, CancellationToken ct = default); // importSpotify(link)
    Task<object> ParseAsync(string text, string? name, CancellationToken ct = default);   // importParse(text, name)
    Task<object?> FromFileAsync(CancellationToken ct = default);                          // importFile()
    Task<IReadOnlyList<Track>> MatchAsync(IReadOnlyList<Track> tracks, CancellationToken ct = default); // importMatch(tracks)
    Task<bool> CancelAsync(CancellationToken ct = default);                               // importCancel()
    event Action<object>? ImportProgress;                                                 // onImportProgress
}

/// <summary>System services (power, bluetooth, external links, info, licences).</summary>
public interface ISystem
{
    Task<bool> OnBatteryAsync(CancellationToken ct = default);                            // power()
    Task<IReadOnlyList<string>> BtDevicesAsync(CancellationToken ct = default);           // btDevices()
    Task<bool> OpenExternalAsync(string url, CancellationToken ct = default);             // openExternal(u)
    Task<AppInfo> InfoAsync(CancellationToken ct = default);                              // info()
    Task<string> LicencesAsync(CancellationToken ct = default);                           // licences()
    Task<bool> ClearCacheAsync(CancellationToken ct = default);                           // clearCache()
    Task<bool> BackupAsync(CancellationToken ct = default);                               // backup()
    Task<bool> RestoreAsync(CancellationToken ct = default);                              // restore()
    Task<string> PathForFileAsync(string file, CancellationToken ct = default);           // pathForFile(f)
}

/// <summary>Window / shell control.</summary>
public interface IWindowShell
{
    Task<bool> SetMiniAsync(bool on, CancellationToken ct = default);                     // setMini(on)
    void SendState(object state);                                                        // sendState(s)
    void SaveSession(object session);                                                    // saveSession(s)
    void TitlebarColor(string color);                                                    // titlebarColor(c)
    void ToggleFullscreen();                                                             // toggleFullscreen()
    event Action<string>? MediaAction;                                                    // onMediaAction
    event Action<object>? WinState;                                                       // onWinState
    event Action<object>? Duck;                                                           // onDuck
}
/// <summary>The platform name and mobile flag the UI switches on.</summary>
public interface IPlatform
{
    string Platform { get; }                                                              // platform
    bool Mobile { get; }                                                                  // mobile
}
