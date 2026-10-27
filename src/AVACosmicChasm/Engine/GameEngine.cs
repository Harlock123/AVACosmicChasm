namespace AVACosmicChasm.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The station's map: its rooms, the core, and the way there.</summary>
    Map,
    /// <summary>Flying the rooms.</summary>
    Fight,
    /// <summary>Crashed: the wreck burns before a life is taken.</summary>
    Down,
    /// <summary>Out through the airlock in time: the station goes up behind.</summary>
    Escaped,
    /// <summary>Too late: the station goes up with the ship inside.</summary>
    Blown,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Fire, EnemyFire, Blast, ShipBoom, CoreBoom, CoreHit, PlateHit, Ping, Drill, DoorOpen, Warp, Emerge, Shield,
    Bounce, Alarm, Siren, ExtraLife, ThrustOn, ThrustOff,
    TitleTune, StationTune, EscapedTune, GameOver, MusicStop,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 15;
    public const double AttractSeconds = 75;
    public const double HighScoreSeconds = 9;
    public const double MapSeconds = 4.5, DownSeconds = 2.4, EscapedSeconds = 5, BlownSeconds = 3.5;

    private double _accumulator, _idle;
    private bool _startPrev, _firePrev, _tunePending = true, _thrustHeard;
    private readonly int _seed;
    private int _games, _stationSeed;

    public GameEngine(HighScoreTable? table = null, int seed = 1)
    {
        HighScores = table ?? HighScoreTable.Load();
        _seed = seed;
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Map;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Score => World.Score;
    public int StationNumber => World.Station.Number;
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }
    /// <summary>The points the last escape was worth.</summary>
    public int LastBonus { get; private set; }

    /// <summary>The escape bonus: a thousand a station, and a hundred for every second to spare.</summary>
    public static int EscapeBonus(int station, double secondsLeft) => 1000 * station + 100 * (int)secondsLeft;

    private void Play(Sfx s)
    {
        // The demonstration plays in silence, the way the arcade machines' attract modes mostly did.
        if (!Silent && State != GameState.Attract) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        bool firePressed = c.Fire && !_firePrev;
        _firePrev = c.Fire;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || firePressed && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(Pilot.Decide(this), false);
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(c, firePressed || startPressed);
                break;
            case GameState.GameOver:
                if (StateTime > 4)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, StationNumber);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        _stationSeed = _seed * 1000 + _games++;
        Sim.Start(World, _stationSeed);
        World.Heard.Clear();
        SetPhase(Phase.Map);
        Message = SubMessage = null;
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        Play(Sfx.MusicStop);
        Play(Sfx.StationTune);
        Callout();
    }

    private void StartAttract()
    {
        Play(Sfx.MusicStop);
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
        Pilot.Reset();
        Callout();
    }

    public void ReturnToTitle()
    {
        Thrust(false);
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Play(Sfx.MusicStop);
        Message = SubMessage = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        Thrust(false);
        Play(Sfx.MusicStop);
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        Thrust(false);
        State = GameState.Paused;
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    /// <summary>The thrust's roar: on while it's thrusting, off otherwise.</summary>
    private void Thrust(bool on)
    {
        if (on == _thrustHeard) return;
        _thrustHeard = on;
        Play(on ? Sfx.ThrustOn : Sfx.ThrustOff);
    }

    private void Callout() => Say($"STATION {StationNumber}", "FIND THE CORE AND DESTROY IT", MapSeconds);

    private void Simulate(Controls c, bool skip)
    {
        double dt = Field.Step;
        PhaseTime += dt;
        var w = World;
        int room = w.Room;
        bool core = w.CoreDestroyed;
        switch (Phase)
        {
            case Phase.Map:
                if (PhaseTime > MapSeconds || skip && PhaseTime > 0.8)
                {
                    SetPhase(Phase.Fight);
                    Message = SubMessage = null;
                }
                break;
            case Phase.Fight:
                Sim.Step(w, c);
                if (w.Escaped) { Escaped(); break; }
                if (w.Ship.Dead) { SetPhase(Phase.Down); break; }
                if (w.Escaping && w.EscapeTime <= 0) { Blown(); break; }
                if (w.Room != room && w.InCore && !w.CoreDestroyed) Say("THE CORE!", "BLAST THROUGH ITS SHIELD RINGS", 3);
                if (w.CoreDestroyed && !core) Say("CORE DESTROYED!", "GET BACK TO THE DOCKING BAY - FAST!", 3.5);
                break;
            case Phase.Down:
                Sim.Step(w, default);
                if (PhaseTime > DownSeconds) LoseLife(() => Sim.Revive(w), Phase.Fight);
                break;
            case Phase.Escaped:
                if (PhaseTime > EscapedSeconds) NextStation();
                break;
            case Phase.Blown:
                if (PhaseTime > BlownSeconds) LoseLife(NextStation, null);
                break;
        }
        Thrust(Phase == Phase.Fight && w.Ship is { Thrusting: true, Dead: false });
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    /// <summary>A life lost: the game's over with the last, or play goes on.</summary>
    private void LoseLife(Action then, Phase? next)
    {
        World.Lives--;
        if (World.Lives <= 0) { GameOver(); return; }
        then();
        if (next is { } p) SetPhase(p);
    }

    private void Escaped()
    {
        var w = World;
        LastBonus = EscapeBonus(StationNumber, w.EscapeTime);
        Sim.AddScore(w, LastBonus);
        SetPhase(Phase.Escaped);
        Say("STATION DESTROYED!", $"ESCAPE BONUS {LastBonus}", EscapedSeconds);
        Play(Sfx.CoreBoom);
        Play(Sfx.EscapedTune);
    }

    private void Blown()
    {
        var w = World;
        Sim.Crash(w);
        SetPhase(Phase.Blown);
        Say("TOO LATE!", "CAUGHT IN THE BLAST", BlownSeconds);
        Play(Sfx.CoreBoom);
    }

    private void NextStation()
    {
        var w = World;
        Sim.BeginStation(w, StationNumber + 1, _stationSeed);
        w.Heard.Clear();
        SetPhase(Phase.Map);
        Callout();
        Play(Sfx.StationTune);
    }

    private void GameOver()
    {
        Thrust(false);
        Play(Sfx.MusicStop);
        Play(Sfx.GameOver);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a station, past its map.</summary>
    public void Go(int station = 1)
    {
        NewGame();
        State = GameState.Playing;
        if (station > 1) Sim.BeginStation(World, station, _stationSeed);
        World.Heard.Clear();
        SetPhase(Phase.Fight);
        Message = null;
    }
}
