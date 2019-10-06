using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RenderHeads.Media.AVProVideo;

public class UIMediaPlayer : MonoBehaviour {

    #region 属性
    private MediaPlayer[] mediaPlayers;
    public MediaPlayer CurMediaPlayer
    {
        get
        {
            return (mediaPlayers != null && mediaPlayers.Length > curMediaPlayerIdx) ? mediaPlayers[curMediaPlayerIdx] : null;
        }
    }
    public MediaPlayer LoadMediaPlayer
    {
        get
        {
            return (mediaPlayers != null && mediaPlayers.Length > curMediaPlayerIdx) ? mediaPlayers[1 - curMediaPlayerIdx] : null;
        }
    }

    private int curMediaPlayerIdx;
    private bool isLoadWaitSeekFinish = false;     // 加载视频时是否等待跳帧完成

    private readonly float FRAME_RATE = 24f;

    [SerializeField]
    private DisplayUGUI displayPnl;

    public System.Action<int> onVideoFrameChanged;
    public System.Action<MediaPlayer> onVideoSeekFinished;
    public System.Action<MediaPlayer> onVideoFrameReady;
    #endregion

    #region 生命周期
    private void Start()
    {
        InitMediaPlayers();
        InitDisplayPnl();
    }

    private void Update()
    {
        if (CurMediaPlayer.Control.IsPlaying())
        {
            if (onVideoFrameChanged != null)
            {
                var frameRateMs = FRAME_RATE / 1000;
                var frame = (int)(CurMediaPlayer.Control.GetCurrentTimeMs() * frameRateMs) + 1;

                onVideoFrameChanged(frame);
            }
        }

    }

    private void OnDestroy()
    {
        for (int i = 0; i < mediaPlayers.Length; i++)
        {
            mediaPlayers[i].CloseVideo();
            mediaPlayers[i].Events.RemoveAllListeners();
        }
    }
    #endregion

    private void InitMediaPlayers()
    {
        mediaPlayers = new MediaPlayer[2];
        for (int i = 0; i < mediaPlayers.Length; i++)
        {
            mediaPlayers[i] = gameObject.AddComponent<MediaPlayer>();
            mediaPlayers[i].m_AutoStart = false;
            mediaPlayers[i].m_AutoOpen = false;
            mediaPlayers[i].Events.AddListener(RegisterPlayerEvents);
            //mediaPlayers[i].
        }
        curMediaPlayerIdx = 0;
    }

    private void InitDisplayPnl()
    {
        if (displayPnl != null)
        {
            displayPnl.rectTransform.anchorMin = Vector2.zero;
            displayPnl.rectTransform.anchorMax = Vector2.one;
            displayPnl.rectTransform.anchoredPosition = Vector2.zero;
            displayPnl._scaleMode = ScaleMode.ScaleAndCrop;
            displayPnl._mediaPlayer = CurMediaPlayer;
        }
    }

    private void RegisterPlayerEvents(MediaPlayer mediaPlayer, MediaPlayerEvent.EventType eventType, ErrorCode errorCode)
    {
        if (eventType == MediaPlayerEvent.EventType.FirstFrameReady)        // 视频加载完成
        {
            if (onVideoFrameReady != null)
            {
                onVideoFrameReady(mediaPlayer);
            }
        }

        else if (eventType == MediaPlayerEvent.EventType.FinishedSeeking)   // 视频跳帧完成
        {
            if (isLoadWaitSeekFinish)
            {
                isLoadWaitSeekFinish = false;
                SwapPlayer();     
            }

            if (onVideoSeekFinished != null)
            {
                onVideoSeekFinished(mediaPlayer);
            }
        }
    }

    private void SwapPlayer()
    {
        //if (playIdx == curMediaPlayerIdx) return;
        var playIdx = 1 - curMediaPlayerIdx;
        CurMediaPlayer.Control.CloseVideo();
        curMediaPlayerIdx = playIdx;
        displayPnl._mediaPlayer = CurMediaPlayer;
    }

    /// <summary>
    /// 如果在打开视频时需要等待跳帧，然后再切换播放器，将isLoadWaitSeek置为true；
    /// callback的参数为isLoadWaitSeek
    /// </summary>
    /// <param name="path"></param>
    /// <param name="callback"></param>
    /// <param name="isLoadWaitSeek"></param>
    public void OpenVideo(string path, System.Action callback, bool isLoadWaitSeek = false)
    {
        if (CurMediaPlayer.Control.CanPlay())       // 当前播放器已加载视频
        {
            var loadMediaPlayerIdx = 1 - curMediaPlayerIdx;
            var loadMediaPlayer = mediaPlayers[loadMediaPlayerIdx];  // 取另一个播放器
            loadMediaPlayer.OpenVideoFromFile(MediaPlayer.FileLocation.RelativeToStreamingAssetsFolder, path, false);
            isLoadWaitSeekFinish = isLoadWaitSeek;

            onVideoFrameReady = (player) =>
            {
                if (isLoadWaitSeek == false) 
                    SwapPlayer();           // 如果要等待跳帧 延迟切换播放器

                if (callback != null)
                    callback.Invoke();
            };
        }

        else
        {
            CurMediaPlayer.OpenVideoFromFile(MediaPlayer.FileLocation.RelativeToStreamingAssetsFolder, path, false);
            isLoadWaitSeekFinish = false;

            onVideoFrameReady = (player) =>
            {
                if (callback != null)
                    callback.Invoke();
            };
        }
    }

    public void Seek(int frame)
    {
        var frameRate = FRAME_RATE;
        var timePerFrameMs = 1 / frameRate * 1000;
        var targetTimeMs = (frame - 1) * timePerFrameMs;
        if (targetTimeMs < 0 || targetTimeMs > CurMediaPlayer.Info.GetDurationMs()) return;    // 超出视频范围

        if (isLoadWaitSeekFinish)
            LoadMediaPlayer.Control.SeekFast(targetTimeMs);
        else
            CurMediaPlayer.Control.SeekFast(targetTimeMs);
    }

    /// <summary>
    /// 如果isUseLoadPlayer为true，会使用加载播放器进行跳帧；使用场景为加载一个新视频时需要等待跳帧结束后再切换播放器
    /// </summary>
    /// <param name="startFrame"></param>
    /// <param name="endFrame"></param>
    /// <param name="isLoop"></param>
    /// <param name="callback"></param>
    /// <param name="isUseLoadPlayer"></param>
    public void PlayOneStage(int startFrame, int endFrame, bool isLoop, System.Action callback = null)
    {
        var isLockFrame = startFrame == endFrame;

        onVideoFrameChanged = (frame) =>
        {
            if (frame >= endFrame)  // 若仅等于 帧率低的时候可能会被跳过
            {
                if (callback != null)
                {
                    callback.Invoke();
                }

                if (isLoop)
                {
                    PlayOneStage(startFrame, endFrame, true, callback);
                }
            }
        };

        onVideoSeekFinished = (player) =>
        {
            if (isLockFrame)
            {
                player.Control.Pause();
            }
            else
            {
                player.Control.Play();
            }
        };

        Seek(startFrame);

    }

    public void SetDisplayPnl(DisplayUGUI displayPnl)
    {
        this.displayPnl = displayPnl;
        InitDisplayPnl();
    }
}
