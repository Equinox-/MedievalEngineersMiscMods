using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.UI;
using Medieval;
using Medieval.Entities.Components.Planet;
using Medieval.GUI.ContextMenu;
using Sandbox.Game.GUI;
using Sandbox.Game.GUI.Dialogs;
using Sandbox.Game.Players;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.Session;
using VRage.Utils;

namespace Equinox76561198048419394.BetterTax.UI
{
    internal class BetterTaxesContext
    {
        private static SimpleDropdownDataSourceFactory<EquiBetterTaxAreaSelection.SelectionMode> ModeDropdownFactory(
            Predicate<EquiBetterTaxAreaSelection.SelectionMode> supported) => SimpleDataSources.DropdownEnum<EquiBetterTaxAreaSelection.SelectionMode>(
            mode =>
            {
                if (!supported(mode)) return null;
                switch (mode)
                {
                    case EquiBetterTaxAreaSelection.SelectionMode.Local:
                        return SimpleDataSources.DropdownItem(nameof(EquiBetterTaxAreaSelection.SelectionMode.Local),
                            "Select only the claim this claim block is in.");
                    case EquiBetterTaxAreaSelection.SelectionMode.Connected:
                        return SimpleDataSources.DropdownItem(nameof(EquiBetterTaxAreaSelection.SelectionMode.Connected),
                            "Select all payable claims connected to this claim block.");
                    case EquiBetterTaxAreaSelection.SelectionMode.All:
                        return SimpleDataSources.DropdownItem(nameof(EquiBetterTaxAreaSelection.SelectionMode.All), "Select all payable claims.");
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
                }
            });

        private readonly EquiBetterTaxComponentDefinition _definition;
        private readonly EquiBetterTaxAreaSelection _selection = new EquiBetterTaxAreaSelection();
        private readonly MyPlanetAreaOwnershipComponent _planetOwnership;
        private readonly MyPlanetAreaUpkeepComponent _planetUpkeep;
        private readonly EquiBetterTaxSystem _tax;
        private readonly MyIdentity _identity;
        private HashSet<long> _selectedAreas;

        private TaxItem _taxItem;

        public BetterTaxesContext(
            MyEntity entity,
            MyPlanetAreaOwnershipComponent planetOwnership,
            long areaId,
            MyIdentity identity,
            Dictionary<MyStringId, IMyContextMenuDataSource> dataSources,
            MyInventoryBase inventory)
        {
            var definition = entity?.Get<EquiBetterTaxComponent>()?.Definition ?? EquiBetterTaxComponentDefinition.Default;
            _definition = definition;
            _planetOwnership = planetOwnership;
            _planetUpkeep = _planetOwnership.Container?.Get<MyPlanetAreaUpkeepComponent>();
            _tax = MySession.Static.Components.Get<EquiBetterTaxSystem>();
            _identity = identity;
            _selection.PaymentEntityId = entity?.EntityId ?? 0L;
            _selection.AreaId = areaId;
            _selection.PlanetId = planetOwnership.Entity.EntityId;
            PoolManager.Get(out _selectedAreas);

            var isOwned = planetOwnership.GetAreaOwner(areaId) != 0;

            dataSources[MyStringId.GetOrCompute("SelectMode")] = ModeDropdownFactory(mode =>
                definition.SupportedModes.Contains(mode) && (mode == EquiBetterTaxAreaSelection.SelectionMode.All || isOwned)).Create(
                () => _selection.Mode,
                val =>
                {
                    if (_selection.Mode == val) return;
                    _selection.Mode = val;
                    RefreshSelectedAreas();
                });
            dataSources[MyStringId.GetOrCompute("SelectFaction")] = new SimpleDataSource<bool>(
                () => _selection.IncludeFaction,
                val =>
                {
                    if (_selection.IncludeFaction == val) return;
                    _selection.IncludeFaction = val;
                    RefreshSelectedAreas();
                });
            dataSources[MyStringId.GetOrCompute("AreaCount")] = SimpleDataSources.SimpleReadOnly(() => _selectedAreas.Count);
            dataSources[MyStringId.GetOrCompute("AreaMinExpiry")] = SimpleDataSources.SimpleReadOnly(() =>
            {
                TimeSpan? minExpiry = null;
                foreach (var area in _selectedAreas)
                    if (!_planetUpkeep.IsTaxFree(area))
                    {
                        var expiresAt = _tax.AreaExpirationTime(_planetUpkeep, area);
                        if (minExpiry == null || expiresAt < minExpiry.Value) minExpiry = expiresAt;
                    }

                return minExpiry.HasValue ? FormatExpiry(minExpiry.Value) : "n/a";
            });
            dataSources[MyStringId.GetOrCompute("AreaMaxPayable")] = SimpleDataSources.SimpleReadOnly(() =>
            {
                var maxPayable = MaxPayableTime();
                return maxPayable > TimeSpan.Zero ? FormatTime(maxPayable) : "n/a";
            });
            dataSources[MyStringId.GetOrCompute("ValueMultiplier")] = SimpleDataSources.SimpleReadOnly(() => _definition.ValueMultiplier);
            dataSources[MyStringId.GetOrCompute("TaxItems")] = new TaxItemsDataSource(
                _tax,
                _definition,
                inventory,
                () => _taxItem,
                val => _taxItem = val,
                MaxPayableTime);
            RefreshSelectedAreas();
        }

        private TimeSpan MaxPayableTime()
        {
            var maxPayableTime = TimeSpan.Zero;
            foreach (var area in _selectedAreas)
                maxPayableTime += _tax.AreaMaxPayment(_planetUpkeep, area);
            return maxPayableTime;
        }

        private void RefreshSelectedAreas()
        {
            if (_selectedAreas == null) PoolManager.Get(out _selectedAreas);
            _selectedAreas.Clear();
            _selection.SelectedAreas(_planetOwnership, _identity, _selectedAreas);
        }

        private static string FormatExpiry(TimeSpan expiresAt)
        {
            var now = MySession.Static.ElapsedGameTime;
            return expiresAt <= now
                ? MyTexts.GetString(MyMedievalTexts.ClaimBlockInteraction_TimeExpired)
                : FormatTime(expiresAt - now);
        }

        internal static string FormatTime(TimeSpan time)
        {
            return MyTexts.GetString(MyMedievalTexts.ContextMenu_TaxPaymentTimeFormat, time.Days, time.Hours, time.Minutes, time.Seconds);
        }

        public void PayBetterTaxes()
        {
            if (_taxItem == null || _selectedAreas.Count == 0) return;
            var available = _taxItem.AmountAvailable;
            if (!MyAPIGateway.Input.IsAnyShiftKeyDown())
            {
                PayBetterTaxesInternal(available);
                return;
            }

            var screen = new MyIntInputDialog(MyTexts.GetString(MyCommonTexts.DialogAmount_AddAmountCaption), 0, available);
            screen.ResultCallback += amount =>
            {
                if (amount > 0) PayBetterTaxesInternal(amount);
            };
            MyGuiSandbox.AddScreen(screen);
        }

        private void PayBetterTaxesInternal(int amount)
        {
            var item = _taxItem.Definition;
            var usedPayment = new Dictionary<long, TimeSpan>();
            _tax.PlanPayment(_planetUpkeep, _selectedAreas, _taxItem.ValueAvailable, usedPayment, out var totalUsedPayment);
            MyMessageBox.Show(
                MyTexts.GetString(MyMedievalTexts.AreaUpkeep_Confirm, amount, item.DisplayNameText, FormatTime(totalUsedPayment)),
                MyTexts.GetString(MyCommonTexts.MessageBoxCaptionPleaseConfirm),
                MyMessageBoxButtons.YesNo, MyMessageBoxIcon.Question, callback: result =>
                {
                    if (result == MyDialogResult.Yes)
                        _tax.RequestPay(_selection, _taxItem.Definition.Id, amount);
                });
        }

        public void Close()
        {
            PoolManager.Return(ref _selectedAreas);
        }
    }
}