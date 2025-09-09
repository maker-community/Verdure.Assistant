namespace Verdure.Assistant.MAUI;

public partial class MainPage : ContentPage
{
    int count = 0;

    public MainPage()
    {
        InitializeComponent();
    }

    private void OnCounterClicked(object sender, EventArgs e)
    {
        count++;

        if (count == 1)
            CounterButton.Text = $"Clicked {count} time";
        else
            CounterButton.Text = $"Clicked {count} times";

        SemanticScreenReader.Announce(CounterButton.Text);
    }
}
