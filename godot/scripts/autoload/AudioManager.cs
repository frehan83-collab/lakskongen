using Godot;
using System.Collections.Generic;

/// <summary>
/// Global audio manager for handling music, sound effects, and ambient audio.
/// Manages audio layers, volume control, and dynamic audio based on game state.
///
/// Audio Resource Structure:
/// - res://audio/ui/*.ogg - UI sounds (button clicks, notifications, alerts)
/// - res://audio/ambient/*.ogg - Ambient environmental sounds (weather, sea)
/// - res://audio/music/*.ogg - Background music tracks (seasonal themes)
/// - res://audio/sfx/*.ogg - Game sound effects
///
/// All paths are configured via constants and dictionaries at the top of this class.
/// To add new audio, update the corresponding dictionary (AMBIENT_SOUNDS, SEASON_MUSIC, etc.)
/// </summary>
public partial class AudioManager : Node
{
    [Signal]
    public delegate void AudioVolumeChangedEventHandler(string busName, float volume);

    // Audio players for different layers
    private AudioStreamPlayer _musicPlayer;
    private AudioStreamPlayer _ambientPlayer;
    private AudioStreamPlayer _uiPlayer;
    
    // Audio buses
    private const string MASTER_BUS = "Master";
    private const string MUSIC_BUS = "Music";
    private const string SFX_BUS = "SFX";
    private const string AMBIENT_BUS = "Ambient";
    
    // === Audio Resource Paths Configuration ===
    // Base directories for audio resources
    private const string AUDIO_BASE_DIR = "res://audio/";
    private const string UI_AUDIO_DIR = AUDIO_BASE_DIR + "ui/";
    private const string AMBIENT_AUDIO_DIR = AUDIO_BASE_DIR + "ambient/";
    private const string MUSIC_AUDIO_DIR = AUDIO_BASE_DIR + "music/";
    
    // File extension for audio files
    private const string AUDIO_EXTENSION = ".ogg";
    
    // Ambient sound paths
    private static readonly Dictionary<string, string> AMBIENT_SOUNDS = new()
    {
        ["storm"] = AMBIENT_AUDIO_DIR + "storm" + AUDIO_EXTENSION,
        ["rain"] = AMBIENT_AUDIO_DIR + "rain" + AUDIO_EXTENSION,
        ["sunny"] = AMBIENT_AUDIO_DIR + "calm_sea" + AUDIO_EXTENSION,
        ["default"] = AMBIENT_AUDIO_DIR + "default" + AUDIO_EXTENSION
    };
    
    // Season music paths
    private static readonly Dictionary<string, string> SEASON_MUSIC = new()
    {
        ["spring"] = MUSIC_AUDIO_DIR + "spring_theme" + AUDIO_EXTENSION,
        ["summer"] = MUSIC_AUDIO_DIR + "summer_theme" + AUDIO_EXTENSION,
        ["autumn"] = MUSIC_AUDIO_DIR + "autumn_theme" + AUDIO_EXTENSION,
        ["winter"] = MUSIC_AUDIO_DIR + "winter_theme" + AUDIO_EXTENSION,
        ["default"] = MUSIC_AUDIO_DIR + "main_theme" + AUDIO_EXTENSION
    };
    
    // Volume settings
    private float _masterVolume = 1.0f;
    private float _musicVolume = 0.7f;
    private float _sfxVolume = 0.8f;
    private float _ambientVolume = 0.5f;
    
    // Audio pools for sound effects
    private Dictionary<string, AudioStreamPlayer> _sfxPlayers = new();
    private Queue<AudioStreamPlayer> _availableSfxPlayers = new();
    
    // Current audio state
    private AudioStream _currentMusic;
    private AudioStream _currentAmbient;
    private bool _isMusicMuted = false;

    public override void _Ready()
    {
        SetupAudioBuses();
        SetupAudioPlayers();
        ConnectToEventBus();
        
        GD.Print("AudioManager initialized");
    }

    private void SetupAudioPlayers()
    {
        // Music player
        _musicPlayer = new AudioStreamPlayer();
        _musicPlayer.Bus = MUSIC_BUS;
        AddChild(_musicPlayer);
        
        // Ambient player
        _ambientPlayer = new AudioStreamPlayer();
        _ambientPlayer.Bus = AMBIENT_BUS;
        AddChild(_ambientPlayer);
        
        // UI sound player
        _uiPlayer = new AudioStreamPlayer();
        _uiPlayer.Bus = SFX_BUS;
        AddChild(_uiPlayer);
        
        // Create pool of SFX players
        for (int i = 0; i < 10; i++)
        {
            var sfxPlayer = new AudioStreamPlayer();
            sfxPlayer.Bus = SFX_BUS;
            sfxPlayer.Finished += () => ReturnSfxPlayerToPool(sfxPlayer);
            AddChild(sfxPlayer);
            _availableSfxPlayers.Enqueue(sfxPlayer);
        }
    }

    private void SetupAudioBuses()
    {
        // Guarantee the buses exist (project bus layout covers editor/real
        // runs; headless/dummy-driver boots may not apply it).
        EnsureBus(MUSIC_BUS);
        EnsureBus(SFX_BUS);
        EnsureBus(AMBIENT_BUS);

        // Set initial volume levels
        SetBusVolume(MASTER_BUS, _masterVolume);
        SetBusVolume(MUSIC_BUS, _musicVolume);
        SetBusVolume(SFX_BUS, _sfxVolume);
        SetBusVolume(AMBIENT_BUS, _ambientVolume);
    }

    private void EnsureBus(string busName)
    {
        if (AudioServer.GetBusIndex(busName) == -1)
        {
            AudioServer.AddBus();
            AudioServer.SetBusName(AudioServer.BusCount - 1, busName);
        }
    }

    private void ConnectToEventBus()
    {
        // Connect to EventBus signals for dynamic audio responses
        var eventBus = GetNode<EventBus>("/root/EventBus");
        
        // Example connections (uncomment when EventBus signals are available)
        // eventBus.DiseaseOutbreak += OnDiseaseOutbreak;
        // eventBus.HarvestCompleted += OnHarvestCompleted;
        // eventBus.WeatherChanged += OnWeatherChanged;
        // eventBus.SeasonChanged += OnSeasonChanged;
    }

    // === Public Interface ===
    
    /// <summary>
    /// Play background music with smooth crossfade
    /// </summary>
    public void PlayMusic(string musicPath, bool loop = true, float fadeTime = 1.0f)
    {
        var newMusic = GD.Load<AudioStream>(musicPath);
        if (newMusic == null)
        {
            GD.PrintErr($"Failed to load music: {musicPath}");
            return;
        }

        if (_currentMusic == newMusic) return;
        
        _currentMusic = newMusic;
        _musicPlayer.Stream = newMusic;
        
        if (loop && _musicPlayer.Stream is AudioStreamOggVorbis oggStream)
        {
            oggStream.Loop = true;
        }
        
        if (!_isMusicMuted)
        {
            _musicPlayer.Play();
        }
        
        GD.Print($"Playing music: {musicPath}");
    }

    /// <summary>
    /// Play ambient background sound
    /// </summary>
    public void PlayAmbient(string ambientPath, bool loop = true)
    {
        var newAmbient = GD.Load<AudioStream>(ambientPath);
        if (newAmbient == null)
        {
            GD.PrintErr($"Failed to load ambient sound: {ambientPath}");
            return;
        }

        _currentAmbient = newAmbient;
        _ambientPlayer.Stream = newAmbient;
        
        if (loop && _ambientPlayer.Stream is AudioStreamOggVorbis oggStream)
        {
            oggStream.Loop = true;
        }
        
        _ambientPlayer.Play();
        GD.Print($"Playing ambient: {ambientPath}");
    }

    /// <summary>
    /// Play a sound effect
    /// </summary>
    public void PlaySfx(string sfxPath, float volumeDb = 0.0f, float pitch = 1.0f)
    {
        var sfxStream = GD.Load<AudioStream>(sfxPath);
        if (sfxStream == null)
        {
            GD.PrintErr($"Failed to load SFX: {sfxPath}");
            return;
        }

        var player = GetSfxPlayer();
        if (player == null) return;

        player.Stream = sfxStream;
        player.VolumeDb = volumeDb;
        player.PitchScale = pitch;
        player.Play();
    }

    /// <summary>
    /// Play UI sound (button clicks, notifications, etc.)
    /// </summary>
    public void PlayUiSound(string soundName)
    {
        string sfxPath = UI_AUDIO_DIR + soundName + AUDIO_EXTENSION;
        PlaySfx(sfxPath);
    }

    /// <summary>
    /// Set volume for audio bus
    /// </summary>
    public void SetBusVolume(string busName, float volume)
    {
        int busIndex = AudioServer.GetBusIndex(busName);
        if (busIndex == -1)
        {
            GD.PrintErr($"Audio bus not found: {busName}");
            return;
        }

        float volumeDb = volume > 0 ? Mathf.LinearToDb(volume) : -80.0f;
        AudioServer.SetBusVolumeDb(busIndex, volumeDb);
        
        EmitSignal(SignalName.AudioVolumeChanged, busName, volume);
    }

    /// <summary>
    /// Mute/unmute music
    /// </summary>
    public void SetMusicMuted(bool muted)
    {
        _isMusicMuted = muted;
        
        if (muted)
        {
            _musicPlayer.Stop();
        }
        else if (_currentMusic != null)
        {
            _musicPlayer.Play();
        }
    }

    // === Private Methods ===

    private AudioStreamPlayer GetSfxPlayer()
    {
        if (_availableSfxPlayers.Count > 0)
        {
            return _availableSfxPlayers.Dequeue();
        }
        
        GD.PrintErr("No available SFX players!");
        return null;
    }

    private void ReturnSfxPlayerToPool(AudioStreamPlayer player)
    {
        _availableSfxPlayers.Enqueue(player);
    }

    // === Event Handlers ===

    private void OnDiseaseOutbreak(int farmId, string diseaseName, int affectedFish)
    {
        PlayUiSound("alert_disease");
    }

    private void OnHarvestCompleted(int farmId, int fishCount, float avgWeight, long revenue)
    {
        PlayUiSound("harvest_complete");
    }

    private void OnWeatherChanged(string weatherType, float temperature, string impact)
    {
        // Play weather-appropriate ambient sounds
        string weatherKey = weatherType.ToLower();
        string ambientPath = AMBIENT_SOUNDS.GetValueOrDefault(weatherKey, AMBIENT_SOUNDS["default"]);
        
        PlayAmbient(ambientPath);
    }

    private void OnSeasonChanged(string newSeason, string effects)
    {
        // Change background music based on season
        string seasonKey = newSeason.ToLower();
        string musicPath = SEASON_MUSIC.GetValueOrDefault(seasonKey, SEASON_MUSIC["default"]);
        
        PlayMusic(musicPath);
    }

    public override void _ExitTree()
    {
        // Cleanup audio players
        _musicPlayer?.Stop();
        _ambientPlayer?.Stop();
        _uiPlayer?.Stop();
        
        foreach (var player in _sfxPlayers.Values)
        {
            player?.Stop();
        }
    }
}