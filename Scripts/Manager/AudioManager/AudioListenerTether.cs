using System.Reflection;
using DarkTonic.MasterAudio;
using Naphora.AudioKit.Core.Config;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owns the game's single AudioListener and keeps it tethered between Camera.main and the player
// (see ListenerTetherSettings). Created by AudioManagerBehaviour when the tether is enabled, so a
// project gets it by ticking a box instead of re-coding it per game. Any other AudioListener
// (normally the one on the camera) is removed so Unity never sees two.
// Runs after CinemachineBrain's LateUpdate so it follows this frame's camera, not last frame's.
[DefaultExecutionOrder(10000)]
[AddComponentMenu("")]
public class AudioListenerTether : MonoBehaviour
{
    private const float TargetSearchInterval = 0.5f;

    private ListenerTetherSettings _settings;
    private AudioListener _listener;
    private Camera _camera;
    private Transform _taggedTarget;
    private float _nextTargetSearch;
    private Vector3 _velocity;
    private bool _snap = true;
    private bool _warnedBadTag;

    public void Init(ListenerTetherSettings settings)
    {
        _settings = settings;
        _listener = gameObject.AddComponent<AudioListener>();
        SceneManager.sceneLoaded += OnSceneLoaded;
        RemoveOtherListeners();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _camera = null;
        _taggedTarget = null;
        _nextTargetSearch = 0f;
        _snap = true;
        RemoveOtherListeners();
    }

    private void LateUpdate()
    {
        if (_camera == null || !_camera.isActiveAndEnabled)
        {
            _camera = Camera.main;
            if (_camera == null)
                return;

            RemoveOtherListeners();
            _snap = true;
        }

        Transform cam = _camera.transform;
        Transform target = AudioManager.ListenerTargetOverride != null ? AudioManager.ListenerTargetOverride : FindTaggedTarget();

        Vector3 desired = target != null
            ? Vector3.Lerp(cam.position, target.position + Vector3.up * _settings.heightOffset, _settings.towardTarget)
            : cam.position;

        if (_snap || _settings.damping <= 0f || (desired - transform.position).sqrMagnitude > _settings.snapDistance * _settings.snapDistance)
        {
            transform.position = desired;
            _velocity = Vector3.zero;
            _snap = false;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _velocity, _settings.damping, Mathf.Infinity, Time.unscaledDeltaTime);
        }

        // Camera's rotation, not the player's: screen-left stays left in the ears whichever way the player faces.
        transform.rotation = cam.rotation;
    }

    private Transform FindTaggedTarget()
    {
        if (_taggedTarget != null || string.IsNullOrEmpty(_settings.targetTag) || Time.unscaledTime < _nextTargetSearch)
            return _taggedTarget;

        _nextTargetSearch = Time.unscaledTime + TargetSearchInterval;
        try
        {
            GameObject found = GameObject.FindWithTag(_settings.targetTag);
            _taggedTarget = found != null ? found.transform : null;
            if (_taggedTarget != null)
                _snap = true;
        }
        catch (UnityException)
        {
            // Tag not defined in this project.
            if (!_warnedBadTag)
                Debug.LogWarning($"[AudioListenerTether] Tag \"{_settings.targetTag}\" doesn't exist -- set Target Tag on AudioManagerBehaviour or call AudioManager.SetListenerTarget().");
            _warnedBadTag = true;
        }

        return _taggedTarget;
    }

    private void RemoveOtherListeners()
    {
        foreach (var other in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (other != _listener)
                Destroy(other);
        }

        PointMasterAudioAtThisListener();
    }

    // Master Audio caches the first active listener it finds and only re-searches if that
    // GameObject deactivates -- the camera stays active, so it would keep measuring distances
    // (voice priority, distance ducking, "audible range" groups) from the camera. It has no
    // setter, hence the reflection.
    private static FieldInfo _maListenerField;
    private static bool _maListenerFieldMissing;

    private void PointMasterAudioAtThisListener()
    {
        if (_maListenerFieldMissing)
            return;

        if (_maListenerField == null)
            _maListenerField = typeof(MasterAudio).GetField("_listenerTrans", BindingFlags.NonPublic | BindingFlags.Static);

        if (_maListenerField == null)
        {
            _maListenerFieldMissing = true;
            Debug.LogWarning("[AudioListenerTether] Master Audio's _listenerTrans field not found (Master Audio updated?) -- " +
                             "its distance-based features may keep using the old listener position.");
            return;
        }

        _maListenerField.SetValue(null, transform);
    }
}
