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
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NosCore.Core.HttpClients.ConnectedAccountHttpClients;
using NosCore.Core.I18N;
using NosCore.Dao.Interfaces;
using NosCore.Data.Dto;
using NosCore.Data.Enumerations.I18N;
using NosCore.Data.WebApi;
using NosCore.GameObject.HttpClients.BlacklistHttpClient;
using NosCore.GameObject.Networking;
using NosCore.GameObject.Networking.ClientSession;
using NosCore.GameObject.Services.BlackListService;
using NosCore.PacketHandlers.Friend;
using NosCore.Packets.ClientPackets.Relations;
using NosCore.Packets.Enumerations;
using NosCore.Packets.ServerPackets.UI;
using NosCore.Shared.Configuration;
using NosCore.Tests.Shared;
using Serilog;

namespace NosCore.PacketHandlers.Tests.Friend
{
    [TestClass]
    public class BDelPacketHandlerTests
    {
        private static readonly ILogger Logger = new Mock<ILogger>().Object;
        private BlacklistService? _blackListController;
        private Mock<IBlacklistHttpClient>? _blackListHttpClient;
        private BlDelPacketHandler? _blDelPacketHandler;
        private Mock<IDao<CharacterDto, long>>? _characterDao;
        private IDao<CharacterRelationDto, Guid>? _characterRelationDao;
        private Mock<IConnectedAccountHttpClient>? _connectedAccountHttpClient;
        private ClientSession? _session;

        [TestInitialize]
        public async Task SetupAsync()
        {
            _characterRelationDao = TestHelpers.Instance.CharacterRelationDao;
            Broadcaster.Reset();
            await TestHelpers.ResetAsync().ConfigureAwait(false);
            _session = await TestHelpers.Instance.GenerateSessionAsync().ConfigureAwait(false);
            _connectedAccountHttpClient = TestHelpers.Instance.ConnectedAccountHttpClient;
            _connectedAccountHttpClient.Setup(s => s.GetCharacterAsync(It.IsAny<long?>(), It.IsAny<string?>()))
                .ReturnsAsync(new Tuple<ServerConfiguration?, ConnectedAccount?>(new ServerConfiguration(),
                    new ConnectedAccount
                    { ChannelId = 1, ConnectedCharacter = new Data.WebApi.Character { Id = _session.Character.CharacterId } }));
            _blackListHttpClient = TestHelpers.Instance.BlacklistHttpClient;
            _blDelPacketHandler = new BlDelPacketHandler(_blackListHttpClient.Object);
            _characterDao = new Mock<IDao<CharacterDto, long>>();
            _blackListController = new BlacklistService(_connectedAccountHttpClient.Object, _characterRelationDao,
                _characterDao.Object);
            _blackListHttpClient.Setup(s => s.GetBlackListsAsync(It.IsAny<long>()))
                .Returns((long id) => _blackListController.GetBlacklistedListAsync(id));
            _blackListHttpClient.Setup(s => s.DeleteFromBlacklistAsync(It.IsAny<Guid>()))
                .Callback((Guid id) => Task.FromResult(_blackListController.UnblacklistAsync(id)));
        }

        [TestMethod]
        public async Task Test_Delete_Friend_When_DisconnectedAsync()
        {
            var targetGuid = Guid.NewGuid();
            var list = new List<CharacterDto>
            {
                _session!.Character!,
                new() {CharacterId = 2, Name = "test"}
            };
            _characterDao!.Setup(s => s.FirstOrDefaultAsync(It.IsAny<Expression<Func<CharacterDto, bool>>>()))
                .Returns((Expression<Func<CharacterDto, bool>> exp) => Task.FromResult(list.FirstOrDefault(exp.Compile()))!);
            await _characterRelationDao!.TryInsertOrUpdateAsync(new[]
            {
               new CharacterRelationDto
               {
                   RelatedCharacterId = 2,
                   CharacterRelationId = targetGuid,
                   CharacterId = _session.Character.CharacterId,
                   RelationType = CharacterRelationType.Blocked
               }
           }).ConfigureAwait(false);
            var blDelPacket = new BlDelPacket
            {
                CharacterId = 2
            };

            await _blDelPacketHandler!.ExecuteAsync(blDelPacket, _session).ConfigureAwait(false);

            Assert.IsTrue(!_characterRelationDao.LoadAll().Any());
        }

        [TestMethod]
        public async Task Test_Delete_FriendAsync()
        {
            var targetSession = await TestHelpers.Instance.GenerateSessionAsync().ConfigureAwait(false);
            Guid.NewGuid();
            var targetGuid = Guid.NewGuid();
            var list = new List<CharacterDto>
            {
                _session!.Character!,
                targetSession.Character!
            };
            _characterDao!.Setup(s => s.FirstOrDefaultAsync(It.IsAny<Expression<Func<CharacterDto, bool>>>()))
                .Returns((Expression<Func<CharacterDto, bool>> exp) => Task.FromResult(list.FirstOrDefault(exp.Compile()))!);
            await _characterRelationDao!.TryInsertOrUpdateAsync(new[]
            {
                new CharacterRelationDto
                {
                    RelatedCharacterId = targetSession.Character.CharacterId,
                    CharacterRelationId = targetGuid,
                    CharacterId = _session.Character.CharacterId,
                    RelationType = CharacterRelationType.Blocked
                }
            }).ConfigureAwait(false);
            var blDelPacket = new BlDelPacket
            {
                CharacterId = targetSession.Character.CharacterId
            };

            await _blDelPacketHandler!.ExecuteAsync(blDelPacket, _session).ConfigureAwait(false);

            Assert.IsTrue(!_characterRelationDao.LoadAll().Any());
        }

        [TestMethod]
        public async Task Test_Delete_Friend_No_FriendAsync()
        {
            var targetSession = await TestHelpers.Instance.GenerateSessionAsync().ConfigureAwait(false);
            var guid = Guid.NewGuid();
            var targetGuid = Guid.NewGuid();
            var list = new List<CharacterDto>
            {
                _session!.Character!,
                targetSession.Character!
            };
            _characterDao!.Setup(s => s.FirstOrDefaultAsync(It.IsAny<Expression<Func<CharacterDto, bool>>>()))
                .Returns((Expression<Func<CharacterDto, bool>> exp) => Task.FromResult(list.FirstOrDefault(exp.Compile()))!);

            var blDelPacket = new BlDelPacket
            {
                CharacterId = targetSession.Character.CharacterId
            };

            await _blDelPacketHandler!.ExecuteAsync(blDelPacket, _session).ConfigureAwait(false);
            var lastpacket = (InfoPacket?)_session.LastPackets.FirstOrDefault(s => s is InfoPacket);
            Assert.AreEqual(GameLanguage.Instance.GetMessageFromKey(LanguageKey.NOT_IN_BLACKLIST,
                _session.Account.Language), lastpacket!.Message);
        }
    }
}