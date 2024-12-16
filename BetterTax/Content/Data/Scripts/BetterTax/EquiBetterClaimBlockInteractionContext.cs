using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.UI;
using Medieval;
using Medieval.Entities.Components.Planet;
using Medieval.GameSystems;
using Medieval.GUI.ContextMenu.Attributes;
using Medieval.GUI.ContextMenu.Contexts;
using Medieval.ObjectBuilders.Components;
using ObjectBuilders.GUI;
using Sandbox.Game.Entities;
using Sandbox.Game.GUI;
using Sandbox.Game.GUI.Dialogs;
using Sandbox.Game.Players;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage;
using VRage.Definitions.Inventory;
using VRage.Input;
using VRage.Library.Collections;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;

namespace Equinox76561198048419394.BetterTax
{
    [MyContextMenuContextType(typeof(MyObjectBuilder_EquiBetterClaimBlockInteractionContext))]
    public class EquiBetterClaimBlockInteractionContext : MyClaimBlockInteractionContext
    {
        private static readonly SimpleDropdownDataSourceFactory<EquiBetterTaxAreaSelection.SelectionMode> ModeDropdown =
            SimpleDataSources.DropdownEnum<EquiBetterTaxAreaSelection.SelectionMode>(mode =>
            {
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

        private readonly EquiBetterTaxAreaSelection _selection = new EquiBetterTaxAreaSelection();

        private MyPlanetAreaOwnershipComponent _planetOwnership;
        private MyPlanetAreaUpkeepComponent _planetUpkeep;
        private HashSet<long> _selectedAreas;
        private EquiBetterTaxComponent _tax;

        private TaxItem _taxItem;

        public override void Init(object[] contextParams)
        {
            base.Init(contextParams);
            _planetOwnership = (MyPlanetAreaOwnershipComponent)contextParams[1];
            _planetUpkeep = _planetOwnership.Container?.Get<MyPlanetAreaUpkeepComponent>();
            _tax = MySession.Static.Components.Get<EquiBetterTaxComponent>();
            _selection.AreaId = AreaId;
            _selection.PlanetId = PlanetId;

            m_dataSources[MyStringId.GetOrCompute("SelectMode")] = ModeDropdown.Create(
                () => _selection.Mode,
                val =>
                {
                    if (_selection.Mode == val) return;
                    _selection.Mode = val;
                    RefreshSelectedAreas();
                });
            m_dataSources[MyStringId.GetOrCompute("SelectFaction")] = new SimpleDataSource<bool>(
                () => _selection.IncludeFaction,
                val =>
                {
                    if (_selection.IncludeFaction == val) return;
                    _selection.IncludeFaction = val;
                    RefreshSelectedAreas();
                });
            m_dataSources[MyStringId.GetOrCompute("AreaCount")] = SimpleDataSources.SimpleReadOnly(() => _selectedAreas.Count);
            m_dataSources[MyStringId.GetOrCompute("AreaMinExpiry")] = SimpleDataSources.SimpleReadOnly(() =>
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
            m_dataSources[MyStringId.GetOrCompute("AreaMaxPayable")] = SimpleDataSources.SimpleReadOnly(() =>
            {
                var maxPayable = MaxPayableTime();
                return maxPayable > TimeSpan.Zero ? FormatTime(maxPayable) : "n/a";
            });
            m_dataSources[MyStringId.GetOrCompute("TaxItems")] = new TaxItemsDataSource(
                _tax,
                MySession.Static?.PlayerEntity?.GetInventory(),
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
            var localPlayer = MyPlayers.Static.GetControllingPlayer(MySession.Static.PlayerEntity);
            if (localPlayer?.Identity == null) return;
            _selection.SelectedAreas(_planetOwnership, localPlayer.Identity, _selectedAreas);
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

        public override void Close()
        {
            base.Close();
            PoolManager.Return(ref _selectedAreas);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiBetterClaimBlockInteractionContext : MyObjectBuilder_ClaimBlockInteractionContext
    {
    }
}