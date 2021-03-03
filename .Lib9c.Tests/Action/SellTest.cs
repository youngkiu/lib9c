namespace Lib9c.Tests.Action
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Libplanet;
    using Libplanet.Action;
    using Libplanet.Assets;
    using Libplanet.Crypto;
    using Nekoyume;
    using Nekoyume.Action;
    using Nekoyume.Model;
    using Nekoyume.Model.Item;
    using Nekoyume.Model.Mail;
    using Nekoyume.Model.State;
    using Serilog;
    using Xunit;
    using Xunit.Abstractions;

    public class SellTest
    {
        private const long ProductPrice = 100;

        private readonly Address _agentAddress;
        private readonly Address _avatarAddress;
        private readonly Currency _currency;
        private readonly AvatarState _avatarState;
        private readonly TableSheets _tableSheets;
        private IAccountStateDelta _initialState;

        public SellTest(ITestOutputHelper outputHelper)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.TestOutput(outputHelper)
                .CreateLogger();

            _initialState = new State();
            var sheets = TableSheetsImporter.ImportSheets();
            foreach (var (key, value) in sheets)
            {
                _initialState = _initialState
                    .SetState(Addresses.TableSheet.Derive(key), value.Serialize());
            }

            _tableSheets = new TableSheets(sheets);

            _currency = new Currency("NCG", 2, minters: null);
            var goldCurrencyState = new GoldCurrencyState(_currency);

            var shopState = new ShopState();

            _agentAddress = new PrivateKey().ToAddress();
            var agentState = new AgentState(_agentAddress);
            _avatarAddress = new PrivateKey().ToAddress();
            var rankingMapAddress = new PrivateKey().ToAddress();
            _avatarState = new AvatarState(
                _avatarAddress,
                _agentAddress,
                0,
                _tableSheets.GetAvatarSheets(),
                new GameConfigState(),
                rankingMapAddress)
            {
                worldInformation = new WorldInformation(
                    0,
                    _tableSheets.WorldSheet,
                    GameConfig.RequireClearedStageLevel.ActionsInShop),
            };
            agentState.avatarAddresses[0] = _avatarAddress;

            var equipment = ItemFactory.CreateItemUsable(
                _tableSheets.EquipmentItemSheet.First,
                Guid.NewGuid(),
                0);
            _avatarState.inventory.AddItem(equipment);

            var consumable = ItemFactory.CreateItemUsable(
                _tableSheets.ConsumableItemSheet.First,
                Guid.NewGuid(),
                0);
            _avatarState.inventory.AddItem(consumable);

            var costume = ItemFactory.CreateCostume(
                _tableSheets.CostumeItemSheet.First,
                Guid.NewGuid());
            _avatarState.inventory.AddItem(costume);

            _initialState = _initialState
                .SetState(GoldCurrencyState.Address, goldCurrencyState.Serialize())
                .SetState(Addresses.Shop, shopState.Serialize())
                .SetState(_agentAddress, agentState.Serialize())
                .SetState(_avatarAddress, _avatarState.Serialize());
        }

        [Theory]
        [InlineData(ItemType.Consumable)]
        [InlineData(ItemType.Costume)]
        [InlineData(ItemType.Equipment)]
        public void Execute(ItemType itemType)
        {
            var shopState = _initialState.GetShopState();
            Assert.Empty(shopState.Products);

            var avatarState = _initialState.GetAvatarState(_avatarAddress);
            List<Inventory.Item> inventoryItem = avatarState.inventory.Items.Where(i => i.item.ItemType == itemType).ToList();
            Assert.Single(inventoryItem);
            var previousStates = _initialState;
            var currencyState = previousStates.GetGoldCurrency();
            var price = new FungibleAssetValue(currencyState, ProductPrice, 0);
            INonFungibleItem nonFungibleItem = (INonFungibleItem)inventoryItem.First().item;
            Assert.Equal(0, nonFungibleItem.RequiredBlockIndex);

            var sellAction = new Sell
            {
                itemId = nonFungibleItem.ItemId,
                price = price,
                sellerAvatarAddress = _avatarAddress,
            };

            var nextState = sellAction.Execute(new ActionContext
            {
                BlockIndex = 0,
                PreviousStates = previousStates,
                Rehearsal = false,
                Signer = _agentAddress,
                Random = new TestRandom(),
            });

            var nextAvatarState = nextState.GetAvatarState(_avatarAddress);
            Assert.True(nextAvatarState.inventory.TryGetNonFungibleItem(nonFungibleItem.ItemId, out var nextItem));
            INonFungibleItem nextNonFungibleItem = (INonFungibleItem)nextItem.item;
            Assert.Equal(Sell.ExpiredBlockIndex, nextNonFungibleItem.RequiredBlockIndex);

            var nextShopState = nextState.GetShopState();

            Assert.Single(nextShopState.Products);

            var products = nextShopState.Products.Values;

            var shopItem = products.First();
            INonFungibleItem item = itemType == ItemType.Costume ? (INonFungibleItem)shopItem.Costume : shopItem.ItemUsable;

            Assert.Equal(price, shopItem.Price);
            Assert.Equal(Sell.ExpiredBlockIndex, item.RequiredBlockIndex);
            Assert.Equal(_agentAddress, shopItem.SellerAgentAddress);
            Assert.Equal(_avatarAddress, shopItem.SellerAvatarAddress);

            var mailList = nextAvatarState.mailBox.Where(m => m is SellCancelMail).ToList();
            Assert.Single(mailList);

            Assert.Equal(Sell.ExpiredBlockIndex, mailList.First().requiredBlockIndex);
        }

        [Fact]
        public void ExecuteThrowInvalidPriceException()
        {
            var action = new Sell
            {
                itemId = default,
                price = -1 * _currency,
                sellerAvatarAddress = _avatarAddress,
            };

            Assert.Throws<InvalidPriceException>(() => action.Execute(new ActionContext
            {
                BlockIndex = 0,
                PreviousStates = _initialState,
                Signer = _agentAddress,
            }));
        }

        [Fact]
        public void ExecuteThrowFailedLoadStateException()
        {
            var action = new Sell
            {
                itemId = default,
                price = 0 * _currency,
                sellerAvatarAddress = _avatarAddress,
            };

            Assert.Throws<FailedLoadStateException>(() => action.Execute(new ActionContext
            {
                BlockIndex = 0,
                PreviousStates = new State(),
                Signer = _agentAddress,
            }));
        }

        [Fact]
        public void ExecuteThrowNotEnoughClearedStageLevelException()
        {
            var avatarState = new AvatarState(_avatarState)
            {
                worldInformation = new WorldInformation(
                    0,
                    _tableSheets.WorldSheet,
                    0
                ),
            };

            _initialState = _initialState.SetState(_avatarAddress, avatarState.Serialize());

            var action = new Sell
            {
                itemId = default,
                price = 0 * _currency,
                sellerAvatarAddress = _avatarAddress,
            };

            Assert.Throws<NotEnoughClearedStageLevelException>(() => action.Execute(new ActionContext
            {
                BlockIndex = 0,
                PreviousStates = _initialState,
                Signer = _agentAddress,
            }));
        }

        [Fact]
        public void ExecuteThrowItemDoesNotExistException()
        {
            var action = new Sell
            {
                itemId = default,
                price = 0 * _currency,
                sellerAvatarAddress = _avatarAddress,
            };

            Assert.Throws<ItemDoesNotExistException>(() => action.Execute(new ActionContext
            {
                BlockIndex = 0,
                PreviousStates = _initialState,
                Signer = _agentAddress,
                Random = new TestRandom(),
            }));
        }

        [Fact]
        public void ExecuteThrowRequiredBlockIndexException()
        {
            var equipmentId = Guid.NewGuid();
            var equipment = ItemFactory.CreateItemUsable(
                _tableSheets.EquipmentItemSheet.First,
                equipmentId,
                10);
            _avatarState.inventory.AddItem(equipment);

            _initialState = _initialState.SetState(_avatarAddress, _avatarState.Serialize());

            var action = new Sell
            {
                itemId = equipmentId,
                price = 0 * _currency,
                sellerAvatarAddress = _avatarAddress,
            };

            Assert.Throws<RequiredBlockIndexException>(() => action.Execute(new ActionContext
            {
                BlockIndex = 0,
                PreviousStates = _initialState,
                Signer = _agentAddress,
                Random = new TestRandom(),
            }));
        }
    }
}
