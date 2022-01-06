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

using FastMember;
using NosCore.Core.I18N;
using NosCore.Data.Enumerations.I18N;
using NosCore.Shared.I18N;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NosCore.Parser.Parsers.Generic
{

    public class GenericParser<T> where T : new()
    {
        private readonly ILogger _logger;
        private readonly string _fileAddress;
        private readonly string _endPattern;
        private readonly TypeAccessor _typeAccessor;
        private readonly Dictionary<string, Func<Dictionary<string, string[][]>, object?>> _actionList;
        private readonly int _firstIndex;
        private readonly ILogLanguageLocalizer<LogLanguageKey> _logLanguage;

        public GenericParser(string fileAddress, string endPattern, int firstIndex, Dictionary<string, Func<Dictionary<string, string[][]>, object?>> actionList, ILogger logger, ILogLanguageLocalizer<LogLanguageKey> logLanguage)
        {
            _fileAddress = fileAddress;
            _endPattern = endPattern;
            _firstIndex = firstIndex;
            _typeAccessor = TypeAccessor.Create(typeof(T), true);
            _actionList = actionList;
            _logger = logger;
            _logLanguage = logLanguage;
        }

        private IEnumerable<string> ParseTextFromFile()
        {
            using var stream = new StreamReader(_fileAddress, Encoding.Default);
            var content = stream.ReadToEnd();
            var i = 0;
            return content.Split(_endPattern).Select(s => $"{(i++ == 0 ? "" : _endPattern)}{s}");
        }
        public Task<List<T>> GetDtosAsync() => GetDtosAsync("\t");
        public async Task<List<T>> GetDtosAsync(string splitter)
        {
            var items = ParseTextFromFile();
            ConcurrentBag<T> resultCollection = new ConcurrentBag<T>();
            await Task.WhenAll(items.Select(item => Task.Run(() =>
            {
                var lines = item.Split(
                        new[] { "\r\n", "\r", "\n" },
                        StringSplitOptions.None
                    )
                    .Select(s => s.Split(splitter))
                    .Where(s => s.Length > _firstIndex)
                    .GroupBy(x => x[_firstIndex]).ToDictionary(x => x.Key, y => y.ToArray());
                if (lines.Count == 0)
                {
                    return;
                }
                try
                {
                    var parsedItem = new T();
                    foreach (var actionOnKey in _actionList.Keys)
                    {
                        try
                        {
                            _typeAccessor[parsedItem, actionOnKey] = _actionList[actionOnKey].Invoke(lines);
                        }
                        catch (Exception ex)
                        {
                            ex.Data.Add("actionKey", actionOnKey);
                            throw;
                        }
                    }

                    resultCollection.Add(parsedItem);
                }
                catch (Exception ex)
                {
                    _logger.Verbose(_logLanguage[LogLanguageKey.CHUNK_FORMAT_INVALID], lines, ex);
                }
            }))).ConfigureAwait(false);
            return resultCollection.ToList();
        }
    }
}
