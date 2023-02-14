using Equinox76561198048419394.Core.Inventory;
using Sandbox.Game.GameSystems.Chat;
using VRage.Components;
using VRage.Session;

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
            RemoveFixedUpdate(Update);
        }
    }
}