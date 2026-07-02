namespace XboxMetroLauncher.Services;

public interface IAudioService
{
    void Play(string soundName);
    void Stop(string soundName);
}
