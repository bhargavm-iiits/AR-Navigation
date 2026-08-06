using System;
using System.Collections.Generic;
using UnityEngine;

namespace TirumalaAR.Core
{
    /// <summary>
    /// Strongly typed publish/subscribe bus (observer pattern). Systems communicate through
    /// events instead of holding references to each other, which keeps the AR, navigation,
    /// audio and UI layers independent.
    /// </summary>
    public static class EventBus
    {
        static readonly Dictionary<Type, Delegate> s_Handlers = new Dictionary<Type, Delegate>();

        public static void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : struct
        {
            if (handler == null)
                return;

            var type = typeof(TEvent);
            s_Handlers[type] = s_Handlers.TryGetValue(type, out var existing)
                ? Delegate.Combine(existing, handler)
                : handler;
        }

        public static void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : struct
        {
            if (handler == null)
                return;

            var type = typeof(TEvent);
            if (!s_Handlers.TryGetValue(type, out var existing))
                return;

            var remaining = Delegate.Remove(existing, handler);
            if (remaining == null)
                s_Handlers.Remove(type);
            else
                s_Handlers[type] = remaining;
        }

        /// <summary>
        /// Invokes every subscriber. A throwing subscriber is logged and skipped so that one bad
        /// listener cannot break the navigation loop mid-walk.
        /// </summary>
        public static void Publish<TEvent>(TEvent evt) where TEvent : struct
        {
            if (!s_Handlers.TryGetValue(typeof(TEvent), out var existing))
                return;

            if (existing is not Action<TEvent> typed)
                return;

            foreach (var invocation in typed.GetInvocationList())
            {
                try
                {
                    ((Action<TEvent>)invocation)(evt);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[EventBus] Subscriber of {typeof(TEvent).Name} threw: {e}");
                }
            }
        }

        public static void Clear() => s_Handlers.Clear();
    }
}
