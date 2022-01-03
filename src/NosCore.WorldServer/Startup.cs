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

using Autofac;
using Autofac.Extensions.DependencyInjection;
using AutofacSerilogIntegration;
using DotNetty.Buffers;
using DotNetty.Codecs;
using FastExpressionCompiler;
using FastMember;
using JetBrains.Annotations;
using Mapster;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using NosCore.Algorithm.ExperienceService;
using NosCore.Core;
using NosCore.Core.Configuration;
using NosCore.Core.Controllers;
using NosCore.Core.Encryption;
using NosCore.Core.HttpClients.AuthHttpClients;
using NosCore.Core.HttpClients.ChannelHttpClients;
using NosCore.Core.HttpClients.ConnectedAccountHttpClients;
using NosCore.Core.I18N;
using NosCore.Dao;
using NosCore.Dao.Interfaces;
using NosCore.Data.CommandPackets;
using NosCore.Data.DataAttributes;
using NosCore.Data.Dto;
using NosCore.Data.Enumerations.I18N;
using NosCore.Database;
using NosCore.Database.Entities;
using NosCore.Database.Entities.Base;
using NosCore.GameObject;
using NosCore.GameObject.Holders;
using NosCore.GameObject.HttpClients.BlacklistHttpClient;
using NosCore.GameObject.Networking.ClientSession;
using NosCore.GameObject.Services.EventLoaderService;
using NosCore.GameObject.Services.InventoryService;
using NosCore.PacketHandlers.Login;
using NosCore.Packets.Attributes;
using NosCore.Packets.Enumerations;
using NosCore.Packets.Interfaces;
using NosCore.PathFinder.Heuristic;
using NosCore.PathFinder.Interfaces;
using NosCore.Shared.Authentication;
using NosCore.Shared.Configuration;
using NosCore.Shared.Enumerations;
using NosCore.Shared.I18N;
using NosCore.WorldServer.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NodaTime;
using NosCore.Core.Services.IdService;
using NosCore.Data;
using NosCore.GameObject.Services.MapItemGenerationService;
using NosCore.Networking;
using NosCore.Networking.Encoding;
using NosCore.Networking.Encoding.Filter;
using NosCore.Networking.SessionRef;
using Character = NosCore.GameObject.Character;
using ConfigureJwtBearerOptions = NosCore.Core.ConfigureJwtBearerOptions;
using Deserializer = NosCore.Packets.Deserializer;
using ILogger = Serilog.ILogger;
using ItemInstance = NosCore.Database.Entities.ItemInstance;
using Serializer = NosCore.Packets.Serializer;

namespace NosCore.WorldServer
{
    public class Startup
    {
        private const string Title = "NosCore - WorldServer";
        private const string ConsoleText = "WORLD SERVER - NosCoreIO";
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        
        public static void RegisterDatabaseObject<TDto, TDb, TPk>(ContainerBuilder containerBuilder, bool isStatic)
        where TDb : class
        where TPk : struct
        {
            containerBuilder.RegisterType<Dao<TDb, TDto, TPk>>().As<IDao<IDto>>().As<IDao<TDto, TPk>>().SingleInstance();
            if (!isStatic)
            {
                return;
            }

            var staticMetaDataAttribute = typeof(TDb).GetCustomAttribute<StaticMetaDataAttribute>();
            containerBuilder.Register(c =>
                {
                    var dic = c.Resolve<IDictionary<Type, Dictionary<string, Dictionary<RegionType, II18NDto>>>>();
                    var items = c.Resolve<IDao<TDto, TPk>>().LoadAll().ToList();
                    var props = StaticDtoExtension.GetI18NProperties(typeof(TDto));
                    if (props.Count > 0)
                    {
                        var regions = Enum.GetValues(typeof(RegionType));
                        var accessors = TypeAccessor.Create(typeof(TDto));
                        Parallel.ForEach(items, s => ((IStaticDto)s!).InjectI18N(props, dic, regions, accessors));
                    }

                    if ((items.Count != 0) || (staticMetaDataAttribute == null) ||
                        (staticMetaDataAttribute.EmptyMessage == LogLanguageKey.UNKNOWN))
                    {
                        if ((staticMetaDataAttribute != null) &&
                            (staticMetaDataAttribute.LoadedMessage != LogLanguageKey.UNKNOWN))
                        {
                            c.Resolve<ILogger>().Information(
                                LogLanguage.Instance.GetMessageFromKey(staticMetaDataAttribute.LoadedMessage),
                                items.Count);
                        }
                    }
                    else
                    {
                        c.Resolve<ILogger>()
                            .Error(LogLanguage.Instance.GetMessageFromKey(staticMetaDataAttribute.EmptyMessage));
                    }

                    return items;
                })
                .As<List<TDto>>()
                .SingleInstance()
                .AutoActivate();
        }
        

        private static void RegisterDto(ContainerBuilder containerBuilder)
        {
            containerBuilder.Register(c => c.Resolve<IEnumerable<IDao<IDto>>>().OfType<IDao<II18NDto>>().ToDictionary(
                    x => x.GetType().GetGenericArguments()[1], y => y.LoadAll().GroupBy(x => x!.Key ?? "")
                        .ToDictionary(x => x.Key,
                            x => x.ToList().ToDictionary(o => o!.RegionType, o => o!))))
            .AsImplementedInterfaces()
            .SingleInstance()
            .AutoActivate();

            var registerDatabaseObject = typeof(Startup).GetMethod(nameof(RegisterDatabaseObject));
            var assemblyDto = typeof(IStaticDto).Assembly.GetTypes();
            var assemblyDb = typeof(Account).Assembly.GetTypes();

            var assemblyGo = typeof(Character).Assembly.GetTypes();

            assemblyDto.Where(p => typeof(IDto).IsAssignableFrom(p) && p.IsClass)
                .ToList()
                .ForEach(t =>
                {
                    assemblyGo.Where(t.IsAssignableFrom).ToList().ForEach(tgo =>
                    {
                        containerBuilder.RegisterType(tgo);
                        containerBuilder
                            .RegisterType(typeof(GameObjectMapper<,>).MakeGenericType(t, tgo))
                            .As(typeof(IGameObjectMapper<>).MakeGenericType(t))
                            .AutoActivate();
                    });
                });

            assemblyDto.Where(p =>
                    typeof(IDto).IsAssignableFrom(p) &&
                    (!p.Name.Contains("InstanceDto") || p.Name.Contains("Inventory")) && p.IsClass)
                .ToList()
                .ForEach(t =>
                {
                    var type = assemblyDb.First(tgo =>
                        string.Compare(t.Name, $"{tgo.Name}Dto", StringComparison.OrdinalIgnoreCase) == 0);
                    var optionsBuilder = new DbContextOptionsBuilder<NosCoreContext>().UseInMemoryDatabase(
                        Guid.NewGuid().ToString());
                    var typepk = type.GetProperties()
                        .Where(s => new NosCoreContext(optionsBuilder.Options).Model.FindEntityType(type)?
                            .FindPrimaryKey()?.Properties.Select(x => x.Name)
                            .Contains(s.Name) ?? false
                        ).ToArray()[0];
                    registerDatabaseObject?.MakeGenericMethod(t, type, typepk!.PropertyType).Invoke(null,
                        new[] { containerBuilder, (object)typeof(IStaticDto).IsAssignableFrom(t) });
                });

            containerBuilder.RegisterType<Dao<ItemInstance, IItemInstanceDto?, Guid>>().As<IDao<IItemInstanceDto?, Guid>>().SingleInstance();
        }

        private void InitializeContainer(ContainerBuilder containerBuilder)
        {
            containerBuilder.RegisterType<NosCoreContext>().As<DbContext>();
            containerBuilder.RegisterType<MapsterMapper.Mapper>().AsImplementedInterfaces();
            var listofpacket = typeof(IPacket).Assembly.GetTypes()
                .Where(p => p.GetInterfaces().Contains(typeof(IPacket)) && (p.GetCustomAttribute<PacketHeaderAttribute>() == null
                    || (p.GetCustomAttribute<PacketHeaderAttribute>()!.Scopes & Scope.OnLoginScreen) == 0) && p.IsClass && !p.IsAbstract).ToList();
            listofpacket.AddRange(typeof(HelpPacket).Assembly.GetTypes()
                .Where(p => p.GetInterfaces().Contains(typeof(IPacket)) && p.IsClass && !p.IsAbstract).ToList());
            containerBuilder.Register(c => new Deserializer(listofpacket))
                .AsImplementedInterfaces()
                .SingleInstance();
            containerBuilder.Register(c => new Serializer(listofpacket))
                .AsImplementedInterfaces()
                .SingleInstance();

            //NosCore.Configuration
            containerBuilder.RegisterLogger();
            containerBuilder.RegisterType<ChannelHttpClient>().SingleInstance().AsImplementedInterfaces();
            containerBuilder.RegisterType<AuthHttpClient>().AsImplementedInterfaces();
            containerBuilder.RegisterType<ConnectedAccountHttpClient>().AsImplementedInterfaces();
            containerBuilder.RegisterAssemblyTypes(typeof(BlacklistHttpClient).Assembly)
                .Where(t => t.Name.EndsWith("HttpClient"))
                .AsImplementedInterfaces();

            containerBuilder.Register(c =>
            {
                var configuration = c.Resolve<IOptions<WorldConfiguration>>();
                return new Channel
                {
                    MasterCommunication = configuration.Value.MasterCommunication,
                    ClientName = configuration.Value.ServerName!,
                    ClientType = ServerType.WorldServer,
                    ConnectedAccountLimit = configuration.Value.ConnectedAccountLimit,
                    Port = configuration.Value.Port,
                    DisplayPort = configuration.Value.DisplayPort,
                    DisplayHost = configuration.Value.DisplayHost,
                    ServerId = configuration.Value.ServerId,
                    StartInMaintenance = configuration.Value.StartInMaintenance,
                    Host = configuration.Value.Host!,
                    WebApi = configuration.Value.WebApi,
                };
            });
            containerBuilder.Register<IHasher>(o => o.Resolve<IOptions<WebApiConfiguration>>().Value.HashingType switch
            {
                HashingType.BCrypt => new BcryptHasher(),
                HashingType.Pbkdf2 => new Pbkdf2Hasher(),
                _ => new Sha512Hasher()
            });

            //NosCore.Controllers
            containerBuilder.RegisterTypes(typeof(NoS0575PacketHandler).Assembly.GetTypes()
                    .Where(type => typeof(IPacketHandler).IsAssignableFrom(type) && typeof(IWorldPacketHandler).IsAssignableFrom(type)).ToArray())
                .AsImplementedInterfaces();

            //NosCore.Core
            containerBuilder.RegisterType<WorldDecoder>().As<MessageToMessageDecoder<IByteBuffer>>();
            containerBuilder.RegisterType<WorldEncoder>().As<MessageToMessageEncoder<IEnumerable<IPacket>>>();
            containerBuilder.Register(x => new List<RequestFilter>()).As<IEnumerable<RequestFilter>>();
            containerBuilder.Register(_ => SystemClock.Instance).As<IClock>().SingleInstance();
            containerBuilder.RegisterType<ClientSession>().AsImplementedInterfaces();
            containerBuilder.RegisterType<SessionRefHolder>().AsImplementedInterfaces().SingleInstance();
            containerBuilder.RegisterType<NetworkManager>();
            containerBuilder.RegisterType<PipelineFactory>().AsImplementedInterfaces();

            //NosCore.GameObject
            containerBuilder.RegisterType<OctileDistanceHeuristic>().As<IHeuristic>();
            containerBuilder.RegisterType<Clock>();
            containerBuilder.Register<IIdService<Group>>(_ => new IdService<Group>(1)).SingleInstance();
            containerBuilder.Register<IIdService<MapItem>>(_ => new IdService<MapItem>(100000)).SingleInstance();
            containerBuilder.Register<IIdService<ChannelInfo>>(_ => new IdService<ChannelInfo>(1)).SingleInstance();

            containerBuilder.RegisterAssemblyTypes(typeof(IInventoryService).Assembly, typeof(IExperienceService).Assembly)
                .Where(t => t.Name.EndsWith("Service"))
                .AsImplementedInterfaces();

            containerBuilder.RegisterAssemblyTypes(typeof(MapInstanceHolder).Assembly)
                .Where(t => t.Name.EndsWith("Holder"))
                .SingleInstance();

            RegisterDto(containerBuilder);

            containerBuilder.RegisterAssemblyTypes(typeof(Character).Assembly)
                .Where(t => typeof(IDto).IsAssignableFrom(t))
                .AsSelf();

            containerBuilder
                .RegisterGeneric(typeof(EventLoaderService<,,>));

            containerBuilder
                .RegisterAssemblyTypes(typeof(IEventHandler<,>).Assembly)
                .AsClosedTypesOf(typeof(IEventHandler<,>))
                .SingleInstance()
                .AsImplementedInterfaces();
        }

        [UsedImplicitly]
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.Title = Title;
            }
            Logger.PrintHeader(ConsoleText);
            services.AddOptions<WorldConfiguration>().Bind(_configuration).ValidateDataAnnotations();
            services.AddOptions<ServerConfiguration>().Bind(_configuration).ValidateDataAnnotations();
            services.AddOptions<WebApiConfiguration>().Bind(_configuration.GetSection(nameof(WorldConfiguration.MasterCommunication))).ValidateDataAnnotations();

            var worldConfiguration = new WorldConfiguration();
            _configuration.Bind(worldConfiguration);
            services.AddDbContext<NosCoreContext>(conf => conf.UseNpgsql(worldConfiguration.Database!.ConnectionString, options => { options.UseNodaTime(); }));
            services.Configure<KestrelServerOptions>(options => options.ListenAnyIP(worldConfiguration.WebApi.Port));
            services.AddSwaggerGen(c => c.SwaggerDoc("v1", new OpenApiInfo { Title = "NosCore World API", Version = "v1" }));
            services.AddLogging(builder => builder.AddFilter("Microsoft", LogLevel.Warning));
            services.AddHttpClient();
            services.AddAuthentication(config => config.DefaultScheme = JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
            services.ConfigureOptions<ConfigureJwtBearerOptions>();
            services.AddAuthorization(o =>
                {
                    o.DefaultPolicy = new AuthorizationPolicyBuilder()
                        .RequireAuthenticatedUser()
                        .Build();
                });

            services
                .AddControllers()
                .AddApplicationPart(typeof(StatController).GetTypeInfo().Assembly)
                .AddApplicationPart(typeof(AuthController).GetTypeInfo().Assembly)
                .AddControllersAsServices();

            services.RemoveAll<IHttpMessageHandlerBuilderFilter>();
            services.AddHostedService<WorldServer>();

            TypeAdapterConfig.GlobalSettings.AllowImplicitSourceInheritance = false;
            TypeAdapterConfig.GlobalSettings.ForDestinationType<IPacket>().Ignore(s => s.ValidationResult!);
            TypeAdapterConfig.GlobalSettings.ForDestinationType<I18NString>().BeforeMapping(s => s.Clear());
            TypeAdapterConfig.GlobalSettings.Default.IgnoreMember((member, side)
                => ((side == MemberSide.Destination) && member.Type.GetInterfaces().Contains(typeof(IEntity))) || (member.Type.GetGenericArguments().Any() && member.Type.GetGenericArguments()[0].GetInterfaces().Contains(typeof(IEntity))));
            TypeAdapterConfig.GlobalSettings.When(s => !s.SourceType.IsAssignableFrom(s.DestinationType))
                .IgnoreMember((member, side) => typeof(I18NString).IsAssignableFrom(member.Type));
            TypeAdapterConfig.GlobalSettings.EnableJsonMapping();
            TypeAdapterConfig.GlobalSettings.Compiler = exp => exp.CompileFast();
            var containerBuilder = new ContainerBuilder();
            InitializeContainer(containerBuilder);
            containerBuilder.Populate(services);
            var container = containerBuilder.Build();
            return new AutofacServiceProvider(container);
        }

        [UsedImplicitly]
        public void Configure(IApplicationBuilder app)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "NosCore World API"));
            app.UseAuthentication();
            app.UseRouting();
            app.UseAuthorization();

            LogLanguage.Language = app.ApplicationServices.GetRequiredService<IOptions<WorldConfiguration>>().Value.Language;
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}