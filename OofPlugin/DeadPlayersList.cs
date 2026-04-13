using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Party;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OofPlugin {
  public class DeadPlayersList {
    public class DeadPlayer {
      public uint PlayerId;
      public Vector3 Distance = Vector3.Zero;
    }

    public List<DeadPlayer> DeadPlayers { get; set; } = new();

    private bool AddRemoveDeadPlayer(uint currentHp, uint entityId, Vector3 pos) {
      if (currentHp == 0 && !DeadPlayers.Any(x => x.PlayerId == entityId)) {
        DeadPlayers.Add(new DeadPlayer { PlayerId = entityId, Distance = pos });
        return true;
      }
      else if (currentHp != 0 &&
                 DeadPlayers.Any(x => x.PlayerId == entityId)) {
        DeadPlayers.RemoveAll(x => x.PlayerId == entityId);
      }

      return false;
    }

    public bool AddRemoveDeadPlayer(IPlayerCharacter character) {
      if (character == null)
        return false;
      return AddRemoveDeadPlayer(character.CurrentHp, character.EntityId,
                                 character.Position);
    }

    public bool AddRemoveDeadPlayer(IPartyMember character) {
      if (character == null)
        return false;
      return AddRemoveDeadPlayer(character.CurrentHP, character.EntityId,
                                 character.Position);
    }
  }
}
