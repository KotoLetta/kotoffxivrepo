using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace OofPlugin {
  [Serializable]
  public class Configuration : IPluginConfiguration {
    public int Version { get; set; } = 0;

    //oof on fall
    public bool OofOnFall { get; set; } = true;
    public bool OofOnFallMounted { get; set; } = true;
    public bool OofOnFallBattle { get; set; } = true;

    //oof on death
    public bool OofOnDeath { get; set; } = true;
    public bool OofOnDeathBattle { get; set; } = true;
    public bool OofOnDeathSelf { get; set; } = true;
    public bool OofOnDeathParty { get; set; } = true;
    public bool OofOnDeathAlliance { get; set; } = true;
    //dbo (distance based oof)
    public bool DistanceBasedOof { get; set; } = true;
    public float DistanceMinVolume { get; set; } = 0.2f;
    public float DistanceFalloff { get; set; } = 0.5f;

    //audio settings
    public float Volume { get; set; } = 0.5f;
    public string DefaultSoundImportPath { get; set; } = string.Empty;
    public List<string> SoundImportPaths { get; set; } = new();

    // the below exist just to make saving less cumbersome

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface) {
      this.pluginInterface = pluginInterface;
      MigrateLegacySoundPath();
    }

    public void Save() {
      this.pluginInterface!.SavePluginConfig(this);
    }

    public void MigrateLegacySoundPath() {
      if (SoundImportPaths.Count == 0 &&
          !string.IsNullOrWhiteSpace(DefaultSoundImportPath)) {
        SoundImportPaths.Add(DefaultSoundImportPath);
      }
    }
  }
}
