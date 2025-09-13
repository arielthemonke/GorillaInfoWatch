using GorillaInfoWatch.Extensions;
using GorillaInfoWatch.Models.Enumerations;
using GorillaInfoWatch.Tools;
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using UnityEngine.Networking;

namespace GorillaInfoWatch.Behaviours
{
    public class MediaManager : MonoBehaviour
    {
        public static MediaManager Instance { get; private set; }

        public Dictionary<string, Session> Sessions { get; private set; } = [];
        public string FocussedSession { get; private set; } = null;

        public event Action<Session> OnSessionFocussed, OnPlaybackStateChanged, OnMediaChanged, OnTimelineChanged;

        private readonly Dictionary<string, Texture2D> thumbnailCache = [];
        private PlayerctlExtensions playerctl;
        private bool isMonitoring = false;
        private Session currentSession;
        
        private float statusPollInterval = 1f;
        private float metadataPollInterval = 2f;

        public void Awake()
        {
            Instance = this;

            Main.OnInitialized += HandleModInitialized;
            Application.wantsToQuit += HandleGameQuit;
        }

        private void HandleModInitialized()
        {
            playerctl = new PlayerctlExtensions();
            
            currentSession = new Session()
            {
                Id = "playerctl-default"
            };
            
            Sessions.Add(currentSession.Id, currentSession);
            FocussedSession = currentSession.Id;
            OnSessionFocussed?.SafeInvoke(currentSession);
            
            isMonitoring = true;
            StartCoroutine(MonitorPlayback());
        }

        public bool HandleGameQuit()
        {
            isMonitoring = false;
            return true;
        }

        private IEnumerator MonitorPlayback()
        {
            string lastStatus = "";
            string lastMetadata = "";
            
            while (isMonitoring)
            {
                yield return StartCoroutine(CheckStatusCoroutine());
                yield return StartCoroutine(CheckMetadataCoroutine());
                yield return StartCoroutine(CheckTimelineCoroutine());
                yield return new WaitForSeconds(statusPollInterval);
            }
        }
        
        private IEnumerator CheckTimelineCoroutine()
        {
            var positionTask = playerctl.GetPosition();
            yield return new WaitUntil(() => positionTask.IsCompleted);
            var durationTask = playerctl.GetDuration();
            yield return new WaitUntil(() => durationTask.IsCompleted);

            if (positionTask.IsCompletedSuccessfully && durationTask.IsCompletedSuccessfully)
            {
                string positionStr = positionTask.Result;
                string durationStr = durationTask.Result;

                bool hasTimelineChanged = false;
                if (!string.IsNullOrEmpty(durationStr) && double.TryParse(durationStr, out double durationSeconds))
                {
                    if (Math.Abs(currentSession.EndTime - durationSeconds) > 0.1)
                    {
                        currentSession.EndTime = durationSeconds;
                        hasTimelineChanged = true;
                    }
                }
                if (!string.IsNullOrEmpty(positionStr) && double.TryParse(positionStr, out double positionSeconds))
                {
                    if (Math.Abs(currentSession.Position - positionSeconds) > 0.5)
                    {
                        currentSession.Position = positionSeconds;
                        hasTimelineChanged = true;
                    }
                }

                if (hasTimelineChanged)
                {
                    OnTimelineChanged?.SafeInvoke(currentSession);
                }
            }
        }

        private IEnumerator CheckStatusCoroutine()
        {
            var statusTask = playerctl.GetStatus();
            yield return new WaitUntil(() => statusTask.IsCompleted);
            
            if (statusTask.IsCompletedSuccessfully)
            {
                string currentStatus = statusTask.Result;
                if (!string.IsNullOrEmpty(currentStatus) && currentStatus != currentSession.PlaybackStatus)
                {
                    currentSession.PlaybackStatus = currentStatus;
                    OnPlaybackStateChanged?.SafeInvoke(currentSession);
                }
            }
        }

        private IEnumerator CheckMetadataCoroutine()
        {
            string newTitle = currentSession.Title;
            string newArtist = currentSession.Artist;
            var newArtistTask = playerctl.GetArtist();
            yield return new WaitUntil(() => newArtistTask.IsCompleted);
            if (newArtistTask.IsCompletedSuccessfully)
            {
                newArtist = newArtistTask.Result;
            }

            var newTitleTask = playerctl.GetTitle();
            yield return new WaitUntil(() => newTitleTask.IsCompleted);
            if (newTitleTask.IsCompletedSuccessfully)
            {
                newTitle = newTitleTask.Result;
            }
            
            bool hasChanged = false;
            
            if (currentSession.Title != newTitle)
            {
                currentSession.Title = newTitle;
                hasChanged = true;
            }
            
            if (currentSession.Artist != newArtist)
            {
                currentSession.Artist = newArtist;
                hasChanged = true;
            }
            
            if (hasChanged)
            {
                StartCoroutine(UpdateCoverArt());
                OnMediaChanged?.SafeInvoke(currentSession);
            }
        }

        private IEnumerator UpdateCoverArt()
        {
            var coverTask = playerctl.GetCover();
            yield return new WaitUntil(() => coverTask.IsCompleted);
            
            if (coverTask.IsCompletedSuccessfully)
            {
                string coverUrl = coverTask.Result;
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    yield return StartCoroutine(LoadCoverFromUrl(coverUrl));
                }
            }
        }

        private IEnumerator LoadCoverFromUrl(string url)
        {
            if (thumbnailCache.TryGetValue(url, out Texture2D cachedTexture))
            {
                currentSession.Thumbnail = cachedTexture;
                yield break;
            }

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                yield return www.SendWebRequest();
                
                if (www.downloadHandler.data != null)
                {
                    Texture2D texture = new Texture2D(2, 2);
                    if (texture.LoadImage(www.downloadHandler.data))
                    {
                        thumbnailCache[url] = texture;
                        currentSession.Thumbnail = texture;
                        OnMediaChanged?.SafeInvoke(currentSession);
                    }
                    else
                    {
                        DestroyImmediate(texture);
                    }
                }
            }
        }

        public void PlayPause()
        {
            StartCoroutine(ExecuteCommand("play-pause"));
        }

        public void Next()
        {
            StartCoroutine(ExecuteCommand("next"));
        }

        public void Previous()
        {
            StartCoroutine(ExecuteCommand("previous"));
        }

        private IEnumerator ExecuteCommand(string command)
        {
            var commandTask = playerctl.SendCommand(command);
            yield return new WaitUntil(() => commandTask.IsCompleted);
        }

        public void PushKey(MediaKeyCode keyCode)
        {
            switch (keyCode)
            {
                case MediaKeyCode.PlayPause:
                    StartCoroutine(ExecuteCommand("play-pause"));
                    break;
                case MediaKeyCode.NextTrack:
                    StartCoroutine(ExecuteCommand("next"));
                    break;
                case MediaKeyCode.PreviousTrack:
                    StartCoroutine(ExecuteCommand("previous"));
                    break;
                default:
                    Logging.Warning($"Unsupported media key: {keyCode}");
                    break;
            }
        }

        public class Session
        {
            public string Id;
            public string Title;
            public string Artist;
            public string[] Genres;
            public int TrackNumber;
            public string AlbumTitle;
            public string AlbumArtist;
            public int AlbumTrackCount;
            public double StartTime;
            public double EndTime;
            public double Position;
            public string PlaybackStatus;
            public Texture2D Thumbnail;
        }
    }
}