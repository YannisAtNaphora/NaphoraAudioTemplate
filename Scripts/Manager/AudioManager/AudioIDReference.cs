namespace Naphora.AudioKit.Core
{
    /// <summary>
    /// Drop this on the same prefab as your MasterAudio component.
    /// The Inspector will display every Sound Group ID defined in AudioConfig
    /// so you can copy-paste them directly into MasterAudio Sound Group names.
    /// This component has no runtime cost — it is stripped in builds.
    /// </summary>
    public class AudioIDReference : UnityEngine.MonoBehaviour { }
}
