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

using System.Collections.Generic;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels.Sockets;
using Microsoft.Extensions.Options;
using NosCore.Networking;
using NosCore.Networking.SessionRef;
using NosCore.Packets.Interfaces;
using NosCore.Shared.Configuration;

namespace NosCore.Core.Networking
{
    public class PipelineFactory : IPipelineFactory
    {
        private readonly ISocketChannel _channel;
        private readonly INetworkClient _clientSession;
        private readonly IOptions<ServerConfiguration> _configuration;
        private readonly MessageToMessageDecoder<IByteBuffer> _decoder;
        private readonly MessageToMessageEncoder<IEnumerable<IPacket>> _encoder;
        private readonly ISessionRefHolder _sessionRefHolder;

        public PipelineFactory(ISocketChannel channel, MessageToMessageDecoder<IByteBuffer> decoder,
            MessageToMessageEncoder<IEnumerable<IPacket>> encoder, INetworkClient clientSession,
            IOptions<ServerConfiguration> configuration, ISessionRefHolder sessionRefHolder)
        {
            _channel = channel;
            _decoder = decoder;
            _encoder = encoder;
            _clientSession = clientSession;
            _configuration = configuration;
            _sessionRefHolder = sessionRefHolder;
        }

        public void CreatePipeline()
        {
            _sessionRefHolder[_channel.Id.AsLongText()] =
                new RegionTypeMapping(0, _configuration.Value.Language);
            var pipeline = _channel.Pipeline;
            pipeline.AddLast(_decoder);
            _clientSession.RegisterChannel(_channel);
            pipeline.AddLast(_clientSession);
            pipeline.AddLast(_encoder);
        }
    }
}