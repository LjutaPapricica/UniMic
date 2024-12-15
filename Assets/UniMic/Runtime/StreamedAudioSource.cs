﻿using System;

using UnityEngine;

namespace Adrenak.UniMic {
    /// <summary>
    /// Plays back the streaming audio provided to it.
    /// Automatically adjusts to any changing sampling frequency
    /// or channel count. Samples of varying lengths can be sent,
    /// however this could impact performance. Consider keeping
    /// sample length constant.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class StreamedAudioSource : MonoBehaviour {
        int bufferDurationMS = 32;

        /// <summary>
        /// Determines the duration of the internal buffer. DEFAULT = 32
        /// This value determines the length of the internal AudioClip
        /// which is sample count (fed in the Feed method) x this value.
        /// There's usually no reason to change this. A high value like
        /// 32 will take some more memory but will ensure audio jitters
        /// are minimal.
        /// </summary>
        public int BufferDurationMS {
            get => bufferDurationMS;
            set {
                if (value <= 2)
                    throw new Exception("BufferDurationMS cannot be 2 or more");

                if(bufferDurationMS != value) {
                    bufferDurationMS = value;
                    if(Buffering) {
                        Stop();
                        Clip = null;
                        Play();
                    }
                }
            }
        }

        /// <summary>
        /// The sampling frequency of the last samples this instance played
        /// </summary>
        public int SamplingFrequency { get; private set; }

        /// <summary>
        /// The channel count in the last samples this instance played
        /// </summary>
        public int ChannelCount { get; private set; }

        /// <summary>
        /// Whether this instance is currently playing
        /// </summary>
        public bool IsPlaying { get; private set; }

        /// <summary>
        /// Whether this instance is currently paused and buffering
        /// while waiting for more audio before it resumes.
        /// </summary>
        public bool Buffering => !UnityAudioSource.isPlaying && IsPlaying;

        /// <summary>
        /// Provides access to the AudioSource used for playing the audio.
        /// ***WARNING*** Do not invoke play or pause methods on this object.
        /// The play, pause and unpausing of this <see cref="AudioSource"/> 
        /// object is done by the <see cref="StreamedAudioSource"/> object
        /// and doing so from outside will affect playback.
        /// 
        /// If you want to start or stop playback, use <see cref="Play"/>
        /// or <see cref="Stop"/> methods on this object directly.
        /// </summary>
        public AudioSource UnityAudioSource {
            get {
                if (source == null) {
                    source = gameObject.GetComponent<AudioSource>();
                    source.loop = true;
                    source.playOnAwake = false;
                }
                return source;
            }
        }
        AudioSource source;

        AudioClip clip;
        AudioClip Clip {
            get {
                if(clip == null) {
                    clip = AudioClip.Create(
                        "clip",
                        BufferDurationMS * samplesLen,
                        ChannelCount,
                        SamplingFrequency,
                        false
                    );
                    var empty = new float[clip.samples];
                    for (int i = 0; i < empty.Length; i++)
                        empty[i] = 0;
                    clip.SetData(empty, 0);
                    UnityAudioSource.clip = clip;
                }
                return clip;
            }
            set {
                if(value == null) {
                    Destroy(clip);
                    clip = null;
                }
            }
        }

        [Obsolete("new not allowed. Use StreamedAudioSource.New", true)]
        public StreamedAudioSource() { }

        public static StreamedAudioSource New() {
            var go = new GameObject("StreamedAudioSource");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            var instance = go.AddComponent<StreamedAudioSource>();
            return instance;
        }

        long receivedFrameCount = 0;
        int setDataPos;
        long absSetDataPos;
        int playbackLoops;
        int lastPlaybackPos;
        int absPlaybackPos;
        int samplesLen;

        /// <summary>
        /// Feed audio data for playback
        /// </summary>
        /// <param name="frequency">The sampling frequency of the audio</param>
        /// <param name="channels">The number of channels in the audio</param>
        /// <param name="samples">The PCM samples of the audio</param>
        public void Feed(int frequency, int channels, float[] samples) {
            if (!IsPlaying) return;

            receivedFrameCount++;

            // When we receive the data for the first time, use it 
            // to initialize the fundamental audio parameters
            if(receivedFrameCount == 1) {
                ChannelCount = channels;
                SamplingFrequency = frequency;
                samplesLen = samples.Length;
            }
            // For subsequent data, we check every time if any fundamental
            // audio parameters have changed and restart if they have
            else {
                if (channels != Clip.channels) {
                    ChannelCount = channels;
                    Stop();
                    Play();
                }
                if (SamplingFrequency != frequency) {
                    SamplingFrequency = frequency;
                    Stop();
                    Play();
                }
                if (samples.Length != samplesLen) {
                    samplesLen = samples.Length;
                    Stop();
                    Play();
                }
            }

            // Detect if the audio source has looped and update the absolute playback pos
            if (UnityAudioSource.timeSamples < lastPlaybackPos) {
                lastPlaybackPos = UnityAudioSource.timeSamples;
                playbackLoops++;
            }
            absPlaybackPos = playbackLoops * Clip.samples + UnityAudioSource.timeSamples;

            // Append the new audio data to the clip
            Clip.SetData(samples, setDataPos);

            // If we're just receive the first two audio data, start playing
            if (receivedFrameCount == 2)
                UnityAudioSource.Play();

            // If we just set a block of audio samples where we're currently playing, pause
            // to make sure the playback isn't jittery. We unpause later when more
            // audio data comes in
            if (absPlaybackPos > absSetDataPos) 
                UnityAudioSource.Pause();

            // If the playback position is behind the set position and we're not playing,
            // it means more audio has arrived since we paused. In this case we resume.
            if (absPlaybackPos < absSetDataPos && !UnityAudioSource.isPlaying) 
                UnityAudioSource.UnPause();

            // Undate the set data positions for the next time this method is invoked
            absSetDataPos += samples.Length;
            setDataPos = (int)(absSetDataPos % Clip.samples);
        }

        /// <summary>
        /// Start/resume the playback
        /// </summary>
        public void Play() {
            if (IsPlaying) return;
            IsPlaying = true;

            UnityAudioSource.Play();
        }

        /// <summary>
        /// Stop/pause the playback
        /// </summary>
        public void Stop() {
            if (!IsPlaying) return;
            IsPlaying = false;

            receivedFrameCount = 0;
            setDataPos = 0;
            absSetDataPos = 0;
            playbackLoops = 0;
            lastPlaybackPos = 0;
            absPlaybackPos = 0;
            UnityAudioSource.Stop();
            Clip = null;
        }
    }
}