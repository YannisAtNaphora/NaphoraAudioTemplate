using System.Collections;
using UnityEngine;
using DarkTonic.MasterAudio;

namespace Naphora.AudioKit.Core
{
    [System.Serializable]
    public struct LevelMusicEntry
    {
        public int    levelNumber;
        public string playlistName;
    }

    /// <summary>
    /// Crossfading music player over two MasterAudio PlaylistControllers
    /// (menu and game). Drive it via AudioManager.PlayMenuMusic() /
    /// PlayGameMusic() / PlayLevelMusic() / PlayEndGameMusic() rather than
    /// calling this class directly.
    /// </summary>
    public class NaphoraMusicManager : MonoBehaviour
    {
        [SerializeField] private GameObject plControllerGamePrefab;
        [SerializeField] private GameObject plControllerMainMenuPrefab;
        [SerializeField] private float fadeDuration = 2f;

        [Header("Level Music Table")]
        [Tooltip("Map each level number to a MasterAudio playlist name on the Game controller.")]
        [SerializeField] private LevelMusicEntry[] levelMusicTable;

        private const string GamePlaylistName   = "GameLevel";
        private const string MenuPlaylistName   = "Menu";

        //To use it, just add an "EndGame" playlist to the PLController_Game MasterAudio Playlist asset.
        private const string EndGamePlaylistName = "EndGame";

        public static NaphoraMusicManager Instance { get; private set; }

        private string _currentMusicKey;

        private PlaylistController _gameController;
        private PlaylistController _menuController;

        private Coroutine _activeSwitch;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private IEnumerator Start()
        {
            // See AudioConfig.UseMasterAudioMusicSystem -- both PlaylistControllers
            // stay uninstantiated (and MenuPlaylistName never auto-plays) until this
            // project actually wants Master Audio's playlist system engaged.
            if (!AudioConfig.UseMasterAudioMusicSystem)
                yield break;

            if (plControllerGamePrefab != null)
            {
                var go = Instantiate(plControllerGamePrefab, transform);
                go.name = plControllerGamePrefab.name;
                _gameController = go.GetComponent<PlaylistController>();
            }
            if (plControllerMainMenuPrefab != null)
            {
                var go = Instantiate(plControllerMainMenuPrefab, transform);
                go.name = plControllerMainMenuPrefab.name;
                _menuController = go.GetComponent<PlaylistController>();
            }

            // Wait one frame for both controllers to run their own Start/Awake
            yield return null;

            // Menu plays naturally via startPlaylistOnAwake; silence game controller if present
            _gameController?.StopPlaylist();
            _currentMusicKey = "menu";
        }

        /// <summary>
        /// Hard-stops both controllers immediately, no fade, no crossfade coroutine.
        /// For a screen where music must not be audible at all -- e.g. under the
        /// first-launch cutscene -- rather than switching to a *different* track.
        /// </summary>
        public void StopAllImmediate()
        {
            if (_activeSwitch != null)
            {
                StopCoroutine(_activeSwitch);
                _activeSwitch = null;
            }
            _gameController?.StopPlaylist();
            _menuController?.StopPlaylist();
            _currentMusicKey = null;
        }

        public void PlayGameMusic()
        {
            if (_currentMusicKey == "game") return;
            _currentMusicKey = "game";
            if (_activeSwitch != null) StopCoroutine(_activeSwitch);
            _activeSwitch = StartCoroutine(SwitchTo(_menuController, _gameController, GamePlaylistName));
        }

        public void PlayLevelMusic(int levelNumber)
        {
            string key = $"level_{levelNumber}";
            if (_currentMusicKey == key) return;

            string playlist = null;
            if (levelMusicTable != null)
            {
                foreach (var entry in levelMusicTable)
                {
                    if (entry.levelNumber == levelNumber)
                    {
                        playlist = entry.playlistName;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(playlist))
            {
                Debug.LogWarning($"[NaphoraMusicManager] No playlist configured for level {levelNumber} — falling back to default game music.");
                PlayGameMusic();
                return;
            }

            _currentMusicKey = key;
            if (_activeSwitch != null) StopCoroutine(_activeSwitch);
            _activeSwitch = StartCoroutine(SwitchTo(_menuController, _gameController, playlist));
        }

        public void PlayMenuMusic()
        {
            if (_currentMusicKey == "menu") return;
            _currentMusicKey = "menu";
            if (_activeSwitch != null) StopCoroutine(_activeSwitch);
            _activeSwitch = StartCoroutine(SwitchTo(_gameController, _menuController, MenuPlaylistName));
        }

        /// <summary>
        /// Crossfades to the "EndGame" playlist on the Game controller.
        /// Add an "EndGame" playlist to the PLController_Game MasterAudio asset to use this.
        /// </summary>
        public void PlayEndGameMusic()
        {
            if (_currentMusicKey == "endgame") return;
            _currentMusicKey = "endgame";
            if (_activeSwitch != null) StopCoroutine(_activeSwitch);
            _activeSwitch = StartCoroutine(SwitchTo(_gameController, _gameController, EndGamePlaylistName));
        }

        private IEnumerator SwitchTo(PlaylistController outController, PlaylistController inController, string incomingPlaylist)
        {
            if (outController != null)
            {
                outController.FadeToVolume(0f, fadeDuration);
                yield return new WaitForSeconds(fadeDuration);
                outController.StopPlaylist();
                outController.FadeToVolume(1f, 0f);
            }
            else
            {
                yield return new WaitForSeconds(fadeDuration);
            }

            if (inController != null)
            {
                inController.FadeToVolume(0f, 0f);
                inController.StartPlaylist(incomingPlaylist);
                inController.FadeToVolume(1f, fadeDuration);
            }

            _activeSwitch = null;
        }
    }
}
