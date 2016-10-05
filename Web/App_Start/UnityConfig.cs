using System;
using System.Collections;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using log4net;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Infrastructure;
using Microsoft.Exchange.WebServices.Data;
using Microsoft.Practices.Unity;
using RightpointLabs.ConferenceRoom.Domain;
using RightpointLabs.ConferenceRoom.Domain.Models.Entities;
using RightpointLabs.ConferenceRoom.Domain.Repositories;
using RightpointLabs.ConferenceRoom.Domain.Services;
using RightpointLabs.ConferenceRoom.Infrastructure.Models;
using RightpointLabs.ConferenceRoom.Infrastructure.Persistence;
using RightpointLabs.ConferenceRoom.Infrastructure.Persistence.Repositories;
using RightpointLabs.ConferenceRoom.Infrastructure.Services;
using RightpointLabs.ConferenceRoom.Web.SignalR;
using Unity.WebApi;

namespace RightpointLabs.ConferenceRoom.Web
{
    public static class UnityConfig
    {
        private static ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static void RegisterComponents()
        {
            var container = new UnityContainer();

            var connectionString = System.Web.Configuration.WebConfigurationManager.ConnectionStrings["Mongo"].ConnectionString;
            var databaseName = ConfigurationManager.AppSettings["MongoDatabaseName"];
            container.RegisterType<IMongoConnectionHandler, MongoConnectionHandler>(
                new ContainerControlledLifetimeManager(),
                string.IsNullOrEmpty(databaseName) ? new InjectionConstructor(connectionString) : new InjectionConstructor(connectionString, databaseName));

            container.RegisterType<ExchangeConferenceRoomServiceConfiguration>(new HierarchicalLifetimeManager(),
                new InjectionFactory(
                    c =>
                        CreateOrganizationalService(c, "Exchange",
                            _ => new ExchangeConferenceRoomServiceConfiguration() {
                                IgnoreFree = _.IgnoreFree,
                                ImpersonateForAllCalls = _.ImpersonateForAllCalls,
                                UseChangeNotification = _.UseChangeNotification,
                                EmailDomains = ((IEnumerable)_.EmailDomains).Cast<string>().ToArray(),
                            })));

            container.RegisterType<Func<ExchangeService>>(new HierarchicalLifetimeManager(),
                new InjectionFactory(
                    c =>
                        CreateOrganizationalService(c, "Exchange",
                            _ => ExchangeConferenceRoomService.GetExchangeServiceBuilder(_.Username, _.Password, _.ServiceUrl))));

            container.RegisterType<IInstantMessagingService>(new HierarchicalLifetimeManager(),
                new InjectionFactory(
                    c =>
                        CreateOrganizationalService(c, "Exchange",
                            _ => new InstantMessagingService(_.Username, _.Password))));

            container.RegisterType<ISmsMessagingService>(new HierarchicalLifetimeManager(),
                new InjectionFactory(
                    c =>
                        CreateOrganizationalService(c, "Plivo",
                            _ => new SmsMessagingService(_.AuthId, _.AuthToken, _.From))));

            container.RegisterType<IGdoService>(new ContainerControlledLifetimeManager(),
                new InjectionFactory(
                    c =>
                        CreateOrganizationalService(c, "GDO",
                            _ => new GdoService(new Uri(_.BaseUrl), _.ApiKey, _.Username, _.Password))));

            container.RegisterType<IBroadcastService, SignalrBroadcastService>(new HierarchicalLifetimeManager());
            container.RegisterType<IConnectionManager>(new ContainerControlledLifetimeManager(), new InjectionFactory(c => GlobalHost.ConnectionManager));
            container.RegisterType<IDateTimeService>(new ContainerControlledLifetimeManager(), new InjectionFactory(c => new DateTimeService(TimeSpan.FromHours(0))));

            container.RegisterType<HttpRequestBase>(new HierarchicalLifetimeManager(), new InjectionFactory(c => new HttpRequestWrapper(HttpContext.Current.Request)));

            container.RegisterType<ISmsAddressLookupService, SmsAddressLookupService>(new HierarchicalLifetimeManager());
            container.RegisterType<ISignatureService, SignatureService>(new ContainerControlledLifetimeManager());
            container.RegisterType<IConferenceRoomService, ExchangeConferenceRoomService>(new HierarchicalLifetimeManager());
            container.RegisterType<IMeetingRepository, MeetingRepository>(new HierarchicalLifetimeManager());
            container.RegisterType<IExchangeServiceManager, ExchangeServiceManager>(new ContainerControlledLifetimeManager());
            container.RegisterType<IMeetingCacheService, MeetingCacheService>(new ContainerControlledLifetimeManager()); // singleton cache
            container.RegisterType<ISimpleTimedCache, SimpleTimedCache>(new ContainerControlledLifetimeManager()); // singleton cache
            container.RegisterType<IRoomMetadataRepository, RoomMetadataRepository>(new HierarchicalLifetimeManager());
            container.RegisterType<IFloorRepository, FloorRepository>(new HierarchicalLifetimeManager());
            container.RegisterType<IBuildingRepository, BuildingRepository>(new HierarchicalLifetimeManager());
            container.RegisterType<IDeviceRepository, DeviceRepository>(new HierarchicalLifetimeManager());
            container.RegisterType<IOrganizationRepository, OrganizationRepository>(new HierarchicalLifetimeManager());
            container.RegisterType<IOrganizationServiceConfigurationRepository, OrganizationServiceConfigurationRepository>(new HierarchicalLifetimeManager());
            container.RegisterType<IGlobalAdministratorRepository, GlobalAdministratorRepository>(new HierarchicalLifetimeManager());
            container.RegisterType<IContextService, ContextService>(new HierarchicalLifetimeManager());
            container.RegisterType<ITokenService, TokenService>(new HierarchicalLifetimeManager(), new InjectionFactory(c => 
                new TokenService(
                    ConfigurationManager.AppSettings["TokenIssuer"],
                    ConfigurationManager.AppSettings["TokenAudience"],
                    ConfigurationManager.AppSettings["TokenKey"])));

            container.RegisterType<IIOCContainer, UnityIOCContainer>(new TransientLifetimeManager(), new InjectionFactory(c => new UnityIOCContainer(c, false)));
            container.RegisterType<ITokenProvider, HttpTokenProvider>(new HierarchicalLifetimeManager());

            // create change notifier in a child container and register as a singleton with the main container (avoids creating it's dependencies in the global container)
            var child = container.CreateChildContainer();
            var changeNotificationService = child.Resolve<ChangeNotificationService>();
            container.RegisterInstance(typeof(IChangeNotificationService), changeNotificationService, new ContainerControlledLifetimeManager());

            
            // register all your components with the container here
            // it is NOT necessary to register your controllers
            
            // e.g. container.RegisterType<ITestService, TestService>();
            
            GlobalConfiguration.Configuration.DependencyResolver = new UnityDependencyResolver(container);
            DependencyResolver.SetResolver(new Unity.Mvc5.UnityDependencyResolver(container));
        }

        private class UnityIOCContainer : IIOCContainer
        {
            private readonly IUnityContainer _unityContainer;
            private readonly bool _owned;

            public UnityIOCContainer(IUnityContainer unityContainer, bool owned)
            {
                _unityContainer = unityContainer;
                _owned = owned;
            }

            public IIOCContainer CreateChildContainer()
            {
                return new UnityIOCContainer(_unityContainer.CreateChildContainer(), true);
            }

            public object Resolve(Type type)
            {
                return _unityContainer.Resolve(type);
            }

            public T Resolve<T>()
            {
                return (T)Resolve(typeof(T));
            }

            public void Dispose()
            {
                if (_owned)
                {
                    _unityContainer.Dispose();
                }
            }

            public void RegisterInstance<TI>(TI instance)
            {
                _unityContainer.RegisterInstance(typeof(TI), instance, new HierarchicalLifetimeManager());
            }
        }

        private static T CreateOrganizationalService<T>(IUnityContainer container, string serviceName, Func<dynamic, T> builder)
        {
            var org = container.Resolve<IContextService>().CurrentOrganization;
            if (null == org)
            {
                log.WarnFormat("Unable to load configuration for {0}, no current organization", serviceName);
                return default(T);
            }
            var config = container.Resolve<IOrganizationServiceConfigurationRepository>().Get(org.Id, serviceName);
            if (null == config)
            {
                log.WarnFormat("Unable to load configuration for {0}, no configuration found for current organization ({1})", serviceName, org.Id);
                return default(T);
            }
            try
            {
                return builder(config.Parameters);
            }
            catch (Exception ex)
            {
                log.WarnFormat("Failed to create {0} for {1}: {2}", serviceName, org.Id, ex);
                return default(T);
            }
        }

        private class SignalrBroadcastService : IBroadcastService
        {
            private static ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

            private readonly IConnectionManager _connectionManager;

            public SignalrBroadcastService(IConnectionManager connectionManager)
            {
                _connectionManager = connectionManager;
            }

            public void BroadcastUpdate(OrganizationEntity org, string roomAddress)
            {
                var context = _connectionManager.GetHubContext<UpdateHub>();
                var groupName = UpdateHub.GetGroupName(org, roomAddress);
                log.DebugFormat("Broadcasting update to {0} for {1}", groupName, roomAddress);

                context.Clients.Group(groupName).Update(roomAddress);
            }
        }
    }
}