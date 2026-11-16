using System;
using UnityEditor;
using UnityEngine;
#if HAVE_UNITY_RECORDER
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
#endif

namespace KhrCharacterSamples.Editor
{
    /// <summary>
    /// Video footage through Unity Recorder: H.264 MP4, 1920×1080, 30 fps, one file per shot (see
    /// <see cref="VideoDriver"/>). Needs an interactive editor with graphics (the Recorder reads the Game view), so run
    /// it without -batchmode and without -nographics:
    /// <c>Unity -projectPath P -executeMethod KhrCharacterSamples.Editor.CharacterVideo.Run</c>, or menu
    /// Samples → Characters → Record Video. The editor quits when the list is done (exit 1 on any failure) when started
    /// with -executeMethod. Env: KHR_EVIDENCE_DIR, KHR_VIDEO_DIR, KHR_CLIPS_DIR, KHR_CHARACTERS, KHR_VIDEO_MAX_SECONDS,
    /// KHR_UNITYGLTF_SHA (written into each shots.json).
    /// </summary>
    public static class CharacterVideo
    {
        private const string Job = "video";
        public const int Width = 1920, Height = 1080;
        public const float FrameRate = 30f;

#if HAVE_UNITY_RECORDER
        private static RecorderController _controller;
#endif

        public static void Run()
        {
            if (Application.isBatchMode)
                throw new InvalidOperationException("CharacterVideo needs an interactive editor (Game view + Recorder); run without -batchmode.");
            PlayModeJob.Start(Job);
        }

        [MenuItem("Samples/Characters/Record Video")]
        private static void Menu() => PlayModeJob.Start(Job, exitWhenDone: false);

        [InitializeOnLoadMethod]
        private static void Register() => PlayModeJob.Register(Job,
            onPlay: () =>
            {
                VideoDriver.ShotBegin -= Begin;
                VideoDriver.ShotEnd -= End;
                VideoDriver.ShotBegin += Begin;
                VideoDriver.ShotEnd += End;
                VideoDriver.Launch();
            },
            isDone: () => VideoDriver.Done,
            failures: () => VideoDriver.Failures + Missing());

        private static int Missing()
        {
#if HAVE_UNITY_RECORDER
            return 0;
#else
            Debug.LogError("[KhrVideo] com.unity.recorder is not installed; no video was written.");
            return 1;
#endif
        }

        private static void Begin(string outputWithoutExtension)
        {
#if HAVE_UNITY_RECORDER
            End();
            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "KhrCharacterShot";
            movie.Enabled = true;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };
            movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = Width, OutputHeight = Height };
            movie.CaptureAudio = false;
            movie.OutputFile = outputWithoutExtension;
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRatePlayback = FrameRatePlayback.Constant;
            settings.FrameRate = FrameRate;
            settings.CapFrameRate = true;
            _controller = new RecorderController(settings);
            _controller.PrepareRecording();
            if (!_controller.StartRecording()) Debug.LogError("[KhrVideo] Recorder did not start for " + outputWithoutExtension);
#endif
        }

        private static void End()
        {
#if HAVE_UNITY_RECORDER
            if (_controller != null && _controller.IsRecording()) _controller.StopRecording();
            _controller = null;
#endif
        }
    }
}
