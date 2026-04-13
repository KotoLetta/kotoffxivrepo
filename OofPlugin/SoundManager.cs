using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace OofPlugin;

internal class SoundManager : IDisposable {
  private sealed class PlaybackInstance : IDisposable {
    public PlaybackInstance(WaveStream reader, WaveChannel32 audioStream,
                            DirectSoundOut output) {
      Reader = reader;
      AudioStream = audioStream;
      Output = output;
    }

    public WaveStream Reader { get; }
    public WaveChannel32 AudioStream { get; }
    public DirectSoundOut Output { get; }

    public void Dispose() {
      Output.Dispose();
      AudioStream.Dispose();
      Reader.Dispose();
    }
  }

  private readonly Configuration Configuration;
  private readonly object playbackLock = new();
  private readonly List<PlaybackInstance> activePlaybacks = new();

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
    PlaybackInstance[] playbacks;

    lock (playbackLock) {
      playbacks = activePlaybacks.ToArray();
      activePlaybacks.Clear();
    }

    foreach (var playback in playbacks) {
      playback.Output.Stop();
      playback.Dispose();
    }
  }

  public void Play(CancellationToken token, float volume = 1f) {
    if (token.IsCancellationRequested)
      return;

    var soundFile = GetRandomSoundFile();

    WaveStream reader;
    try {
      reader = new MediaFoundationReader(soundFile);
    }
    catch (Exception ex) {
      Dalamud.Log.Error("Failed to read sound file", ex);
      return;
    }

    var audioStream = new WaveChannel32(reader) {
      Volume = Configuration.Volume * volume,
      PadWithZeroes = false // you need this or else playbackstopped event will not fire
    };

    var soundOut = new DirectSoundOut();
    var playback = new PlaybackInstance(reader, audioStream, soundOut);
    var playbackRegistered = false;

    soundOut.PlaybackStopped += (_, _) => CleanupPlayback(playback);

    try {
      soundOut.Init(audioStream);

      lock (playbackLock) {
        activePlaybacks.Add(playback);
        playbackRegistered = true;
      }

      soundOut.Play();
    }
    catch (Exception ex) {
      if (playbackRegistered)
        CleanupPlayback(playback);
      else
        playback.Dispose();
      Dalamud.Log.Error("Failed to play sound", ex);
    }
  }

  private void CleanupPlayback(PlaybackInstance playback) {
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
