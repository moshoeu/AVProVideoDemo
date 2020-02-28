/* 
流程： 1.创建2个MediaPlayer，用于双缓冲切换视频
       2.创建Canvas及DisplayUGUI，用以显示视频
       3.需手动设置Canvas对应相机
       4.注意，Canvas对应相机销毁时必须设置新的相机，否则Canvas层级会出错！
 */

 /*
       2019.10.12 新增视频帧事件
  */


using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RenderHeads.Media.AVProVideo;

namespace Soccer
{
    public class UIMediaPlayer : MonoBehaviour
    {

        //public enum VideoPackageMode

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
        private bool isSeekingCur = false;     // 自己判断是否在seek 插件有Bug 短距离seek时可能不抛事件
        private bool isSeekingLoad = false;
        private float seekTargetCur = 0;
        private float seekTargetLoad = 0;

        private readonly float FRAME_RATE = 24f;

        public DisplayUGUI DisplayPnl
        {
            get
            {
                return displayPnl;
            }
        }
        [SerializeField]
        private DisplayUGUI displayPnl;
        public Texture DefaultTexture
        {
            get
            {
                return defaultTexture;
            }
            set
            {
                defaultTexture = value;
                if (displayPnl != null)
                {
                    displayPnl._defaultTexture = defaultTexture;
                    displayPnl._noDefaultDisplay = false;
                }
            }
        }
        [SerializeField]
        private Texture defaultTexture;

        private enum CanvasLayer
        {
            Layer_Scene = LayerTools.Layer_Scene,
            Layer_UI = LayerTools.Layer_UI
        }
        [SerializeField]
        private CanvasLayer canvasLayer = CanvasLayer.Layer_Scene;

        private Canvas displayCanvas;

        public AlphaPacking alphaPacking = AlphaPacking.None;

        public System.Action<int> onVideoFrameChanged;
        public System.Action<MediaPlayer> onVideoSeekFinished;
        public System.Action<MediaPlayer> onVideoFrameReady;
        public System.Action onVideoPlayFinished;

        private System.Action<int> onVideoFrameChangedEvents;       // 监听帧事件
        #endregion

        #region 生命周期
        private void Start()
        {
            InitMediaPlayers();
            InitDisplayPnl();
        }

        private void Update()
        {
            if (CurMediaPlayer.Control != null && CurMediaPlayer.Control.IsPlaying())
            {
                if (onVideoFrameChanged != null)
                {
                    var frameRateMs = FRAME_RATE / 1000;
                    var frame = (int)(CurMediaPlayer.Control.GetCurrentTimeMs() * frameRateMs) + 1;

                    onVideoFrameChanged(frame);
                }

                if (onVideoFrameChangedEvents != null)
                {
                    var frameRateMs = FRAME_RATE / 1000;
                    var frame = (int)(CurMediaPlayer.Control.GetCurrentTimeMs() * frameRateMs) + 1;

                    onVideoFrameChangedEvents(frame);
                }
            }

            if (isSeekingCur)
            {
                if (Mathf.Abs(CurMediaPlayer.Control.GetCurrentTimeMs() - seekTargetCur) < 40f)    // 差距40ms以内（约1帧） 即判断为seek成功
                {
                    OnPlayerEvents(CurMediaPlayer, MediaPlayerEvent.EventType.FinishedSeeking, ErrorCode.None);
                }
            }

            if (isSeekingLoad)
            {
                if (Mathf.Abs(LoadMediaPlayer.Control.GetCurrentTimeMs() - seekTargetLoad) < 40f)
                {
                    OnPlayerEvents(LoadMediaPlayer, MediaPlayerEvent.EventType.FinishedSeeking, ErrorCode.None);
                }
            }

        }

        private void OnDestroy()
        {
            Release();
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
                mediaPlayers[i].m_AlphaPacking = alphaPacking;
                mediaPlayers[i].Events.AddListener(OnPlayerEvents);
            }
            curMediaPlayerIdx = 0;
        }

        private void InitDisplayPnl()
        {
            if (displayPnl == null)
            {
                if (displayCanvas == null)
                {
                    displayCanvas = new GameObject("VideoDisplayCanvas").AddComponent<Canvas>();
                    displayCanvas.renderMode = RenderMode.ScreenSpaceCamera;

                    var scale = displayCanvas.gameObject.AddComponent<CanvasScaler>();
                    scale.matchWidthOrHeight = Framework.UIModule.DefaultMatchRatio(false);
                    scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scale.referenceResolution = Framework.Driver.Instance.Config._uiResolution;

                    LayerTools.SetLayer(displayCanvas.gameObject, (int)canvasLayer);
                }

                displayPnl = new GameObject("VideoDisplayPnl").AddComponent<DisplayUGUI>();
                displayPnl.transform.SetParent(displayCanvas.transform, false);
                LayerTools.SetLayer(displayPnl.gameObject, (int)canvasLayer);

                ResetDisplayPnl();
                displayPnl.raycastTarget = false;
                displayPnl._scaleMode = ScaleMode.ScaleAndCrop;

                displayPnl._noDefaultDisplay = defaultTexture == null;
                if (defaultTexture != null)
                {
                    displayPnl._defaultTexture = defaultTexture;
                    displayPnl.enabled = false;
                }
            }

            displayPnl._mediaPlayer = CurMediaPlayer;
        }

        private void OnPlayerEvents(MediaPlayer mediaPlayer, MediaPlayerEvent.EventType eventType, ErrorCode errorCode)
        {
            if (eventType == MediaPlayerEvent.EventType.FirstFrameReady)        // 视频加载完成
            {
                if (onVideoFrameReady != null)
                {
                    onVideoFrameReady(mediaPlayer);
                    onVideoFrameReady = null;
                }

                // 这里抛视频加载完成事件
            }

            else if (eventType == MediaPlayerEvent.EventType.FinishedSeeking)   // 视频跳帧完成
            {
                isSeekingCur = mediaPlayer == CurMediaPlayer ? false : isSeekingCur;
                isSeekingLoad = mediaPlayer == LoadMediaPlayer ? false : isSeekingLoad;

                if (isLoadWaitSeekFinish)
                {
                    isLoadWaitSeekFinish = false;
                    SwapPlayer();
                }

                if (onVideoSeekFinished != null)
                {
                    onVideoSeekFinished(mediaPlayer);
                    onVideoSeekFinished = null;
                }
            }

            else if (eventType == MediaPlayerEvent.EventType.FinishedPlaying)
            {
                if (onVideoPlayFinished != null)
                {
                    onVideoPlayFinished();
                    onVideoPlayFinished = null;
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

        #region 公有方法
        /// <summary>
        /// 如果在打开视频时需要等待跳帧，然后再切换播放器，将isLoadWaitSeek置为true；
        /// callback的参数为isLoadWaitSeek
        /// </summary>
        /// <param name="path"></param>
        /// <param name="callback"></param>
        /// <param name="isLoadWaitSeek"></param>
        public void OpenVideo(string path, System.Action<MediaPlayer> callback, bool isLoadWaitSeek = false)
        {
            if (CurMediaPlayer.Control != null && CurMediaPlayer.Control.CanPlay())       // 当前播放器已加载视频
            {
                if (false == LoadMediaPlayer.OpenVideoFromFile(MediaPlayer.FileLocation.RelativeToPeristentDataFolder, "Download/" + path, false))
                {
                    LoadMediaPlayer.OpenVideoFromFile(MediaPlayer.FileLocation.RelativeToStreamingAssetsFolder, path, false);
                }

                isLoadWaitSeekFinish = isLoadWaitSeek;

                onVideoFrameReady = (player) =>
                {
                    if (isLoadWaitSeek == false)
                        SwapPlayer();           // 如果要等待跳帧 延迟切换播放器

                    if (callback != null)
                        callback.Invoke(player);
                };
            }

            else
            {
                if (false == CurMediaPlayer.OpenVideoFromFile(MediaPlayer.FileLocation.RelativeToPeristentDataFolder, "Download/" + path, false))
                {
                    CurMediaPlayer.OpenVideoFromFile(MediaPlayer.FileLocation.RelativeToStreamingAssetsFolder, path, false);
                }

                isLoadWaitSeekFinish = false;

                onVideoFrameReady = (player) =>
                {
                    if (callback != null)
                        callback.Invoke(player);
                };
            }
        }

        public void Seek(int frame, MediaPlayer player = null)
        {
            player = player ?? CurMediaPlayer;
            var frameRate = FRAME_RATE;
            var timePerFrameMs = 1 / frameRate * 1000;
            var targetTimeMs = (frame - 1) * timePerFrameMs;
            var videoLengthMs = player.Info.GetDurationMs();

            if (targetTimeMs < 0 || targetTimeMs > videoLengthMs) return;    // 超出视频范围

            player.Control.Seek(targetTimeMs);

            if (player == CurMediaPlayer)
            {
                isSeekingCur = true;
                seekTargetCur = targetTimeMs;
            }

            if (player == LoadMediaPlayer)
            {
                isSeekingLoad = true;
                seekTargetLoad = targetTimeMs;
            }

        }

        public void Pause()
        {
            CurMediaPlayer.Control.Pause();
        }

        public void Rewind()
        {
            CurMediaPlayer.Control.Rewind();
            CurMediaPlayer.Control.Play();
        }

        public void Play(bool isLoop = false)
        {
            CurMediaPlayer.Control.SetLooping(isLoop);
            CurMediaPlayer.Control.Play();
        }

        /// <summary>
        /// 播放用开始帧和结束帧描述的一段视频，若isLoop为true，会循环播放该段视频
        /// </summary>
        /// <param name="startFrame"></param>
        /// <param name="endFrame"></param>
        /// <param name="isLoop"></param>
        /// <param name="callback"></param>
        /// <param name="isUseLoadPlayer"></param>
        public void PlayOneStage(int startFrame, int endFrame, bool isLoop, MediaPlayer mediaPlayer = null, System.Action callback = null)
        {
            var isLockFrame = startFrame == endFrame;
            mediaPlayer = mediaPlayer ?? CurMediaPlayer;
            onVideoFrameChanged = null;

            System.Action<int> action = (frame) =>
            {
                int frameSub = frame - endFrame;
                if (frameSub >= 0)
                {
                    if (callback != null)
                    {
                        callback.Invoke();
                    }

                    if (isLoop)
                    {
                        PlayOneStage(startFrame, endFrame, true, mediaPlayer, callback);
                    }
                }
            };

            if (startFrame == 1)
            {
                mediaPlayer.Control.Rewind();
                mediaPlayer.Control.Play();

                onVideoFrameChanged = action;
                onVideoSeekFinished = null;
            }
            else
            {
                onVideoSeekFinished = (player) =>
                {
                    if (isLockFrame)
                    {
                        player.Control.Pause();
                    }

                    onVideoFrameChanged = action;
                    //Debug.LogFormat("Seek Finish:{0}, {1}, {2}", player.Control.GetCurrentTimeMs(), startFrame, endFrame);
                };

                //Debug.LogFormat("Seek Target:{0}", endFrame);
                Seek(startFrame, mediaPlayer);
                mediaPlayer.Control.Play();
            }

        }

        public void SetDisplayPnl(DisplayUGUI displayPnl)
        {
            this.displayPnl = displayPnl;
            InitDisplayPnl();
        }

        public void SetDisplayPnlActive(bool enable)
        {
            if (displayPnl != null)
                displayPnl.enabled = enable;
        }

        public void ResetDisplayPnl()
        {
            if (displayPnl != null)
            {
                displayPnl.rectTransform.anchorMin = Vector2.zero;
                displayPnl.rectTransform.anchorMax = Vector2.one;
                displayPnl.rectTransform.anchoredPosition = Vector2.zero;
                displayPnl.rectTransform.offsetMin = Vector2.zero;
                displayPnl.rectTransform.offsetMax = Vector2.zero;
                displayPnl.rectTransform.localScale = Vector3.one;
            }
        }

        public void SetCamera(Camera camera)
        {
            if (displayCanvas != null)
                displayCanvas.worldCamera = camera;
        }

        /// <summary>
        /// 释放视频资源 销毁Canvas
        /// </summary>
        public void Release()
        {
            CloseVideo(true);

            if (displayCanvas != null)
                Destroy(displayCanvas.gameObject);
        }

        /// <summary>
        /// 释放视频资源
        /// </summary>
        public void CloseVideo(bool isRemoveListener = false)
        {
            isSeekingCur = false;
            isSeekingLoad = false;
            curMediaPlayerIdx = 0;
            isLoadWaitSeekFinish = false;
            if (mediaPlayers != null)
            {
                for (int i = 0; i < mediaPlayers.Length; i++)
                {
                    mediaPlayers[i].CloseVideo();
                    if (isRemoveListener)
                        mediaPlayers[i].Events.RemoveAllListeners();
                }
                displayPnl._mediaPlayer = CurMediaPlayer;
            }
        }

        /// <summary>
        /// 注册事件监听
        /// </summary>
        /// <param name="cb"></param>
        public void AddFrameEventListener(System.Action<int> cb)
        {
            onVideoFrameChangedEvents += cb;
        }

        /// <summary>
        /// 取消事件监听
        /// </summary>
        /// <param name="cb"></param>
        public void RemoveFrameEventListener(System.Action<int> cb, bool isRemoveAll = false)
        {
            if (isRemoveAll)
            {
                onVideoFrameChangedEvents = null;
            }
            else
            {
                onVideoFrameChangedEvents -= cb;
            }
        }

        /// <summary>
        /// 设置视频Canvas渲染模式
        /// </summary>
        /// <param name="mode"></param>
        public void SetDisplayCanvasRenderMode(RenderMode mode)
        {
            if (displayCanvas != null)
            {
                displayCanvas.renderMode = mode;
            }
        }

        #endregion
    }


}

