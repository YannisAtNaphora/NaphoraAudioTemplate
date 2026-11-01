using UnityEngine;

/// <summary>
/// Inspector-driven audio trigger bound to this GameObject's Unity lifecycle
/// events. Use for one-shot stingers or ambient loops that should start/stop
/// alongside a GameObject rather than being called from gameplay code.
/// </summary>
public class AudioEventTrigger : MonoBehaviour
{
    [Header("Sound ID")]
    [SerializeField] private string soundID;

    [Header("One Shot")]
    [SerializeField] private bool playOnAwake   = false;
    [SerializeField] private bool playOnStart   = false;
    [SerializeField] private bool playOnDisable = false;
    [SerializeField] private bool playOnDestroy = false;

    [Header("Loop")]
    [SerializeField] private bool loopOnStart   = false;
    [SerializeField] private bool stopOnDisable = false;

    private int _loopHandle = -1;

    private void Awake()
    {
        if (playOnAwake) AudioManager.Trigger(soundID);
    }

    private void Start()
    {
        if (playOnStart) AudioManager.Trigger(soundID);
        if (loopOnStart) _loopHandle = AudioManager.PlayLoop(soundID, transform);
    }

    private void OnDisable()
    {
        if (playOnDisable) AudioManager.Trigger(soundID);
        if (stopOnDisable && _loopHandle >= 0)
        {
            AudioManager.StopInstance(_loopHandle);
            _loopHandle = -1;
        }
    }

    private void OnDestroy()
    {
        if (playOnDestroy) AudioManager.Trigger(soundID);
    }
}
