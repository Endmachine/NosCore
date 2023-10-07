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

using NosCore.Core.I18N;
using NosCore.Dao.Interfaces;
using NosCore.Data.Enumerations.I18N;
using NosCore.Data.StaticEntities;
using NosCore.Shared.I18N;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NosCore.Parser.Parsers
{
    public class ScriptParser(IDao<ScriptDto, Guid> scriptDao, ILogger logger, ILogLanguageLocalizer<LogLanguageKey> logLanguage)
    {
        //script {ScriptId}	
        //{ScriptStepId}	{StepType} {Argument}
        private readonly string _fileCardDat = $"{Path.DirectorySeparatorChar}tutorial.dat";

        public async Task InsertScriptsAsync(string folder)
        {
            var allScripts = scriptDao.LoadAll().ToList();
            using var stream = new StreamReader(folder + _fileCardDat, Encoding.Default);
            var scripts = new List<ScriptDto>();
            string? line;
            byte scriptId = 0;
            while ((line = await stream.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                var split = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (line.StartsWith("#"))
                {
                    continue;
                }
                if (split.Length > 1 && split[0] == "script")
                {
                    scriptId = Convert.ToByte(split[1]);
                }
                else if (split.Length > 2 && !allScripts.Any(s => s.ScriptId == scriptId && s.ScriptStepId == Convert.ToInt16(split[0])))
                {
                    var canParse = short.TryParse(split[2], out var argument1);
                    var stringArgument = !canParse ? split[2] : null;
                    scripts.Add(new ScriptDto()
                    {
                        Id = Guid.NewGuid(),
                        ScriptStepId = Convert.ToInt16(split[0]),
                        StepType = split[1],
                        StringArgument = stringArgument,
                        Argument1 = canParse ? argument1 : (short?)null,
                        Argument2 = split.Length > 3 && short.TryParse(split[3], out var argument2) ? argument2 : (short?)null,
                        Argument3 = split.Length > 4 && short.TryParse(split[4], out var argument3) ? argument3 : (short?)null,
                        ScriptId = scriptId
                    });
                }
            }

            await scriptDao.TryInsertOrUpdateAsync(scripts).ConfigureAwait(false);
            logger.Information(logLanguage[LogLanguageKey.SCRIPTS_PARSED], scripts.Count);
        }
    }
}