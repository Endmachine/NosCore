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

using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;
using JetBrains.Annotations;
using NosCore.Core.Extensions;
using NosCore.Core.I18N;
using NosCore.Core.Networking;
using NosCore.Data.Enumerations.I18N;
using NosCore.Packets.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using NosCore.Networking.Extensions;

namespace NosCore.Core.Encryption
{
    public class LoginEncoder : MessageToMessageEncoder<IEnumerable<IPacket>>
    {
        private readonly ILogger _logger;
        private readonly ISerializer _serializer;

        public LoginEncoder(ILogger logger, ISerializer serializer)
        {
            _logger = logger;
            _serializer = serializer;
        }

        protected override void Encode(IChannelHandlerContext context, IEnumerable<IPacket> message,
            [NotNull] List<object> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            try
            {
                output.Add(Unpooled.WrappedBuffer(message.SelectMany(packet =>
                {
                    var packetString = _serializer.Serialize(packet);
                    var tmp = SessionFactory.Instance.Sessions[context.Channel.Id.AsLongText()].RegionType.GetEncoding()!.GetBytes($"{packetString} ");
                    for (var i = 0; i < packetString.Length; i++)
                    {
                        tmp[i] = Convert.ToByte(tmp[i] + 15);
                    }

                    tmp[^1] = 25;
                    return tmp.Length == 0 ? new byte[] { 0xFF } : tmp;
                }).ToArray()));
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                _logger.Information(LogLanguage.Instance.GetMessageFromKey(LogLanguageKey.ENCODE_ERROR), ex);
            }
        }
    }
}