namespace StampJourney.UI
{
    public interface IScreen
    {
        string ScreenName { get; }
        void Show();
    }
}