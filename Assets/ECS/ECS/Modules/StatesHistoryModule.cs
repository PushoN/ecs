﻿#if STATES_HISTORY_MODULE_SUPPORT
using System.Collections.Generic;
using Tick = System.UInt64;
using RPCId = System.Int32;

namespace ME.ECS.StatesHistory {

    #if ECS_COMPILE_IL2CPP_OPTIONS
    [Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.NullChecks, false),
     Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false),
     Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.DivideByZeroChecks, false)]
    #endif
    public class CircularQueue<T> where T : class {

        private readonly Dictionary<Tick, T> data;
        private readonly uint capacity;
        private readonly uint ticksPerState;
        private bool beginSet;

        public CircularQueue(uint ticksPerState, uint capacity) {

            this.data = new Dictionary<Tick, T>((int)capacity);
            this.capacity = capacity;
            this.ticksPerState = ticksPerState;
            this.beginSet = false;

        }

        public T Get(Tick tick) {

            Tick nearestTick = tick - tick % this.ticksPerState;
            T state;
            if (this.data.TryGetValue(nearestTick, out state) == true) {

                return state;

            }

            return default;

        }

        public void BeginSet() {

            this.beginSet = true;

        }

        public void EndSet() {
            
            this.beginSet = false;
            
        }

        public T Set(Tick tick, T data, out T removedData) {

            T result;
            removedData = null;
            
            var nearestTick = (long)(tick - tick % this.ticksPerState);
            var oldestTick = nearestTick - this.ticksPerState * this.capacity;
            if (oldestTick < 0L) oldestTick = 0L;
            if (oldestTick >= 0L) {

                var key = (Tick)oldestTick;
                if (this.data.TryGetValue(key, out result) == true) {

                    // we've found oldest-tick record - need to remove it
                    removedData = result;
                    this.data.Remove(key);

                }

            }

            var searchTick = (Tick)nearestTick;
            if (this.data.TryGetValue(searchTick, out result) == true) {

                this.data[searchTick] = data;
                return result;

            } else {
                
                this.data.Add(searchTick, data);
                
            }

            if (this.beginSet == false && this.data.Count != this.capacity) {

                foreach (var item in this.data) {
                    
                    UnityEngine.Debug.Log("Item: " + item.Key + ": " + item.Value);
                    
                }

                throw new OutOfBoundsException("CircularQueue is out of bounds."+
                                               " Count: " + this.data.Count +
                                               ", Capacity: " + this.capacity +
                                               ", SourceTick: " + tick +
                                               ", NearestTick: " + nearestTick +
                                               ", OldestTick: " + oldestTick +
                                               ", RemovedData: " + (removedData != null));
                
            }

            return default;

        }
        
    }

    [System.Serializable]
    public struct HistoryEvent {

        // Header
        /// <summary>
        /// Event tick
        /// </summary>
        public Tick tick;
        /// <summary>
        /// Global event order (for example: you have 30 players on the map, each has it's own index)
        /// </summary>
        public int order;
        /// <summary>
        /// Local event order (order would be the first, then localOrder applies)
        /// </summary>
        public int localOrder;
        
        // Data
        /// <summary>
        /// Object Id to be called on (see NetworkModule::RegisterObject)
        /// </summary>
        public int objId;
        /// <summary>
        /// Group Id of objects (see NetworkModule::RegisterObject).
        /// One object could be registered in different groups at the same time.
        /// 0 by default (Common group)
        /// </summary>
        public int groupId;
        /// <summary>
        /// Rpc Id is a method Id (see NetworkModule::RegisterRPC) 
        /// </summary>
        public RPCId rpcId;
        /// <summary>
        /// Parameters of method
        /// </summary>
        public object[] parameters;

        public override string ToString() {
            
            return "Tick: " + this.tick + ", Order: " + this.order + ", LocalOrder: " + this.localOrder + ", objId: " + this.objId + ", groupId: " + this.groupId + ", rpcId: " + this.rpcId + ", parameters: " + (this.parameters != null ? this.parameters.Length : 0);
            
        }

    }

    public interface IEventRunner {

        void RunEvent(HistoryEvent historyEvent);

    }

    public class OutOfBoundsException : System.Exception {

        public OutOfBoundsException() : base("ME.ECS Exception") { }
        public OutOfBoundsException(string message) : base(message) { }

    }
    
    public class StateNotFoundException : System.Exception {

        public StateNotFoundException() : base("ME.ECS Exception") { }
        public StateNotFoundException(string message) : base(message) { }

    }

    public interface IStatesHistoryModule<TState> : IModule<TState> where TState : class, IState<TState> {

        void AddEvents(IList<HistoryEvent> historyEvents);
        void AddEvent(HistoryEvent historyEvent);

        void BeginAddEvents();
        void EndAddEvents();

        Tick GetTickByTime(double seconds);
        TState GetStateBeforeTick(Tick tick);

        Tick GetTick();
        void SetTick(Tick tick);

        void PlayEventsForTick(Tick tick);
        void RunEvent(HistoryEvent historyEvent);

        void SetEventRunner(IEventRunner eventRunner);

        int GetEventsAddedCount();

    }

    #if ECS_COMPILE_IL2CPP_OPTIONS
    [Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.NullChecks, false),
     Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false),
     Unity.IL2CPP.CompilerServices.Il2CppSetOptionAttribute(Unity.IL2CPP.CompilerServices.Option.DivideByZeroChecks, false)]
    #endif
    public abstract class StatesHistoryModule<TState> : IStatesHistoryModule<TState>, IModuleValidation where TState : class, IState<TState> {

        private CircularQueue<TState> states;
        private Dictionary<Tick, SortedList<long, HistoryEvent>> events;
        private Tick currentTick;
        private Tick maxTick;
        private bool prewarmed;
        private Tick beginAddEventsTick;
        private bool beginAddEvents;
        private IEventRunner eventRunner;

        private int statEventsAdded;

        public IWorld<TState> world { get; set; }

        void IModule<TState>.OnConstruct() {
            
            this.states = new CircularQueue<TState>(this.GetTicksPerState(), this.GetQueueCapacity());
            this.events = PoolDictionary<ulong, SortedList<long, HistoryEvent>>.Spawn(1000);
            PoolSortedList<int, HistoryEvent>.Prewarm(1000, 2);
            
            this.world.SetStatesHistoryModule(this);

        }
        
        void IModule<TState>.OnDeconstruct() { }

        void IStatesHistoryModule<TState>.SetEventRunner(IEventRunner eventRunner) {

            this.eventRunner = eventRunner;

        }

        protected virtual uint GetQueueCapacity() {

            return 10u;

        }

        /// <summary>
        /// System will copy current state every N ticks
        /// </summary>
        /// <returns></returns>
        protected virtual uint GetTicksPerState() {

            return 100u;

        }

        public int GetEventsAddedCount() {

            return this.statEventsAdded;

        }

        public void BeginAddEvents() {

            this.beginAddEventsTick = this.currentTick;
            this.beginAddEvents = true;

        }

        public void EndAddEvents() {

            if (this.beginAddEvents == true) {
                
                this.world.SetPreviousTick(this.beginAddEventsTick);
                this.world.Simulate(this.currentTick);
                
            }
            
            this.beginAddEvents = false;
            
        }

        public void AddEvents(IList<HistoryEvent> historyEvents) {
            
            this.BeginAddEvents();
            for (int i = 0; i < historyEvents.Count; ++i) this.AddEvent(historyEvents[i]);
            this.EndAddEvents();
            
        }

        public void AddEvent(HistoryEvent historyEvent) {

            ++this.statEventsAdded;
            
            this.ValidatePrewarm();

            if (historyEvent.tick <= 0UL) {

                // Tick fix if it is zero
                historyEvent.tick = 1UL;

            }

            SortedList<long, HistoryEvent> list;
            if (this.events.TryGetValue(historyEvent.tick, out list) == true) {
                
                list.Add(MathUtils.GetKey(historyEvent.order, historyEvent.localOrder), historyEvent);

            } else {

                list = PoolSortedList<long, HistoryEvent>.Spawn(2);
                list.Add(MathUtils.GetKey(historyEvent.order, historyEvent.localOrder), historyEvent);
                this.events.Add(historyEvent.tick, list);

            }

            if (this.currentTick >= historyEvent.tick) {

                var tick = this.currentTick;
                var searchTick = historyEvent.tick - 1;
                var historyState = this.GetStateBeforeTick(searchTick);
                //var cloneState = this.world.CreateState();
                //cloneState.Initialize(this.world, freeze: true, restore: false);
                if (historyState != null) {
                    
                    // State found - simulate from this state to current tick
                    this.world.GetState().CopyFrom(historyState);
                    this.world.GetState().tick = tick;
                    //cloneState.CopyFrom(state);
                    //this.world.SetState(cloneState);
                    if (this.beginAddEvents == false) {
                        
                        this.world.SetPreviousTick(historyState.tick);
                        this.world.Simulate(tick);
                        
                    }
                    
                } else {
                    
                    // Previous state was not found - need to rewind from initial state
                    var resetState = this.world.GetResetState();
                    this.world.GetState().CopyFrom(resetState);
                    this.world.GetState().tick = tick;
                    //cloneState.CopyFrom(resetState);
                    //this.world.SetState(cloneState);
                    if (this.beginAddEvents == false) {
                        
                        this.world.SetPreviousTick(resetState.tick);
                        this.world.Simulate(tick);
                        
                    }
                    
                    //throw new StateNotFoundException("Tick: " + searchTick);

                }

            }

        }

        public TState GetStateBeforeTick(Tick tick) {

            this.ValidatePrewarm();
            
            //TState state = default;
            //var minDelta = long.MaxValue;
            var state = this.states.Get(tick);
            /*for (int i = 0; i < arr.Length; ++i) {

                var item = arr[i];
                if (item != null && item.tick < tick) {

                    var delta = (long)(item.tick - tick);
                    if (delta < 0L) delta = -delta;
                    if (delta < minDelta) {

                        minDelta = delta;
                        state = item;

                    }

                }

            }*/

            return state;

        }

        public Tick GetTickByTime(double seconds) {

            var tick = (seconds / this.world.GetTickTime());
            return (Tick)System.Math.Floor(tick);

        }

        public Tick GetTick() {

            return this.currentTick;

        }

        public void SetTick(Tick tick) {

            //UnityEngine.Debug.Log(UnityEngine.Time.frameCount + " Rewind to " + tick + " from " + this.currentTick);

            this.currentTick = tick;
            //this.world.SetTimeSinceStart(tick * this.world.GetTickTime());
            if (tick > this.maxTick) this.maxTick = tick;

        }

        public virtual bool CouldBeAdded() {

            return this.world.GetTickTime() > 0f;

        }

        public void Update(TState state, float deltaTime) {

            this.ValidatePrewarm();
            
            var tick = this.GetTickByTime(this.world.GetTimeSinceStart());
            if (tick != this.currentTick) {

                this.currentTick = tick;
                state.tick = this.currentTick;
                
            }

        }

        public void PlayEventsForTick(Tick tick) {

            if (tick > 0UL && this.currentTick % this.GetTicksPerState() == 0) {

                this.StoreState(tick);

            }
            
            SortedList<long, HistoryEvent> list;
            if (this.events.TryGetValue(tick, out list) == true) {

                var values = list.Values;
                for (int i = 0, count = values.Count; i < count; ++i) {

                    this.RunEvent(values[i]);

                }

            }

        }

        public void RunEvent(HistoryEvent historyEvent) {
            
            //UnityEngine.Debug.LogError("Run event tick: " + historyEvent.tick + ", method: " + historyEvent.id);
            if (this.eventRunner != null) this.eventRunner.RunEvent(historyEvent);
            
        }

        private void ValidatePrewarm() {
            
            if (this.prewarmed == false) {
                
                this.Prewarm();
                this.prewarmed = true;

            }

        }

        private void Prewarm() {

            this.states.BeginSet();
            for (uint i = 0; i < this.GetQueueCapacity(); ++i) {
                
                this.StoreState(i * this.GetTicksPerState());
                
            }
            this.states.EndSet();

        }

        private void StoreState(Tick tick) {

            var newState = this.world.CreateState();
            newState.Initialize(this.world, freeze: true, restore: false);
            var state = this.world.GetState();
            newState.CopyFrom(state);
            newState.tick = tick;

            var oldState = this.states.Set(tick, newState, out var removeState);
            if (oldState != null) this.world.ReleaseState(ref oldState);
            if (removeState != null) this.world.ReleaseState(ref removeState);

        }

    }

}
#endif