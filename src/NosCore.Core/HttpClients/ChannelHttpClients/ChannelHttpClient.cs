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

using Json.Patch;
using NosCore.Core.I18N;

using NosCore.Data.Enumerations.I18N;
using Polly;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NodaTime;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace NosCore.Core.HttpClients.ChannelHttpClients
{
    public class ChannelHttpClient : IChannelHttpClient
    {
        private readonly Channel _channel;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;
        private Instant _lastUpdateToken;
        private string? _token;
        private readonly IClock _clock;

        public ChannelHttpClient(IHttpClientFactory httpClientFactory, Channel channel, ILogger logger, IClock clock)
        {
            _httpClientFactory = httpClientFactory;
            _channel = channel;
            _logger = logger;
            _clock = clock;
        }

        public async Task ConnectAsync()
        {
            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_channel.MasterCommunication!.ToString());

            using var content = new StringContent(JsonSerializer.Serialize(_channel),
                Encoding.Default, "application/json");

            var message = Policy
                .Handle<Exception>()
                .OrResult<HttpResponseMessage>(mess => !mess.IsSuccessStatusCode)
                .WaitAndRetryForeverAsync(retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (_, __, timeSpan) =>
                        _logger.Error(
                            LogLanguage.Instance.GetMessageFromKey(LogLanguageKey.MASTER_SERVER_RETRY),
                            timeSpan.TotalSeconds)
                ).ExecuteAsync(() => client.PostAsync(new Uri($"{client.BaseAddress}api/channel"), content));

            var result =
                JsonSerializer.Deserialize<ConnectionInfo>(await (await message.ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false), new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            _token = result?.Token;
            _lastUpdateToken = _clock.GetCurrentInstant();
            _logger.Debug(LogLanguage.Instance.GetMessageFromKey(LogLanguageKey.REGISTRED_ON_MASTER));
            _channel.ChannelId = result?.ChannelInfo?.ChannelId ?? 0;

            await Policy
                .HandleResult<HttpStatusCode>(ping => ping == HttpStatusCode.OK)
                .WaitAndRetryForeverAsync(retryAttempt => TimeSpan.FromSeconds(1),
                    (_, __, timeSpan) =>
                        _logger.Verbose(
                            LogLanguage.Instance.GetMessageFromKey(LogLanguageKey.MASTER_SERVER_PING))
                ).ExecuteAsync(() =>
                {
                    var jsonPatch = new JsonPatch(PatchOperation.Replace(Json.Pointer.JsonPointer.Parse("/LastPing"), JsonDocument.Parse(JsonSerializer.Serialize(_clock.GetCurrentInstant())).RootElement));
                    return PatchAsync(_channel.ChannelId, jsonPatch);
                }).ConfigureAwait(false);
            _logger.Error(
                LogLanguage.Instance.GetMessageFromKey(LogLanguageKey.MASTER_SERVER_PING_FAILED));
            Environment.Exit(0);
        }

        public async Task<HttpStatusCode> PatchAsync(int channelId, Json.Patch.JsonPatch patch)
        {
            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_channel.MasterCommunication!.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetOrRefreshTokenAsync().ConfigureAwait(false));
            //todo replace when System.Text.Json support jsonpatch
            using var content = new StringContent(JsonSerializer.Serialize(patch), Encoding.Default,
                "application/json-patch+json");

            var postResponse = await client
                .PatchAsync(new Uri($"{client.BaseAddress}api/channel?id={channelId}"), content).ConfigureAwait(false);
            if (postResponse.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<HttpStatusCode>(await postResponse.Content.ReadAsStringAsync().ConfigureAwait(false), new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })!;
            }

            throw new HttpRequestException(postResponse.Headers.ToString());
        }

        public async Task<string?> GetOrRefreshTokenAsync()
        {
            if (_lastUpdateToken.Plus(Duration.FromMinutes(25)) >= _clock.GetCurrentInstant())
            {
                return _token;
            }

            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_channel.MasterCommunication!.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            using var content = new StringContent(JsonSerializer.Serialize(_channel),
                Encoding.Default, "application/json");
            var message = client.PutAsync(new Uri($"{client.BaseAddress}api/channel"), content);
            var result =
                JsonSerializer.Deserialize<ConnectionInfo>(await (await message.ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false), new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            _token = result?.Token;
            _lastUpdateToken = _clock.GetCurrentInstant();
            _logger.Information(LogLanguage.Instance.GetMessageFromKey(LogLanguageKey.SECURITY_TOKEN_UPDATED));

            return _token;
        }

        public async Task<List<ChannelInfo>> GetChannelsAsync()
        {
            var channels = MasterClientListSingleton.Instance.Channels;
            if (MasterClientListSingleton.Instance.Channels.Any())
            {
                return channels;
            }

            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await GetOrRefreshTokenAsync().ConfigureAwait(false));

            var response = await client.GetAsync(new Uri($"{_channel.MasterCommunication}/api/channel")).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var chan = JsonSerializer.Deserialize<List<ChannelInfo>>(await response.Content.ReadAsStringAsync()
                    , new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                if (chan != null)
                {
                    channels = chan;
                    return channels;
                }
            }

            throw new HttpRequestException();
        }

        public async Task<ChannelInfo?> GetChannelAsync(int channelId)
        {
            var channels = MasterClientListSingleton.Instance.Channels;
            if (MasterClientListSingleton.Instance.Channels.Any())
            {
                return channels?.FirstOrDefault(s => s.Id == channelId);
            }

            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_channel.MasterCommunication!.ToString());
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await GetOrRefreshTokenAsync().ConfigureAwait(false));

            var response = await client.GetAsync(new Uri($"{_channel.MasterCommunication}/api/channel?id={channelId}")).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                channels = JsonSerializer.Deserialize<List<ChannelInfo>>(await response.Content.ReadAsStringAsync().ConfigureAwait(false)
                    , new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
            }

            return channels?.FirstOrDefault(s => s.Id == channelId);
        }
    }
}