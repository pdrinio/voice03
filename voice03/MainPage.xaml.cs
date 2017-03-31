using System.Threading.Tasks;
using Windows.Storage;
using Windows.Media.SpeechRecognition;
using Windows.UI.Core;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Resources.Core;
using Windows.Globalization;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// La plantilla de elemento Página en blanco está documentada en https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0xc0a

namespace voice03
{
    /// <summary>
    /// Página vacía que se puede usar de forma independiente o a la que se puede navegar dentro de un objeto Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private SpeechRecognizer speechRecognizer;
        private IAsyncOperation<SpeechRecognitionResult> recognitionOperation;
        private CoreDispatcher dispatcher;
        private ResourceContext speechContext;
        private ResourceMap sppechResourceMap;
        

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;

            bool tengoPermiso = await AudioCapturePermissions.RequestMicrophonePermission();
            if (tengoPermiso)
            {
                Language speechLanguage = SpeechRecognizer.SystemSpeechLanguage;
                await InitializeRecognizer(SpeechRecognizer.SystemSpeechLanguage);
            } else
            {
                resultadosTB.Visibility = Visibility.Visible;
                resultadosTB.Text = "Sin acceso al micrófono";
            }
        }

        private async Task InitializeRecognizer(Language recognizerLanguage)
        {
            if (speechRecognizer != null)
            {
                speechRecognizer.StateChanged -= SpeechRecognizer_StateChanged;
            }
        }

        private async void SpeechRecognizer_StateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
        {
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
            resultadosTB.Text = "Speech recognizer state: " + args.State.ToString();
            });
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
