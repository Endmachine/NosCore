﻿//  __  _  __    __   ___ __  ___ ___
// |  \| |/__\ /' _/ / _//__\| _ \ __|
// | | ' | \/ |`._`.| \_| \/ | v / _|
// |_|\__|\__/ |___/ \__/\__/|_|_\___|
// 
// Copyright (C) 2019 - NosCore
// 
// NosCore is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NosCore.Core.I18N;
using NosCore.Data.Enumerations;
using NosCore.Data.Enumerations.I18N;
using NosCore.Data.StaticEntities;
using NosCore.GameObject;
using NosCore.GameObject.Networking;
using NosCore.GameObject.Networking.ClientSession;
using NosCore.GameObject.Services.EventLoaderService;
using NosCore.GameObject.Services.InventoryService;
using NosCore.GameObject.Services.ItemGenerationService;
using NosCore.GameObject.Services.ItemGenerationService.Item;
using NosCore.GameObject.Services.MapInstanceAccessService;
using NosCore.PacketHandlers.Shops;
using NosCore.Packets.ClientPackets.Inventory;
using NosCore.Packets.ClientPackets.Shops;
using NosCore.Packets.Enumerations;
using NosCore.Packets.ServerPackets.Shop;
using NosCore.Tests.Shared;
using Serilog;

namespace NosCore.PacketHandlers.Tests.Shops
{
    [TestClass]
    public class SellPacketHandlerTests
    {
        private MapInstanceAccessorService? _instanceProvider;
        private SellPacketHandler? _sellPacketHandler;
        private ClientSession? _session;
        private readonly ILogger _logger = new Mock<ILogger>().Object;

        [TestInitialize]
        public async Task SetupAsync()
        {
            await TestHelpers.ResetAsync().ConfigureAwait(false);
            Broadcaster.Reset();
            _instanceProvider = TestHelpers.Instance.MapInstanceAccessorService;
            _session = await TestHelpers.Instance.GenerateSessionAsync().ConfigureAwait(false);
            _sellPacketHandler = new SellPacketHandler(TestHelpers.Instance.WorldConfiguration);
        }


        [TestMethod]
        public async Task UserCanNotSellInExchangeAsync()
        {
            _session!.Character.InShop = true;
            var items = new List<ItemDto>
            {
                new Item {Type = NoscorePocketType.Etc, VNum = 1, IsTradable = true}
            };
            var itemBuilder = new ItemGenerationService(items,
                new EventLoaderService<Item, Tuple<InventoryItemInstance, UseItemPacket>, IUseItemEventHandler>(new List<IEventHandler<Item, Tuple<InventoryItemInstance, UseItemPacket>>>()), _logger);

            _session.Character.InventoryService!.AddItemToPocket(InventoryItemInstance.Create(itemBuilder.Create(1, 1), 0),
                NoscorePocketType.Etc, 0);
            _session.Character.InventoryService.AddItemToPocket(InventoryItemInstance.Create(itemBuilder.Create(1, 2), 0),
                NoscorePocketType.Etc, 1);
            _session.Character.InventoryService.AddItemToPocket(InventoryItemInstance.Create(itemBuilder.Create(1, 3), 0),
                NoscorePocketType.Etc, 2);

            _session.Character.MapInstance = _instanceProvider!.GetBaseMapById(1);
            await _sellPacketHandler!.ExecuteAsync(new SellPacket { Slot = 0, Amount = 1, Data = (short)NoscorePocketType.Etc },
                _session).ConfigureAwait(false);
            Assert.IsTrue(_session.Character.Gold == 0);
            Assert.IsNotNull(_session.Character.InventoryService.LoadBySlotAndType(0, NoscorePocketType.Etc));
        }

        [TestMethod]
        public async Task UserCanNotSellNotSoldableAsync()
        {
            var items = new List<ItemDto>
            {
                new Item {Type = NoscorePocketType.Etc, VNum = 1, IsSoldable = false}
            };
            var itemBuilder = new ItemGenerationService(items,
                new EventLoaderService<Item, Tuple<InventoryItemInstance, UseItemPacket>, IUseItemEventHandler>(new List<IEventHandler<Item, Tuple<InventoryItemInstance, UseItemPacket>>>()), _logger);

            _session!.Character.InventoryService!.AddItemToPocket(InventoryItemInstance.Create(itemBuilder.Create(1, 1), 0),
                NoscorePocketType.Etc, 0);
            _session.Character.InventoryService.AddItemToPocket(InventoryItemInstance.Create(itemBuilder.Create(1, 2), 0),
                NoscorePocketType.Etc, 1);
            _session.Character.InventoryService.AddItemToPocket(InventoryItemInstance.Create(itemBuilder.Create(1, 3), 0),
                NoscorePocketType.Etc, 2);

            _session.Character.MapInstance = _instanceProvider!.GetBaseMapById(1);
            await _sellPacketHandler!.ExecuteAsync(new SellPacket { Slot = 0, Amount = 1, Data = (short)NoscorePocketType.Etc },
                _session).ConfigureAwait(false);
            var packet = (SMemoiPacket?)_session.LastPackets.FirstOrDefault(s => s is SMemoiPacket);
            Assert.IsTrue(packet?.Type == SMemoType.Error && packet?.Message == Game18NConstString.ItemCanNotBeSold);
            Assert.IsTrue(_session.Character.Gold == 0);
            Assert.IsNotNull(_session.Character.InventoryService.LoadBySlotAndType(0, NoscorePocketType.Etc));
        }

        [TestMethod]
        public async Task UserCanSellAsync()
        {
            var items = new List<ItemDto>
            {
                new Item {Type = NoscorePocketType.Etc, VNum = 1, IsSoldable = true, Price = 500000}
            };
            var itemBuilder = new ItemGenerationService(items,
                new EventLoaderService<Item, Tuple<InventoryItemInstance, UseItemPacket>, IUseItemEventHandler>(new List<IEventHandler<Item, Tuple<InventoryItemInstance, UseItemPacket>>>()), _logger);

            _session!.Character.InventoryService!.AddItemToPocket(InventoryItemInstance.Create(itemBuilder.Create(1, 1), 0),
                NoscorePocketType.Etc, 0);
            _session.Character.InventoryService.AddItemToPocket(InventoryItemInstance.Create(itemBuilder.Create(1, 2), 0),
                NoscorePocketType.Etc, 1);
            _session.Character.InventoryService.AddItemToPocket(InventoryItemInstance.Create(itemBuilder.Create(1, 3), 0),
                NoscorePocketType.Etc, 2);

            _session.Character.MapInstance = _instanceProvider!.GetBaseMapById(1);
            await _sellPacketHandler!.ExecuteAsync(new SellPacket { Slot = 0, Amount = 1, Data = (short)NoscorePocketType.Etc },
                _session).ConfigureAwait(false);
            Assert.IsTrue(_session.Character.Gold > 0);
            Assert.IsNull(_session.Character.InventoryService.LoadBySlotAndType(0, NoscorePocketType.Etc));
        }
    }
}