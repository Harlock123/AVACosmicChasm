using AVACosmicChasm.Audio;

namespace AVACosmicChasm.Engine;

/// <summary>
/// Plays the game's sounds through OpenAL Soft, which mixes as many at once as the game needs. The tunes play on the
/// music channel and the thrust's roar loops on the ambience channel while it's held. In a virtual machine a larger
/// mixing buffer keeps the sound from breaking up.
/// </summary>
public sealed class SoundManager : IDisposable
{
    private readonly IAudioEngine _engine;
    private readonly string _folder;

    public SoundManager(bool silent = false)
    {
        _folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        _engine = silent
            ? new NullAudioEngine("silent")
            : OpenAlAudioEngine.CreateOrSilent(AudioBuffer.PeriodFrames(AudioBuffer.Automatic, VirtualMachine.Detect()));
        _engine.SetVolumes(0.9f, 0.8f, 0.7f);
        _engine.SetAmbienceVolume(0.45f);
    }

    public bool IsAvailable => _engine.IsAvailable;

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (value) StopAll();
        }
    }
    private bool _muted;

    /// <summary>The file a sound plays from.</summary>
    public string File(Sfx s) => Path.Combine(_folder, (s == Sfx.ThrustOn ? "Thrust" : s.ToString()) + ".wav");

    /// <summary>The sounds that are instructions to the player rather than files.</summary>
    public static bool HasFile(Sfx s) => s is not (Sfx.ThrustOff or Sfx.MusicStop);

    public void Play(Sfx sfx)
    {
        if (sfx == Sfx.ThrustOff) { _engine.StopAmbience(); return; }
        if (_muted) return;
        switch (sfx)
        {
            case Sfx.TitleTune: _engine.PlayMusic(File(sfx), loop: true); return;
            case Sfx.StationTune or Sfx.EscapedTune or Sfx.GameOver: _engine.PlayMusic(File(sfx), loop: false); return;
            case Sfx.MusicStop: _engine.StopMusic(); return;
            case Sfx.ThrustOn: _engine.PlayAmbience(File(sfx)); return;
        }
        _engine.PlayEffect(File(sfx), sfx switch { Sfx.Fire => 0.45f, Sfx.Drill => 0.5f, Sfx.Emerge => 0.5f, Sfx.EnemyFire => 0.55f, _ => 1f });
    }

    public void StopAll()
    {
        _engine.StopAmbience();
        _engine.StopMusic();
    }

    public void Dispose() => _engine.Dispose();
}
