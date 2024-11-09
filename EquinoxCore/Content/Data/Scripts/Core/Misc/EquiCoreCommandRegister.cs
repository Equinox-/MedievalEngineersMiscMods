using System.Collections.Generic;
using Equinox76561198048419394.Core.Controller;
using Equinox76561198048419394.Core.Inventory;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Entities.Gravity;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Misc
{
    [MySessionComponent(AllowAutomaticCreation = true, AlwaysOn = true)]
    public sealed class EquiCoreCommandRegister : MySessionComponent
    {
        [FixedUpdate(true)]
        private void Update()
        {
            var chat = MyChatSystem.Static;
            if (chat == null)
                return;

            chat.RegisterChatCommand("/item-gen",
                EquiItemGeneratorComponent.HandleCommand,
                "Edits item generator tasks");
            chat.RegisterChatCommand(
                "/teleportWithGrids",
                HandleTeleportWithGrids,
                "Teleports a player and their grids"
            );
            RemoveFixedUpdate(Update);
        }

        private static bool HandleTeleportWithGrids(ulong sender, string message, MyChatCommandType handledAsType)
        {
            bool Respond(string response)
            {
                MyChatSystem.Static.SendMessageToClient(sender, MyStringHash.GetOrCompute("System"),
                    0, response);
                return true;
            }

            if (!MyAPIGateway.Session.IsAdminModeEnabled(sender))
                return Respond("You need to enable Medieval Master to use this command.");

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender, 0));
            var playerPos = player?.ControlledEntity?.Get<MyPositionComponentBase>();
            if (playerPos == null)
                return Respond("You must have a character to use this command");

            var tokens = message.Split(' ');
            if (tokens.Length < 4)
                return Respond($"You're at {playerPos.WorldMatrix.Translation.X} {playerPos.WorldMatrix.Translation.Y} {playerPos.WorldMatrix.Translation.Z}");
            if (!double.TryParse(tokens[1], out var x) || !double.TryParse(tokens[2], out var y) || !double.TryParse(tokens[3], out var z))
                return Respond("Should be {x} {y} {z}");
            var pos = new Vector3D(x, y, z);
            var up = -Vector3.Normalize(MyGravityProviderSystem.CalculateTotalGravityInPoint(pos));
            var forward = Vector3.Normalize(Vector3.CalculatePerpendicularVector(up));

            var src = playerPos.WorldMatrix;
            var dest = MatrixD.CreateWorld(pos, forward, up);

            var delta = MatrixD.Invert(src) * dest;
            var entities = new List<MyEntity> { playerPos.Entity };
            for (var i = 0; i < entities.Count; i++)
            {
                var ent = entities[i];
                if (ent.Components.TryGet(out EquiEntityControllerComponent ctl) && ctl.Controlled?.Controllable?.Entity != null)
                    Add(ctl.Controlled.Controllable.Entity);
                foreach (var group in ent.Scene.GetEntityGroups(ent.Id))
                foreach (var other in group.Entities)
                    Add(other);
                continue;

                void Add(MyEntity other)
                {
                    if (other.Parent != null)
                    {
                        Add(other.Parent);
                        return;
                    }

                    if (entities.Contains(other)) return;
                    entities.Add(other);
                }
            }

            foreach (var ent in entities)
                ent.PositionComp.SetWorldMatrix(ent.WorldMatrix * delta, null, true);
            return Respond($"Moved {delta.Translation.Length()}");
        }
    }
}