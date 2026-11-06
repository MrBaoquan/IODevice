using System.Windows.Forms;
using Microsoft.Win32;

namespace IOToolkit.WebBridge.Hosting;

public sealed class HiddenMessageWindow : IDisposable
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private Form? _form;

    /// <summary>
    /// Invoked synchronously on the message thread when Windows asks the session to end (log off,
    /// shutdown, restart). Keep it short: the OS terminates the process once this returns.
    /// </summary>
    public Action? SessionEnding { get; set; }

    public void Start(TimeSpan timeout)
    {
        if (_thread is not null)
            return;

        _thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                _form = new Form
                {
                    Text = "IOToolkit.WebBridge.Host",
                    FormBorderStyle = FormBorderStyle.FixedToolWindow,
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new System.Drawing.Point(-32000, -32000),
                    Size = new System.Drawing.Size(1, 1),
                    Opacity = 0,
                    Visible = false
                };
                _form.Load += (_, _) => _ready.TrySetResult();
                // WM_QUERYENDSESSION arrives on this thread's message pump. Returning from the handler
                // lets the shutdown proceed; leaving Cancel false keeps it non-blocking.
                Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
                Application.Run(_form);
            }
            catch (Exception ex)
            {
                _ready.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "IOToolkit.WebBridge.MessageWindow"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_ready.Task.Wait(timeout))
            throw new TimeoutException("Hidden Windows message window did not start in time.");
        _ready.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding; } catch { }
        if (_form is not null)
        {
            try { _form.BeginInvoke(new Action(() => _form.Close())); } catch { }
        }
        try { _thread?.Join(TimeSpan.FromSeconds(2)); } catch { }
        _form = null;
        _thread = null;
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        // Windows waits roughly five seconds for this handler before terminating the process, so the
        // callback must not block on anything slow.
        try { SessionEnding?.Invoke(); }
        catch { /* The session is ending; nothing useful left to report to. */ }
    }
}
