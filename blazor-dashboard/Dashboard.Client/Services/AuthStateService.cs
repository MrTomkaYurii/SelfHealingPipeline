namespace Dashboard.Client.Services;

public class AuthStateService
{
    public bool IsAuthenticated { get; private set; }
    public string Username { get; private set; } = "";

    public event Action? StateChanged;

    public void Login(string username)
    {
        IsAuthenticated = true;
        Username = username;
        StateChanged?.Invoke();
    }

    public void Logout()
    {
        IsAuthenticated = false;
        Username = "";
        StateChanged?.Invoke();
    }
}
