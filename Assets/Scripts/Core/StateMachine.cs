using System;
using System.Collections.Generic;
using UnityEngine;

namespace TirumalaAR.Core
{
    public interface IState
    {
        string Name { get; }
        void Enter();
        void Tick(float deltaTime);
        void Exit();
    }

    /// <summary>
    /// Generic finite state machine. Drives the navigation session lifecycle
    /// (Initialising → AcquiringGps → Localising → Navigating → Recovering → Arrived).
    /// </summary>
    public sealed class StateMachine
    {
        readonly Dictionary<Type, IState> m_States = new Dictionary<Type, IState>();

        public IState CurrentState { get; private set; }
        public float TimeInState { get; private set; }
        public event Action<IState, IState> StateChanged;

        public void AddState(IState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            m_States[state.GetType()] = state;
        }

        public bool IsIn<TState>() where TState : IState => CurrentState is TState;

        public void ChangeState<TState>() where TState : IState
        {
            if (!m_States.TryGetValue(typeof(TState), out var next))
            {
                Debug.LogError($"[StateMachine] State '{typeof(TState).Name}' was never added.");
                return;
            }

            ChangeState(next);
        }

        public void ChangeState(IState next)
        {
            if (ReferenceEquals(next, CurrentState))
                return;

            var previous = CurrentState;
            previous?.Exit();
            CurrentState = next;
            TimeInState = 0f;
            CurrentState?.Enter();
            StateChanged?.Invoke(previous, CurrentState);
        }

        public void Tick(float deltaTime)
        {
            TimeInState += deltaTime;
            CurrentState?.Tick(deltaTime);
        }
    }

    /// <summary>Convenience state built from delegates, for short lifecycle states.</summary>
    public sealed class DelegateState : IState
    {
        readonly Action m_OnEnter;
        readonly Action<float> m_OnTick;
        readonly Action m_OnExit;

        public string Name { get; }

        public DelegateState(string name, Action onEnter = null, Action<float> onTick = null, Action onExit = null)
        {
            Name = name;
            m_OnEnter = onEnter;
            m_OnTick = onTick;
            m_OnExit = onExit;
        }

        public void Enter() => m_OnEnter?.Invoke();
        public void Tick(float deltaTime) => m_OnTick?.Invoke(deltaTime);
        public void Exit() => m_OnExit?.Invoke();
    }
}
