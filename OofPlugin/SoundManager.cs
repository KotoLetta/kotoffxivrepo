using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OofPlugin;

internal class SoundManager : IDisposable {
  private interface IPlaybackInstance : IDisposable {
    void Stop();
  }

  private sealed class NAudioPlaybackInstance : IPlaybackInstance {
    public NAudioPlaybackInstance(WaveStream reader, WaveChannel32 audioStream,
                                  DirectSoundOut output) {
      Reader = reader;
      AudioStream = audioStream;
      Output = output;
    }

    public WaveStream Reader { get; }
    public WaveChannel32 AudioStream { get; }
    public DirectSoundOut Output { get; }

    public void Stop() => Output.Stop();

    public void Dispose() {
      Output.Dispose();
      AudioStream.Dispose();
      Reader.Dispose();
    }
  }

  private sealed class ProcessPlaybackInstance : IPlaybackInstance {
    public ProcessPlaybackInstance(Process process) {
      Process = process;
    }

    public Process Process { get; }

    public void Stop() {
      try {
        if (!Process.HasExited)
          Process.Kill();
      }
      catch (Exception ex) {
        Dalamud.Log.Debug($"Failed to stop sound process: {ex.Message}");
      }
    }

    public void Dispose() => Process.Dispose();
  }

  private readonly Configuration Configuration;
  private readonly object playbackLock = new();
  private readonly List<IPlaybackInstance> activePlaybacks = new();

  public bool isSoundPlaying {
    get {
      lock (playbackLock) {
        return activePlaybacks.Count > 0;
      }
    }
  }

  private string[] soundFiles = Array.Empty<string>();

  internal CancellationTokenSource CancelToken;

  public SoundManager(OofPlugin plugin) {
    Configuration = plugin.Configuration;

    LoadFile();

    CancelToken = new CancellationTokenSource();
  }

  public void LoadFile() {
    Configuration.MigrateLegacySoundPath();

    soundFiles = Configuration.SoundImportPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct()
                     .ToArray();

    if (soundFiles.Length == 0) {
      soundFiles = new[] { Path.Combine(
          Dalamud.PluginInterface.AssemblyLocation.Directory!.FullName,
          "oof.wav") };
    }
  }

  public void Stop() {
    IPlaybackInstance[] playbacks;

    lock (playbackLock) {
      playbacks = activePlaybacks.ToArray();
      activePlaybacks.Clear();
    }

    foreach (var playback in playbacks) {
      playback.Stop();
      playback.Dispose();
    }
  }

  public void Play(CancellationToken token, float volume = 1f) {
    if (token.IsCancellationRequested)
      return;

    var soundFile = GetRandomSoundFile();

    if (OperatingSystem.IsLinux()) {
      PlayLinux(soundFile, volume);
      return;
    }

    if (IsWineOnLinux()) {
      if (TryPlayWineLinuxHost(soundFile, volume))
        return;

      TryPlayNAudio(soundFile, volume);
      return;
    }

    TryPlayNAudio(soundFile, volume);
  }

  private bool TryPlayNAudio(string soundFile, float volume,
                             bool logErrors = true) {
    WaveStream reader;
    try {
      reader = CreateReader(soundFile);
    }
    catch (Exception ex) {
      if (logErrors)
        Dalamud.Log.Error("Failed to read sound file", ex);
      return false;
    }

    var audioStream = new WaveChannel32(reader) {
      Volume = Configuration.Volume * volume,
      PadWithZeroes = false // you need this or else playbackstopped event will not fire
    };

    var soundOut = new DirectSoundOut();
    var playback = new NAudioPlaybackInstance(reader, audioStream, soundOut);
    var playbackRegistered = false;

    soundOut.PlaybackStopped += (_, _) => CleanupPlayback(playback);

    try {
      soundOut.Init(audioStream);

      lock (playbackLock) {
        activePlaybacks.Add(playback);
        playbackRegistered = true;
      }

      soundOut.Play();
      return true;
    }
    catch (Exception ex) {
      if (playbackRegistered)
        CleanupPlayback(playback);
      else
        playback.Dispose();
      if (logErrors)
        Dalamud.Log.Error("Failed to play sound", ex);
      return false;
    }
  }

  private void PlayLinux(string soundFile, float volume) {
    var startInfo = CreateLinuxPlayerStartInfo(
        soundFile, Math.Clamp(Configuration.Volume * volume, 0f, 1f));

    if (startInfo == null) {
      Dalamud.Log.Error(
          "Failed to play sound: no Linux audio player found. Install pw-play, paplay, ffplay, or aplay.");
      return;
    }

    var process = new Process {
      StartInfo = startInfo,
      EnableRaisingEvents = true
    };
    var playback = new ProcessPlaybackInstance(process);
    var playbackRegistered = false;

    process.Exited += (_, _) => CleanupPlayback(playback);

    try {
      lock (playbackLock) {
        activePlaybacks.Add(playback);
        playbackRegistered = true;
      }

      if (!process.Start())
        throw new InvalidOperationException("Audio player process did not start");
    }
    catch (Exception ex) {
      if (playbackRegistered)
        CleanupPlayback(playback);
      else
        playback.Dispose();
      Dalamud.Log.Error("Failed to play sound", ex);
    }
  }

  private static WaveStream CreateReader(string soundFile) {
    if (Path.GetExtension(soundFile).Equals(".wav",
                                           StringComparison.OrdinalIgnoreCase)) {
      return new WaveFileReader(soundFile);
    }

    return new MediaFoundationReader(soundFile);
  }

  private static ProcessStartInfo? CreateLinuxPlayerStartInfo(string soundFile,
                                                              float volume) {
    var pwPlay = FindExecutable("pw-play");
    if (pwPlay != null) {
      var startInfo = CreateProcessStartInfo(pwPlay);
      startInfo.ArgumentList.Add("--volume");
      startInfo.ArgumentList.Add(volume.ToString(CultureInfo.InvariantCulture));
      startInfo.ArgumentList.Add(soundFile);
      return startInfo;
    }

    var paplay = FindExecutable("paplay");
    if (paplay != null) {
      var startInfo = CreateProcessStartInfo(paplay);
      startInfo.ArgumentList.Add("--volume");
      startInfo.ArgumentList.Add(
          Math.Round(volume * 65536f).ToString(CultureInfo.InvariantCulture));
      startInfo.ArgumentList.Add(soundFile);
      return startInfo;
    }

    var ffplay = FindExecutable("ffplay");
    if (ffplay != null) {
      var startInfo = CreateProcessStartInfo(ffplay);
      startInfo.ArgumentList.Add("-nodisp");
      startInfo.ArgumentList.Add("-autoexit");
      startInfo.ArgumentList.Add("-loglevel");
      startInfo.ArgumentList.Add("quiet");
      startInfo.ArgumentList.Add("-volume");
      startInfo.ArgumentList.Add(
          Math.Round(volume * 100f).ToString(CultureInfo.InvariantCulture));
      startInfo.ArgumentList.Add(soundFile);
      return startInfo;
    }

    var aplay = FindExecutable("aplay");
    if (aplay != null) {
      var startInfo = CreateProcessStartInfo(aplay);
      startInfo.ArgumentList.Add("-q");
      startInfo.ArgumentList.Add(soundFile);
      return startInfo;
    }

    return null;
  }

  private bool TryPlayWineLinuxHost(string soundFile, float volume) {
    var startInfo = CreateWineLinuxHostPlayerStartInfo(
        soundFile, Math.Clamp(Configuration.Volume * volume, 0f, 1f));

    if (startInfo == null) {
      Dalamud.Log.Error(
          "Failed to play sound: Wine is running on Linux, but no host audio player or path mapping was found.");
      return false;
    }

    try {
      using var process = Process.Start(startInfo);
      return process != null;
    }
    catch (Exception ex) {
      Dalamud.Log.Error("Failed to start Wine Linux audio fallback", ex);
      return false;
    }
  }

  private static ProcessStartInfo? CreateWineLinuxHostPlayerStartInfo(
      string soundFile, float volume) {
    var unixPath = ConvertWinePathToUnixPath(soundFile);
    if (unixPath == null)
      return null;

    var playerPath = FirstExistingWineHostFile("/usr/bin/pw-play",
                                               "/bin/pw-play",
                                               "/usr/bin/paplay",
                                               "/bin/paplay",
                                               "/usr/bin/ffplay",
                                               "/bin/ffplay",
                                               "/usr/bin/aplay",
                                               "/bin/aplay");
    if (playerPath == null)
      return null;

    var startInfo = CreateProcessStartInfo("start.exe");
    startInfo.ArgumentList.Add("/unix");
    startInfo.ArgumentList.Add(playerPath);

    var fileName = Path.GetFileName(playerPath);
    if (fileName.Equals("pw-play", StringComparison.OrdinalIgnoreCase)) {
      startInfo.ArgumentList.Add("--volume");
      startInfo.ArgumentList.Add(volume.ToString(CultureInfo.InvariantCulture));
    }
    else if (fileName.Equals("paplay", StringComparison.OrdinalIgnoreCase)) {
      startInfo.ArgumentList.Add("--volume");
      startInfo.ArgumentList.Add(
          Math.Round(volume * 65536f).ToString(CultureInfo.InvariantCulture));
    }
    else if (fileName.Equals("ffplay", StringComparison.OrdinalIgnoreCase)) {
      startInfo.ArgumentList.Add("-nodisp");
      startInfo.ArgumentList.Add("-autoexit");
      startInfo.ArgumentList.Add("-loglevel");
      startInfo.ArgumentList.Add("quiet");
      startInfo.ArgumentList.Add("-volume");
      startInfo.ArgumentList.Add(
          Math.Round(volume * 100f).ToString(CultureInfo.InvariantCulture));
    }
    else if (fileName.Equals("aplay", StringComparison.OrdinalIgnoreCase)) {
      startInfo.ArgumentList.Add("-q");
    }

    startInfo.ArgumentList.Add(unixPath);
    return startInfo;
  }

  private static ProcessStartInfo CreateProcessStartInfo(string fileName) {
    return new ProcessStartInfo {
      FileName = fileName,
      UseShellExecute = false,
      CreateNoWindow = true
    };
  }

  private static string? FindExecutable(string executableName) {
    var path = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(path))
      return null;

    foreach (var directory in path.Split(Path.PathSeparator)) {
      if (string.IsNullOrWhiteSpace(directory))
        continue;

      var candidate = Path.Combine(directory, executableName);
      if (File.Exists(candidate))
        return candidate;
    }

    return null;
  }

  private static string? ConvertWinePathToUnixPath(string path) {
    if (path.StartsWith('/'))
      return path;

    if (path.Length >= 3 && path[1] == ':' &&
        (path[2] == '\\' || path[2] == '/')) {
      var drive = char.ToUpperInvariant(path[0]);
      var relativePath = path[3..].Replace('\\', '/');

      if (drive == 'Z')
        return "/" + relativePath;

      if (drive == 'C') {
        var winePrefix = Environment.GetEnvironmentVariable("WINEPREFIX");
        if (string.IsNullOrWhiteSpace(winePrefix)) {
          var home = Environment.GetEnvironmentVariable("HOME");
          if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

          if (string.IsNullOrWhiteSpace(home))
            return null;

          winePrefix = $"{home.TrimEnd('/', '\\')}/.wine";
        }

        return $"{winePrefix.TrimEnd('/', '\\')}/drive_c/{relativePath}";
      }

      return null;
    }

    if (Path.DirectorySeparatorChar == '/' && Path.IsPathFullyQualified(path))
      return path;

    return null;
  }

  private static string? FirstExistingWineHostFile(params string[] unixPaths) {
    foreach (var unixPath in unixPaths) {
      var winePath = ConvertUnixPathToWinePath(unixPath);
      if (winePath != null && File.Exists(winePath))
        return unixPath;
    }

    return null;
  }

  private static string? ConvertUnixPathToWinePath(string unixPath) {
    if (!unixPath.StartsWith('/'))
      return null;

    return "Z:\\" + unixPath.TrimStart('/').Replace('/', '\\');
  }

  private static bool IsWineOnLinux() {
    try {
      WineGetHostVersion(out var sysnamePtr, out _);
      var sysname = Marshal.PtrToStringAnsi(sysnamePtr);
      return string.Equals(sysname, "Linux", StringComparison.OrdinalIgnoreCase);
    }
    catch {
      return !string.IsNullOrWhiteSpace(
                 Environment.GetEnvironmentVariable("WINEPREFIX")) ||
             Directory.Exists("Z:\\usr\\bin");
    }
  }

  [DllImport("ntdll.dll", EntryPoint = "wine_get_host_version",
             CallingConvention = CallingConvention.Cdecl)]
  private static extern void WineGetHostVersion(out IntPtr sysname,
                                                out IntPtr release);

  private void CleanupPlayback(IPlaybackInstance playback) {
    bool removed;

    lock (playbackLock) {
      removed = activePlaybacks.Remove(playback);
    }

    if (removed)
      playback.Dispose();
  }

  private string GetRandomSoundFile() {
    if (soundFiles.Length == 0) {
      LoadFile();
    }

    return soundFiles[Random.Shared.Next(soundFiles.Length)];
  }

  public void PlayDeath(Vector3 localPosition, Vector3 deadPlayerPosition,
                        CancellationToken token) {
    var volume = 1f;

    if (Configuration.DistanceBasedOof &&
        deadPlayerPosition != Vector3.Zero) {
      var dist = Vector3.Distance(localPosition, deadPlayerPosition);
      volume = VolumeFromDist(dist);
    }

    Play(token, volume);
  }

  public float VolumeFromDist(float dist, float distMax = 30f) {
    dist = Math.Min(dist, distMax);

    var falloff = Configuration.DistanceFalloff > 0
                      ? 3f - Configuration.DistanceFalloff * 3f
                      : 2.999f;

    var vol = 1f - ((dist / distMax) * (1f / falloff));
    return Math.Max(Configuration.DistanceMinVolume, vol);
  }

  public async Task TestDistanceAudio(CancellationToken token) {
    async Task PlayTest(float volume) {
      if (token.IsCancellationRequested)
        return;

      Play(token, volume);
      await Task.Delay(700, token);
    }

    await PlayTest(VolumeFromDist(0));
    await PlayTest(VolumeFromDist(10));
    await PlayTest(VolumeFromDist(20));
    await PlayTest(VolumeFromDist(30));
  }

  public void Dispose() {
    CancelToken.Cancel();
    CancelToken.Dispose();
    Stop();
  }
}
