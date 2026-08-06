using System;
using System.Collections.Generic;

namespace TirumalaAR.Core
{
    /// <summary>
    /// Minimal constructor-less dependency injection container.
    /// The composition root (<c>AppBootstrap</c>) registers every service here; consumers resolve
    /// interfaces rather than concrete types so that implementations stay swappable.
    /// </summary>
    public sealed class ServiceLocator
    {
        static ServiceLocator s_Current;

        readonly Dictionary<Type, object> m_Services = new Dictionary<Type, object>();
        readonly Dictionary<Type, Func<object>> m_Factories = new Dictionary<Type, Func<object>>();

        public static ServiceLocator Current => s_Current ??= new ServiceLocator();

        /// <summary>Discards every registration. Called when the navigation scene is torn down.</summary>
        public static void Reset()
        {
            s_Current?.m_Services.Clear();
            s_Current?.m_Factories.Clear();
            s_Current = null;
        }

        public void Register<TService>(TService instance) where TService : class
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            m_Services[typeof(TService)] = instance;
        }

        /// <summary>Registers a lazily constructed singleton. The factory runs on first resolve.</summary>
        public void RegisterLazy<TService>(Func<TService> factory) where TService : class
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            m_Factories[typeof(TService)] = () => factory();
        }

        public bool IsRegistered<TService>() where TService : class
        {
            var type = typeof(TService);
            return m_Services.ContainsKey(type) || m_Factories.ContainsKey(type);
        }

        public TService Resolve<TService>() where TService : class
        {
            if (TryResolve<TService>(out var service))
                return service;

            throw new InvalidOperationException(
                $"Service '{typeof(TService).Name}' has not been registered. " +
                "Check that AppBootstrap ran before this object's Start().");
        }

        public bool TryResolve<TService>(out TService service) where TService : class
        {
            var type = typeof(TService);

            if (m_Services.TryGetValue(type, out var existing))
            {
                service = (TService)existing;
                return true;
            }

            if (m_Factories.TryGetValue(type, out var factory))
            {
                var created = factory();
                m_Services[type] = created;
                m_Factories.Remove(type);
                service = (TService)created;
                return true;
            }

            service = null;
            return false;
        }

        public void Unregister<TService>() where TService : class
        {
            m_Services.Remove(typeof(TService));
            m_Factories.Remove(typeof(TService));
        }
    }
}
