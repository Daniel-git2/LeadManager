using System.Windows;

namespace LeadManager.Views;

/// <summary>Asks for one line of text, e.g. a template name.</summary>
public partial class PromptWindow : Window
{
    public PromptWindow(string title, string prompt, string initialText)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        AnswerBox.Text = initialText;
        Loaded += (_, _) =>
        {
            AnswerBox.Focus();
            AnswerBox.SelectAll();
        };
    }

    public string Answer => AnswerBox.Text;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
