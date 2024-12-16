using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util;
using Medieval.Entities.Components.Planet;
using Medieval.GameSystems;
using Sandbox.Game.Entities;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Library.Collections;
using VRage.Network;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.BetterTax
{
    public partial class EquiBetterTaxComponent
    {
        public void RequestPay(EquiBetterTaxAreaSelection selection, MyDefinitionId itemId, int amount)
        {
            if (MyAPIGateway.Multiplayer != null && !MyEventContext.Current.IsLocallyInvoked)
                MyAPIGateway.Multiplayer.RaiseEvent(this, e => e.Pay_Sync, selection, (SerializableDefinitionId)itemId, amount);
            else
                PayInternal(selection, itemId, amount, MyPlayers.Static.GetControllingPlayer(Session.PlayerEntity));
        }

        [Server, Reliable, Event]
        private void Pay_Sync(EquiBetterTaxAreaSelection selection, SerializableDefinitionId itemId, int amount)
        {
            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(MyEventContext.Current.Sender.Value, 0));
            PayInternal(selection, itemId, amount, player);
        }

        private void PayInternal(EquiBetterTaxAreaSelection selection, SerializableDefinitionId itemId, int amount, MyPlayer player)
        {
            if (!Scene.TryGetEntity(selection.PlanetId, out var planet)
                || !planet.Components.TryGet(out MyPlanetAreasComponent areasComponent)
                || !planet.Components.TryGet(out MyPlanetAreaOwnershipComponent areaOwnershipComponent)
                || !planet.Components.TryGet(out MyPlanetAreaUpkeepComponent areaUpkeepComponent)
                || !MyDefinitionManager.TryGet(itemId, out MyInventoryItemDefinition itemDef))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            var playerIdentity = player?.Identity;
            var playerEntity = player?.ControlledEntity;
            var playerInventory = playerEntity?.GetInventory();
            if (playerEntity == null
                || playerIdentity == null
                || playerInventory == null)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            // If remote pay isn't supported then ensure the calling player is inside the requested area.
            if (!Config.SupportPayAll &&
                !NetworkTrust.IsTrusted(playerEntity.GetPosition(), areasComponent.CalculateEnclosingWorldSphere(selection.AreaId)))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            // Ensure the requested mode is supported.
            if (!Config.IsSupported(selection.Mode))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            var itemValue = GetValue(itemDef);
            var availablePayment = TimeSpan.FromTicks(Math.Min(playerInventory.GetItemAmount(itemId), amount) * itemValue.Ticks);

            // Ensure the player has the items they want to use and that they have value.
            if (availablePayment == TimeSpan.Zero)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            using (PoolManager.Get(out Dictionary<long, TimeSpan> usedPayment))
            {
                TimeSpan totalUsedPayment;
                using (PoolManager.Get(out HashSet<long> areaIds))
                {
                    selection.SelectedAreas(areaOwnershipComponent, playerIdentity, areaIds);
                    // Ensure the player actually selected areas.
                    if (areaIds.Count == 0)
                    {
                        MyEventContext.ValidationFailed();
                        return;
                    }

                    PlanPayment(areaUpkeepComponent, areaIds, availablePayment, usedPayment, out totalUsedPayment);
                }

                if (usedPayment.Count == 0) return;

                var totalUsedItems = (int)Math.Ceiling(totalUsedPayment.Ticks / (double)itemValue.Ticks);

                // Remove the used items.
                if (!playerInventory.RemoveItems(itemId, totalUsedItems))
                {
                    MyEventContext.ValidationFailed();
                    return;
                }

                // Apply the expiration changes.
                using (PoolManager.Get(out Dictionary<long, TimeSpan> newExpirationTimes))
                {
                    var now = Session.ElapsedGameTime;
                    foreach (var used in usedPayment)
                    {
                        var newExpiresTime = AreaExpirationTime(areaUpkeepComponent, used.Key);
                        if (newExpiresTime < now) newExpiresTime = now;
                        newExpiresTime += used.Value;
                        newExpirationTimes.Add(used.Key, newExpiresTime);
                    }

                    EquiUpkeepSyncExtensions.UpdateUpkeep(areaUpkeepComponent, newExpirationTimes);
                }
            }
        }

        internal void PlanPayment(
            MyPlanetAreaUpkeepComponent upkeepComponent,
            HashSet<long> areaIds,
            TimeSpan availablePayment,
            Dictionary<long, TimeSpan> usedPayment,
            out TimeSpan totalUsedPayment)
        {
            using (PoolManager.Get(out List<MyTuple<long, TimeSpan>> areaAndMaxPayment))
            {
                totalUsedPayment = TimeSpan.Zero;

                foreach (var area in areaIds)
                {
                    var maxPayment = AreaMaxPayment(upkeepComponent, area);
                    if (maxPayment > TimeSpan.Zero) areaAndMaxPayment.Add(MyTuple.Create(area, maxPayment));
                }

                if (areaAndMaxPayment.Count == 0) return;

                // Sort from high max payment to low max payment.
                areaAndMaxPayment.Sort((a, b) => -a.Item2.CompareTo(b.Item2));

                var paidDownTo = areaAndMaxPayment[0].Item2;
                var endOfConst = 0;
                while (endOfConst < areaAndMaxPayment.Count && availablePayment.Ticks >= endOfConst * Granularity.Ticks)
                {
                    // Scan for the first area with a lower max payment.
                    while (endOfConst < areaAndMaxPayment.Count && paidDownTo == areaAndMaxPayment[endOfConst].Item2)
                        endOfConst++;

                    // Compute how much to pay to bring the max payment of this block of areas down to the next block.
                    var paidDownToNext = endOfConst < areaAndMaxPayment.Count ? areaAndMaxPayment[endOfConst].Item2 : TimeSpan.Zero;
                    var blockPayment = paidDownTo - paidDownToNext;

                    // If the remaining available payment, divided across the block, is less than the block payment then clip it.
                    var availableBlockPayment = DivideWithGranularity(availablePayment, endOfConst);
                    if (availableBlockPayment <= blockPayment)
                        blockPayment = availableBlockPayment;

                    var blockTotalPayment = TimeSpan.FromTicks(blockPayment.Ticks * endOfConst);

                    paidDownTo -= blockPayment;
                    availablePayment -= blockTotalPayment;
                    totalUsedPayment += blockTotalPayment;
                }

                // Determine total used cost per area.
                foreach (var areaAndMax in areaAndMaxPayment)
                {
                    var paid = areaAndMax.Item2 - paidDownTo;
                    if (paid > TimeSpan.Zero) usedPayment.Add(areaAndMax.Item1, paid);
                }
            }
        }

    }
}