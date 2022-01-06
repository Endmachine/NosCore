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
using Microsoft.Extensions.Localization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NodaTime;
using NosCore.Algorithm.ExperienceService;
using NosCore.Algorithm.HeroExperienceService;
using NosCore.Algorithm.JobExperienceService;
using NosCore.Core.I18N;
using NosCore.Data.Enumerations;
using NosCore.Data.Enumerations.I18N;
using NosCore.Data.Enumerations.Items;
using NosCore.Data.StaticEntities;
using NosCore.GameObject.Services.EventLoaderService;
using NosCore.GameObject.Services.InventoryService;
using NosCore.GameObject.Services.ItemGenerationService;
using NosCore.GameObject.Services.ItemGenerationService.Handlers;
using NosCore.GameObject.Services.ItemGenerationService.Item;
using NosCore.GameObject.Services.TransformationService;
using NosCore.Packets.ClientPackets.Inventory;
using NosCore.Packets.ServerPackets.UI;
using NosCore.Shared.I18N;
using NosCore.Tests.Shared;
using Serilog;

namespace NosCore.GameObject.Tests.Services.ItemGenerationService.Handlers
{
    [TestClass]
    public class VehicleEventHandlerTests : UseItemEventHandlerTestsBase
    {
        private Mock<ILogger>? _logger;
        private GameObject.Services.ItemGenerationService.ItemGenerationService? _itemProvider;

        [TestInitialize]
        public async Task SetupAsync()
        {
            await TestHelpers.ResetAsync().ConfigureAwait(false);
            Session = await TestHelpers.Instance.GenerateSessionAsync().ConfigureAwait(false);
            _logger = new Mock<ILogger>();
            Handler = new VehicleEventHandler(_logger.Object, TestHelpers.Instance.LogLanguageLocalizer, new TransformationService(TestHelpers.Instance.Clock, new Mock<IExperienceService>().Object, new Mock<IJobExperienceService>().Object, new Mock<IHeroExperienceService>().Object, _logger.Object, TestHelpers.Instance.LogLanguageLocalizer));
            var items = new List<ItemDto>
            {
                new Item {Type = NoscorePocketType.Equipment, VNum = 1, ItemType = ItemType.Weapon}
            };
            _itemProvider = new GameObject.Services.ItemGenerationService.ItemGenerationService(items,
                new EventLoaderService<Item, Tuple<InventoryItemInstance, UseItemPacket>, IUseItemEventHandler>(new List<IEventHandler<Item, Tuple<InventoryItemInstance, UseItemPacket>>>()), _logger.Object);
        }


        [TestMethod]
        public async Task Test_Can_Not_Vehicle_In_ShopAsync()
        {
            Session!.Character.InShop = true;
            var itemInstance = InventoryItemInstance.Create(_itemProvider!.Create(1), Session.Character.CharacterId);
            await ExecuteInventoryItemInstanceEventHandlerAsync(itemInstance).ConfigureAwait(false);
            _logger!.Verify(s => s.Error(TestHelpers.Instance.LogLanguageLocalizer[LogLanguageKey.CANT_USE_ITEM_IN_SHOP]), Times.Exactly(1));
        }

        [TestMethod]
        public async Task Test_Vehicle_GetDelayedAsync()
        {
            UseItem.Mode = 1;
            var itemInstance = InventoryItemInstance.Create(_itemProvider!.Create(1), Session!.Character.CharacterId);
            await ExecuteInventoryItemInstanceEventHandlerAsync(itemInstance).ConfigureAwait(false);
            var lastpacket = (DelayPacket?)Session.LastPackets.FirstOrDefault(s => s is DelayPacket);
            Assert.IsNotNull(lastpacket);
        }

        [TestMethod]
        public async Task Test_VehicleAsync()
        {
            UseItem.Mode = 2;
            var itemInstance = InventoryItemInstance.Create(_itemProvider!.Create(1), Session!.Character.CharacterId);
            await ExecuteInventoryItemInstanceEventHandlerAsync(itemInstance).ConfigureAwait(false);
            Assert.IsTrue(Session.Character.IsVehicled);
        }

        [TestMethod]
        public async Task Test_Vehicle_RemoveAsync()
        {
            Session!.Character.IsVehicled = true;
            var itemInstance = InventoryItemInstance.Create(_itemProvider!.Create(1), Session.Character.CharacterId);
            await ExecuteInventoryItemInstanceEventHandlerAsync(itemInstance).ConfigureAwait(false);
            Assert.IsFalse(Session.Character.IsVehicled);
        }
    }
}