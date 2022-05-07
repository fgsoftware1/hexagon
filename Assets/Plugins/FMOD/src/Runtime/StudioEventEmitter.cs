﻿using System;
using System.Collections.Generic;
using System.Threading;
using FMOD.Studio;
using UnityEngine;

namespace FMODUnity
{
    [AddComponentMenu("FMOD Studio/FMOD Studio Event Emitter")]
    public class StudioEventEmitter : EventHandler
    {
        private const string SnapshotString = "snapshot";

        [EventRef] public string Event = "";

        public EmitterGameEvent PlayEvent = EmitterGameEvent.None;
        public EmitterGameEvent StopEvent = EmitterGameEvent.None;
        public bool AllowFadeout = true;
        public bool TriggerOnce;
        public bool Preload;
        public ParamRef[] Params = new ParamRef[0];
        public bool OverrideAttenuation;
        public float OverrideMinDistance = -1.0f;
        public float OverrideMaxDistance = -1.0f;
        private readonly List<ParamRef> cachedParams = new List<ParamRef>();

        protected EventDescription eventDescription;

        private bool hasTriggered;

        protected EventInstance instance;
        private bool isOneshot;
        private bool isQuitting;
        public EventDescription EventDescription => eventDescription;
        public EventInstance EventInstance => instance;

        public bool IsActive { get; private set; }

        public float MaxDistance
        {
            get
            {
                if (OverrideAttenuation) return OverrideMaxDistance;

                if (!eventDescription.isValid()) Lookup();

                float maxDistance;
                eventDescription.getMaximumDistance(out maxDistance);
                return maxDistance;
            }
        }

        private void Start()
        {
            RuntimeUtils.EnforceLibraryOrder();
            if (Preload)
            {
                Lookup();
                eventDescription.loadSampleData();
                RuntimeManager.StudioSystem.update();
                LOADING_STATE loadingState;
                eventDescription.getSampleLoadingState(out loadingState);
                while (loadingState == LOADING_STATE.LOADING)
                {
#if WINDOWS_UWP
                    System.Threading.Tasks.Task.Delay(1).Wait();
#else
                    Thread.Sleep(1);
#endif
                    eventDescription.getSampleLoadingState(out loadingState);
                }
            }

            HandleGameEvent(EmitterGameEvent.ObjectStart);
        }

        private void OnDestroy()
        {
            if (!isQuitting)
            {
                HandleGameEvent(EmitterGameEvent.ObjectDestroy);

                if (instance.isValid())
                {
                    RuntimeManager.DetachInstanceFromGameObject(instance);
                    if (eventDescription.isValid() && isOneshot)
                    {
                        instance.release();
                        instance.clearHandle();
                    }
                }

                RuntimeManager.DeregisterActiveEmitter(this);

                if (Preload) eventDescription.unloadSampleData();
            }
        }

        private void OnApplicationQuit()
        {
            isQuitting = true;
        }

        protected override void HandleGameEvent(EmitterGameEvent gameEvent)
        {
            if (PlayEvent == gameEvent) Play();
            if (StopEvent == gameEvent) Stop();
        }

        private void Lookup()
        {
            eventDescription = RuntimeManager.GetEventDescription(Event);

            if (eventDescription.isValid())
                for (var i = 0; i < Params.Length; i++)
                {
                    PARAMETER_DESCRIPTION param;
                    eventDescription.getParameterDescriptionByName(Params[i].Name, out param);
                    Params[i].ID = param.id;
                }
        }

        public void Play()
        {
            if (TriggerOnce && hasTriggered) return;

            if (string.IsNullOrEmpty(Event)) return;

            cachedParams.Clear();

            if (!eventDescription.isValid()) Lookup();

            if (!Event.StartsWith(SnapshotString, StringComparison.CurrentCultureIgnoreCase))
                eventDescription.isOneshot(out isOneshot);

            bool is3D;
            eventDescription.is3D(out is3D);

            IsActive = true;

            if (is3D && !isOneshot && Settings.Instance.StopEventsOutsideMaxDistance)
            {
                RuntimeManager.RegisterActiveEmitter(this);
                RuntimeManager.UpdateActiveEmitter(this, true);
            }
            else
            {
                PlayInstance();
            }
        }

        public void PlayInstance()
        {
            if (!instance.isValid()) instance.clearHandle();

            // Let previous oneshot instances play out
            if (isOneshot && instance.isValid())
            {
                instance.release();
                instance.clearHandle();
            }

            bool is3D;
            eventDescription.is3D(out is3D);

            if (!instance.isValid())
            {
                eventDescription.createInstance(out instance);

                // Only want to update if we need to set 3D attributes
                if (is3D)
                {
                    var rigidBody = GetComponent<Rigidbody>();
                    var rigidBody2D = GetComponent<Rigidbody2D>();
                    var transform = GetComponent<Transform>();
                    if (rigidBody)
                    {
                        instance.set3DAttributes(RuntimeUtils.To3DAttributes(gameObject, rigidBody));
                        RuntimeManager.AttachInstanceToGameObject(instance, transform, rigidBody);
                    }
                    else
                    {
                        instance.set3DAttributes(RuntimeUtils.To3DAttributes(gameObject, rigidBody2D));
                        RuntimeManager.AttachInstanceToGameObject(instance, transform, rigidBody2D);
                    }
                }
            }

            foreach (var param in Params) instance.setParameterByID(param.ID, param.Value);

            foreach (var cachedParam in cachedParams) instance.setParameterByID(cachedParam.ID, cachedParam.Value);

            if (is3D && OverrideAttenuation)
            {
                instance.setProperty(EVENT_PROPERTY.MINIMUM_DISTANCE, OverrideMinDistance);
                instance.setProperty(EVENT_PROPERTY.MAXIMUM_DISTANCE, OverrideMaxDistance);
            }

            instance.start();

            hasTriggered = true;
        }

        public void Stop()
        {
            RuntimeManager.DeregisterActiveEmitter(this);
            IsActive = false;
            cachedParams.Clear();
            StopInstance();
        }

        public void StopInstance()
        {
            if (TriggerOnce && hasTriggered) RuntimeManager.DeregisterActiveEmitter(this);

            if (instance.isValid())
            {
                instance.stop(AllowFadeout ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
                instance.release();
                instance.clearHandle();
            }
        }

        public void SetParameter(string name, float value, bool ignoreseekspeed = false)
        {
            if (Settings.Instance.StopEventsOutsideMaxDistance && IsActive)
            {
                var cachedParam = cachedParams.Find(x => x.Name == name);

                if (cachedParam == null)
                {
                    PARAMETER_DESCRIPTION paramDesc;
                    eventDescription.getParameterDescriptionByName(name, out paramDesc);

                    cachedParam = new ParamRef();
                    cachedParam.ID = paramDesc.id;
                    cachedParam.Name = paramDesc.name;
                    cachedParams.Add(cachedParam);
                }

                cachedParam.Value = value;
            }

            if (instance.isValid()) instance.setParameterByName(name, value, ignoreseekspeed);
        }

        public void SetParameter(PARAMETER_ID id, float value, bool ignoreseekspeed = false)
        {
            if (Settings.Instance.StopEventsOutsideMaxDistance && IsActive)
            {
                var cachedParam = cachedParams.Find(x => x.ID.Equals(id));

                if (cachedParam == null)
                {
                    PARAMETER_DESCRIPTION paramDesc;
                    eventDescription.getParameterDescriptionByID(id, out paramDesc);

                    cachedParam = new ParamRef();
                    cachedParam.ID = paramDesc.id;
                    cachedParam.Name = paramDesc.name;
                    cachedParams.Add(cachedParam);
                }

                cachedParam.Value = value;
            }

            if (instance.isValid()) instance.setParameterByID(id, value, ignoreseekspeed);
        }

        public bool IsPlaying()
        {
            if (instance.isValid())
            {
                PLAYBACK_STATE playbackState;
                instance.getPlaybackState(out playbackState);
                return playbackState != PLAYBACK_STATE.STOPPED;
            }

            return false;
        }
    }
}